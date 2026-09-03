# Resource Guru migration

SRA's resourcing lives in **Resource Guru** today. This folder holds a report
export from it, and this file records how that export maps onto the SRA-RMS
model (FR-IMP-1..7 in `docs/Requirements.md` §4.13).

> **The export files are not in git.** They carry real staff names, emails,
> phone numbers, security clearances and client project data (NFR-SEC-5), and
> run to ~60 MB. `.gitignore` excludes everything here except this README.

## How to run an import

**In the app** — *Data Import* in the sidebar. Choose the export, press
**Analyse**, read the report, then press **Import**. Administrator only.

**Analysing writes nothing.** The API runs the entire import inside a
transaction and rolls it back, so a preview exercises every foreign key, unique
index and check constraint the real import will, and reports exactly the counts
the real import will produce. That is why *Import* only unlocks after *Analyse*.

**Over the API** (`dryRun` defaults to `true`):

```bash
curl -X POST "http://localhost:5163/v1/import/resource-guru?dryRun=false" \
  -F "file=@Resource Guru Report Thu 1 January - Wed 30 September 2026.zip"
```

Re-running is safe. Records that already exist are matched on their natural key
and left untouched, so a second import tops up rather than duplicating:

| Entity | Matched on |
|---|---|
| Client | name (case-insensitive) |
| Project | code |
| Resource | email (case-insensitive) |
| Allocation | project + person + window + details + billability + booker + status |
| Time off | overlapping window for the same person |
| Pick-lists | value (case-insensitive) |

## The sheets

The export is a `.zip` of CSV sheets (a single extracted `.csv` is also
accepted). Sheets are resolved by the filename fragment Resource Guru uses, so
the date range in the name does not matter.

| Sheet | Rows (1 Jan – 30 Sep 2026) | Used for |
|---|---|---|
| `Resource Data` | 82 | The person master list — the only sheet that is required |
| `Availability Data` | 22,386 | Weekly availability and working days per person |
| `Bookings Data` | 25,002 | Allocations |
| `Downtime Data` | 1,648 | Time off |
| `Scheduled Vs Actuals` | 7,748 | Project catalogue and date ranges, incl. projects with no bookings |
| `Utilization Data` | 89,544 | **Not read** — derived percentages SRA-RMS recomputes |
| `Timesheets Data` | 0 | **Not read** — SRA-RMS does not model timesheet actuals |
| `Report ....xlsx` | — | **Not read** — the same data as the CSVs |

## Field mapping

### Resource Data → `resource`

| Source column | Target | Notes |
|---|---|---|
| `Name`, `Email` | `name`, `email` | Email is the identity |
| `Resource Field: Job Title` | `primary_job_title` | NOT NULL; falls back to Job Role, then `Unspecified` |
| `Resource Field: Job Role` | `job_role` | Distinct from job title — the reference app shows both |
| `Resource Field: Department`, `Location`, `Manager`, `Phone` | `department`, `location`, `manager_id`, `phone` | Manager resolves by name |
| `Resource Field: Primary Skills` | `skills` | Comma-separated, split and de-duplicated |
| `Resource Field: Secondary Skills` | `secondary_skills` | |
| `Resource Field: Security Clearances` | `security_clearances` | |
| `Resource Field: Security NPC (date obtained)` | `security_npc_obtained_on` | `d/M/yyyy` — day-first, unlike the ISO dates in the fact sheets |
| `Resource Field: Staff Certifications` | `certifications` | |
| `Rate` | `default_rate_hourly` | Blank throughout this export |
| — | `status`, `bookable_status` | `active` / `bookable`; the export has no equivalent |

### Availability Data → `resource.availability_hours_per_week`, `working_days`

Resource Guru **zeroes `Available Hours` on any day taken by leave or a public
holiday**, so only the non-zero rows describe a person's contract. The importer
takes the modal non-zero value per weekday (ties to the longer day): a mean is
dragged down by part weeks at the edges of the window, a maximum is pulled up by
one overtime day.

A weekday with no non-zero observation is not a working day. If fewer than two
weekdays qualify, the window simply never saw that person working — someone on
long-term leave — and they get a standard Monday–Friday week plus an
`availability.notObserved` warning, rather than zero capacity.

People missing from the sheet entirely get a standard 40-hour Monday–Friday week.

### Bookings Data → `allocation`

