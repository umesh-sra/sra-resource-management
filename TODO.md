# SRA-RMS — TODO

Living backlog for the SRA Resource Management System. Status as of 2026-09-02.
See `docs/Requirements.md` for the full spec and `docs/openapi.yaml` for the API contract.

## Done

- [x] **Database** — `db/migrations/V001__initial_schema.sql` applied to local `sra_rms`; dev seed in `db/seed/dev_seed.sql`.
- [x] **Business API** — `src/SraRms.Api` implements every endpoint in `docs/openapi.yaml` (CRUD, dashboard, Gantt, reports, reference data), RBAC policies, RFC 9457 errors, over-allocation warnings.
- [x] **Tests (initial)** — `tests/SraRms.Api.Tests`: AllocationService unit tests + integration tests for clients, allocations, resources, reports (Testcontainers Postgres). 18 passing.
- [x] **Front end (initial)** — `web/` Vue 3 + Vite + TS SPA: layout/nav, dashboard, clients, projects, resources (all list + create + detail), allocations, utilisation report. Wired to the live API.
- [x] **Security headers (NFR-SEC-1, NFR-SEC-4)** — 2026-07-05: API middleware sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, strict CSP (Swagger exempt), and default `Cache-Control: no-store` on every response, + HSTS/HTTPS-redirect outside Development (integration-tested in `SecurityHeadersTests`); Vite dev/preview servers send the non-CSP subset; production hosting CSP documented in `web/README.md`. Note: the deployment guide (below) must wire forwarded headers if TLS terminates at a proxy.
- [x] **Reference-app UI rebuild** — 2026-09-02: the SPA now follows the information architecture and screen layouts in `screens/` (Dashboard agenda + rail, Schedule people timeline, Gantt charts, People & Resources card grid with a person drawer, tabbed Projects & Clients, Reports rail + chart), rendered in the SRA palette rather than the reference's purple (NFR-USE-1). Adds `TimelineGrid`, `SidePanel`, `PersonPanel`, `CollapsibleSection`, `AppAvatar`/`AvatarStack`, tabbed `ProjectFormModal` and sectioned `PersonFormModal`, plus page-scroll locking for overlays. API side: `Project.team` roster on list/detail and `effortUnit` on Gantt bars (both added to `docs/openapi.yaml`), covered by new `ProjectsTests`. See `web/README.md` for the screen map and the list of reference features deliberately not built.
- [x] **Gantt UI (FR-GANTT-*)** — 2026-09-02: `/gantt` renders `/dashboard/gantt` for both the projects and resources views; `/schedule` is the day-level people view.
- [x] **Accessibility pass (NFR-USE-2, WCAG 2.1 AA)** — resolves review finding W-M1 (2026-07-05): skip link, keyboard-reachable row links, modal focus trap/Escape/labelling, toast live region + keyboard dismiss, `label for=` on all fields, `th scope`, visible focus indicators, contrast-compliant tokens (muted text, amber, success toast, input borders), single `h1` per page, reduced-motion support.

- [x] **Reference-app data model (V002)** — 2026-09-02: `db/migrations/V002__reference_app_model.sql` adds `project_phase`, `project_milestone`, `time_off` and the `activity_type` pick-list; extends `resource` with the full person profile (job role, manager, phone, secondary skills, security clearances + NPC date, certifications, time zone, bookable status, public holiday calendar, default rate, colour), `project` with budget type / hour budgets / activity types / details / colour, and `allocation` with an hourly rate. SRS raised to v1.1 (§3.6–§3.9, FR-PHASE-*, FR-MILE-*, FR-TIMEOFF-*, FR-RES-8/9, FR-PRJ-8/9, FR-ALL-8, FR-REP-6); `docs/openapi.yaml` gains the matching schemas and the `/projects/{id}/phases`, `/projects/{id}/milestones` and `/timeoff` paths. 17 new integration tests (`ReferenceModelTests`), 53 passing. The test fixture now applies **all** `V*.sql` in order, so future migrations need no fixture edit.