Resource Guru stores a booking as **one row per day**, so a booking has to be
reassembled. Rows are grouped by person, project, `Details`, `Booker`,
`Billable` and `Booking Status`, their hours summed per day, and then
consecutive days with equal hours are folded into one allocation. Two dates join only when **every date
between them is a non-working day for that person** — so a Monday–Friday booking
repeated over four weeks is one allocation, while a real mid-week break, or a
public holiday nobody booked, still splits it.

| Source column | Target | Notes |
|---|---|---|
| `Hours` (per day) | `effort` + `effort_unit` | Converted: `effort = daily hours × the person's working days per week`, unit `hoursPerWeek` |
| `Date` range of the run | `start_date`, `end_date` | |
| `Details` | `details` | |
| `Booker` | `booker_id` | Resolved by name; business data, distinct from the `created_by` audit stamp |
| `Billable` | `billable` | `non-billable` → false |
| `Project`, `Project Code`, `Client` | the project it belongs to | See below |
| `Booking Status` | `booking_status` | `confirmed` / `tentative` / `waiting`, mapped one-for-one (V004). Part of a booking's identity, so a tentative day never folds into a confirmed one |
| `Approval Status` | **not imported** | `approved` for every row in this export |
| `Rate`, `Billable Rate Total` | `hourly_rate` | Blank throughout this export |
| `Days`, `Year`/`Quarter`/`Month`/`Week Number`, `Resource Field: *` | **not imported** | Derived from other columns, or already on the person |

### Downtime Data → `time_off`

Grouped by person, type and `Details`, then folded into ranges the same way.

| Resource Guru type | `time_off.type` |
|---|---|
| Public holiday | `publicHoliday` |
| Holiday (personal) | `annualLeave` |
| Sick leave | `sick` |
| Parental leave, No type assigned | `other` |

`hours_per_day` is left null — meaning the whole working day is gone — unless the
downtime is genuinely shorter than the shortest working day the range covers.
The *shortest*, not the average: a person's days differ in length, and against an
average a normal eight-hour day off an 8.01-hour average reads as a part day. A
0.05-hour tolerance absorbs the rounding wobble in the source, which writes both
`8` and `8.0166` for the same nominal eight-hour day.

### Projects and clients

The Bookings sheet carries no project id, so a project's identity is its
(client, name, code) text. Two consequences:

- **Codes are unique in SRA-RMS** (`ux_project_code`, FR-PRJ-1) but not in
  Resource Guru — a change request gets booked against its parent's code. The
  second project to claim a code gets `-2` appended and a
  `project.duplicateCode` warning.
- **Projects with no code** (bench time, "In the Office", "No project assigned")
  get a generated `RG-<project>-<client>` code. It is derived from the names, so
  a re-import lands on the same project rather than making a new one.

`start_date` / `end_date` come from the first and last date the project appears
on, across both the Bookings and Scheduled Vs Actuals sheets. An existing
project whose window does not cover the imported bookings is **widened**, so
allocations stay inside their project window (FR-ALL-5).

`status` is derived from that window against the business date (`App:TimeZone`):
ended → `completed`, not started → `planned`, otherwise `active`. The export has
no status column, and leaving everything `planned` would empty the dashboard.

Resource Guru's `No client assigned` and `No project assigned` placeholders are
imported under those names rather than dropped, so the bookings behind them are
not lost. A `client.placeholder` warning says so; reassign and delete them once
migrated.

## Known gaps

Everything below is reported by the import, not silent. As of V004 no source
*column* is dropped: the response's `unmappedFields` comes back empty for the
SRA export.

- **Booking status is descriptive.** Tentative and waiting bookings import with
  their status and are drawn as provisional, but they still count toward
  capacity, over-allocation warnings and the utilisation ratio — see
  `docs/Requirements.md` §3.4. About one booking row in six of the SRA export is
  unconfirmed; the import reports the figure as a `booking.unconfirmed` warning.
- **Timesheet actuals.** Not modelled. The `Total Actual Hours` columns are zero
  throughout this export anyway.
- **Managers and bookers outside the export.** Names such as *Steve Rowe* and
  *Peter Black* appear as managers or bookers but are not themselves resources;
  those references are left unset and warned about.
- **`Admin Admin`.** A Resource Guru service account, not a person. Bookings it
  made import with no booker.
- **Effort is a weekly figure.** SRA-RMS stores `hoursPerWeek`; Resource Guru
  books per day. The conversion multiplies by the person's working days per
  week, so a part-time week converts correctly, but a booking that spanned an
  unusual pattern is smoothed.