- [x] **UTC/local business-date mismatch** — 2026-09-02: `DashboardController` and `ResourcesController` derived "today" from `DateTime.UtcNow`, so for the first ~10 hours of each Australian day the API and SPA disagreed about the date (a project starting that day vanished from the dashboard horizon while the agenda still listed it). Both now use the injected `BusinessClock`, which resolves the date in the configurable `App:TimeZone`; the dashboard response carries `today` + `timeZone` so the SPA anchors on the server's business date instead of the browser's. Audit timestamps deliberately stay UTC. 6 unit tests in `BusinessClockTests` cover the boundary and a DST transition.

## Open items from the V002 model

- [ ] **Time off does not yet reduce over-allocation warnings.** `AllocationService` still compares effort against gross weekly availability, so a person fully on leave is not flagged differently. Reporting *does* account for leave (`effectiveCapacityHours`). Decide whether the warning should use effective capacity — this is open question #7 in `docs/Requirements.md` §8.
- [ ] **Public holiday calendar is stored but not expanded** — `publicHolidayCalendar` holds a region key; nothing generates `publicHoliday` time-off rows from it (§8 #8).
- [ ] **Manager cycles** — only direct self-reference is rejected; A → B → A is possible. A full ancestry check needs a recursive query on every write (§8 #9).
- [ ] **Time off can be created but not edited from the UI.** Picking a day on the Schedule opens `ScheduleEntryModal` (Booking / Time Off), so leave can now be added; editing and deleting an existing leave record still has no dialog — clicking a hatched bar is a no-op.
- [ ] **Allocation hourly rate is only in `AllocationEditModal`.** The controllers were silently discarding `hourlyRate` on create and update and the contract omitted it entirely; both are fixed in V003's change set, and the allocation edit dialog now exposes the field. The project Team tab still does not.
- [ ] **Phases do not drive allocation** — they are presentational only (§8 #10).
- [ ] **Reference booking fields still not modelled.** *Details* and *Booker* landed in V003; `screens/shedule_booking.png` also carries *Activity Type*, *Tentative*, *Add Repeat* and *Specific Time*. `Project.activityTypes` exists but an allocation has no activity-type column, so the booking dialog omits these four rather than discarding input. Adding any of them is a contract change to `Allocation` in `docs/openapi.yaml`.
- [ ] **Booker cannot default to the signed-in user.** The reference preselects the current user; nothing maps the authenticated AD identity onto a `resource` row (the SPA's user chip is hard-coded "Dev User"), so both dialogs default to "No booker recorded". Needs a `/me` endpoint resolving the principal to a resource, which depends on the AD group/identity mapping in §8.

## Next up — additional test slices

Extend `tests/SraRms.Api.Tests` to cover the gaps below. Each is independent.

### RBAC / 403 behaviour
The Dev auth handler currently grants all three roles, so role restrictions are never exercised. Make per-test role control possible, then assert it.
- [ ] Allow tests to run as a specific role set (e.g. a test auth scheme reading roles from a request header, or per-request role configuration on the factory) instead of the all-roles Dev user.
- [ ] **General** role: can `GET` clients/projects/resources/allocations/dashboard (200) but `POST`/`PUT`/`DELETE` are **403**.
- [ ] **Report** role: can hit `/v1/reports/*` (200) but is **403** on `GET /v1/clients` and on writes.
- [ ] **Administrator** role: writes succeed (201/200/204).
- [ ] A request with **no/empty roles** is **403** on protected endpoints (and document that unauthenticated → 401 once real Entra auth is wired).

### Dashboard
- [ ] `GET /v1/dashboard/summary`: with seeded allocations active "today", assert `activeProjects`, `totalResources`, `averageUtilisation`, and `overAllocatedResources`/`underAllocatedResources` counts. Use a window that makes allocations current so utilisation is non-zero.
- [ ] `budgetAtRisk`: a project consumed ≥90% (budget vs remaining) is included; one below threshold is excluded.
- [ ] `upcomingProjectStarts` / `upcomingRollOffs`: respect the `from`/`to` horizon.
- [ ] `GET /v1/dashboard/gantt?view=projects`: rows per project, bars clipped to the requested window.
- [ ] `GET /v1/dashboard/gantt?view=resources`: bars carry `overAllocated=true` when overlapping weekly hours exceed availability.
- [ ] `view` other than `projects`/`resources` → **400**; `to` before `from` → **400**.

### Reference data
- [ ] `GET /v1/reference/{collection}` for `departments`, `locations`, `jobTitles`, `skills` returns seeded values; `resourceStatuses` returns the enum tokens (`active`, `inactive`, `onLeave`).
- [ ] `POST /v1/reference/{departments}` creates a value (201); duplicate (case-insensitive) → **409**.
- [ ] `POST /v1/reference/resourceStatuses` → **400** (fixed enumeration, not extendable).
- [ ] Unknown collection (e.g. `/v1/reference/widgets`) → **404**.

## Backlog (not yet started)

- [ ] **Front-end polish** — role-aware UI (hide writes for non-admins), reference-data pick-lists in forms (`/reference/*` is implemented server-side but the new person/project dialogs still use free-text for job title, department and location).
- [x] **Resource Guru import (FR-IMP-*)** — 2026-09-03: `POST /v1/import/resource-guru` (multipart, Administrator-only, `dryRun` defaulting to true) plus the **Data Import** screen in the SPA. Reads the report `.zip` (or a single `.csv`), reassembles Resource Guru's one-row-per-day bookings into allocations, derives availability/working days/project status the export does not carry, and reports rows read, created/updated/skipped per entity, aggregated warnings and the columns deliberately not imported. Whole run is one transaction; `dryRun` rolls it back, so a preview and its commit report identical counts. No schema change. SRS raised to v1.2 (§4.13); `docs/openapi.yaml` gains `/import/resource-guru` and the `ImportReport` schemas; mapping in `data-migration/README.md`. 18 new integration tests, 82 passing. The SRA export (1 Jan – 30 Sep 2026) loads in ~3 s: 44 clients, 98 projects, 82 people, 2,475 allocations, 946 time-off records, 130 pick-list values, nothing dropped.

- [x] **Booking status on allocations (V004)** — 2026-09-03: `db/migrations/V004__allocation_booking_status.sql` adds the `booking_status` enum and `allocation.booking_status` (NOT NULL, default `confirmed`, partial index on the unconfirmed). Writable on create and update, filterable via `?bookingStatus=`, carried on Gantt bars, and reported as `unconfirmedHours` on the utilisation report. Deliberately **descriptive**: no capacity arithmetic keys off it, so over-allocation warnings, the dashboard and the utilisation ratio are unchanged and a tentative booking still warns when it would exceed availability (SRS §3.4, FR-ALL-9, FR-REP-7; settles open question 10). The importer maps `Booking Status` one-for-one and treats it as part of a booking's identity, so `unmappedFields` is now empty for the SRA export. SPA: status picker in both booking dialogs, dashed bars plus an "Unconfirmed only" filter on the Schedule, a Status column on the project team table, and a "Not yet firm" figure in Reports. SRS raised to v1.3. 6 new tests, 88 passing. Re-imported: 1,683 confirmed / 873 tentative / 239 waiting allocations.

- [ ] **Person photos** — the person dialog uploads via `PUT /resources/{id}/image`, but there is no way to remove a photo, and the people grid falls back to initials tiles. The reference grid is photo-led, so bulk import / upload UX is worth revisiting.
- [ ] **Reference features not built** (no data model behind them) — Timesheets, Time Off, project Phases and Milestones, hour-based budgets, per-person charge-out rates. Each needs a schema + `docs/openapi.yaml` change before any UI.
- [ ] **Real Entra ID auth** — both tiers: replace `AzureAd` placeholders + AD group→role mapping (SRS #5) in the API, and attach MSAL bearer tokens in `web/src/api/http.ts`.
- [ ] **CI** — pipeline to run `dotnet build` + `dotnet test` (Docker available on the runner for integration tests).
- [ ] **Deployment guide & artifacts** (SRS §7).
- [ ] Serve uploaded resource images (static files / blob storage) and validate image size/type.

## Open questions to resolve (SRS §8)

These affect schema/behaviour; decisions should land before they get expensive.
- [ ] Effort unit: hours/week, percent, or both? (currently both, per `EffortUnit`)
- [ ] `remaining` budget — maintained manually or derived from actuals? (currently a stored column)
- [ ] Retain historical allocations / soft-delete? (currently physical delete, no history)
- [ ] Multi-currency / multi-time-zone in v1? (currently single)
- [ ] Exact AD groups → role mapping.
- [ ] Full before/after audit history, or last-modified attribution? (currently the latter)
