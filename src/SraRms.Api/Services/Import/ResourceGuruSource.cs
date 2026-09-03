using SraRms.Api.Data;

namespace SraRms.Api.Services.Import;

/// <summary>Identity of a project in the export: its client, name and code.</summary>
/// <remarks>
/// The Bookings sheet carries no project id, so the three text columns are the
/// only join key back to a project. Two Resource Guru projects legitimately
/// share a code (a change request booked against its parent's code), which is
/// why the code alone will not do.
/// </remarks>
public sealed record RgProjectKey(string Client, string Name, string Code);

/// <summary>A row of the person master list.</summary>
public sealed class RgResource
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? JobTitle { get; init; }
    public string? JobRole { get; init; }
    public string? Department { get; init; }
    public string? Location { get; init; }
    public string? Manager { get; init; }
    public string? Phone { get; init; }
    public decimal? Rate { get; init; }
    public DateOnly? NpcObtainedOn { get; init; }
    public List<string> PrimarySkills { get; init; } = [];
    public List<string> SecondarySkills { get; init; } = [];
    public List<string> Clearances { get; init; } = [];
    public List<string> Certifications { get; init; } = [];
}

/// <summary>A person's bookable pattern, reduced from the daily Availability sheet.</summary>
public sealed class RgAvailability
{
    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Bookable hours by <see cref="DayOfWeek"/> index (0 = Sunday).</summary>
    public decimal[] HoursByDay { get; } = new decimal[7];

    public bool IsWorkingDay(DateOnly date) => HoursByDay[(int)date.DayOfWeek] > 0;

    public int WorkingDaysPerWeek => HoursByDay.Count(h => h > 0);

    public decimal WeeklyHours => Math.Round(HoursByDay.Sum(), 2);

    /// <summary>Bookable hours on a given date; zero on a non-working day.</summary>
    public decimal HoursOn(DateOnly date) => HoursByDay[(int)date.DayOfWeek];

    /// <summary>
    /// The pattern to fall back on when the export window barely saw this person
    /// working. Someone on long-term leave has almost every day zeroed, which
    /// would otherwise record them as having no capacity at all.
    /// </summary>
    public static RgAvailability StandardWeek(decimal dayHours)
    {
        var profile = new RgAvailability();
        foreach (var d in MondayFirst.Where(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday)))
            profile.HoursByDay[(int)d] = dayHours;
        return profile;
    }

    /// <summary>Working days, Monday first, as the person dialog lists them.</summary>
    public List<Weekday> WorkingDays()
    {
        var days = new List<Weekday>();
        foreach (var d in MondayFirst)
            if (HoursByDay[(int)d] > 0)
                days.Add(ImportMaps.ToWeekday(d));
        return days;
    }
}

/// <summary>A project as the export describes it, aggregated over its daily rows.</summary>
public sealed class RgProject
{
    public required RgProjectKey Key { get; init; }
    public DateOnly? First { get; private set; }
    public DateOnly? Last { get; private set; }
    public bool AnyBillable { get; set; }
    public bool HasBookings { get; set; }
    public HashSet<string> ActivityTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(DateOnly date)
    {
        if (First is null || date < First) First = date;
        if (Last is null || date > Last) Last = date;
    }
}

/// <summary>
/// One run of bookings that share everything but their dates. Resource Guru
/// stores a booking as one row per day, so a booking has to be reassembled from
/// the rows that agree on person, project, details, booker, billability and
/// status. Status belongs in the key: a tentative day and a confirmed day are
/// two different bookings, and folding them together would silently promote the
/// tentative half to firm.
/// </summary>
public sealed record RgBookingKey(
    string Email, RgProjectKey Project, string? Details, string? Booker, bool Billable,
    BookingStatus Status);

/// <summary>The same idea for leave: person, leave type and details.</summary>
public sealed record RgDowntimeKey(string Email, TimeOffType Type, string? Note);

/// <summary>
/// Everything the importer needs, parsed out of the export. Holds no database
/// state, so it can be built and inspected (and unit-tested) on its own.
/// </summary>
public sealed class ResourceGuruSource
{
    /// <summary>Below this many bookable weekdays, a derived pattern is not believed.</summary>
    private const int MinObservedWorkingDays = 2;

    public Dictionary<string, RgResource> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RgAvailability> Availability { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<RgProjectKey, RgProject> Projects { get; } = [];

    /// <summary>Booked hours per day, keyed by booking run then date.</summary>
    public Dictionary<RgBookingKey, SortedDictionary<DateOnly, decimal>> Bookings { get; } = [];

    /// <summary>Downtime hours per day, keyed by leave run then date.</summary>
    public Dictionary<RgDowntimeKey, SortedDictionary<DateOnly, decimal>> Downtime { get; } = [];

    /// <summary>Clients named in the export, including Resource Guru's placeholder.</summary>
    public HashSet<string> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Departments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> JobTitles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ActivityTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rows parsed per sheet, for the report.</summary>
    public Dictionary<RgSheet, int> RowCounts { get; } = [];

    /// <summary>Non-empty counts for source columns that are not imported.</summary>
    public List<ImportUnmappedFieldDto> UnmappedFields { get; } = [];

    /// <summary>Booking rows the export marked tentative or waiting (V004).</summary>
    public int UnconfirmedBookingRows { get; private set; }

    /// <summary>
    /// Parses every sheet the export contains. Bookings and downtime are folded
    /// into per-day totals as they stream, so a nine-month export never holds
    /// more than its grouped form in memory.
    /// </summary>
    public static ResourceGuruSource Read(ResourceGuruArchive archive, ImportIssueLog issues)
    {
        var src = new ResourceGuruSource();
        src.ReadResources(archive);
        src.ReadAvailability(archive, issues);
        src.ReadScheduledVsActuals(archive);
        src.ReadBookings(archive, issues);
        src.ReadDowntime(archive, issues);
        return src;
    }

    // ---- Resource Data -----------------------------------------------------

    private void ReadResources(ResourceGuruArchive archive)
    {
        var table = archive.Open(RgSheet.Resources);
        if (table is null) return;

        var count = 0;
        foreach (var row in table.Rows())
        {
            var email = row.Text("Email");
            var name = row.Text("Name");
            if (email is null || name is null) continue; // no identity, not importable
            count++;

            var resource = new RgResource
            {
                Name = name,
                Email = email,
                JobTitle = row.Text("Resource Field: Job Title"),
                JobRole = row.Text("Resource Field: Job Role"),
                Department = row.Text("Resource Field: Department"),
                Location = row.Text("Resource Field: Location"),
                Manager = row.Text("Resource Field: Manager"),
                Phone = row.Text("Phone"),
                Rate = row.Text("Rate") is null ? null : row.Number("Rate"),
                NpcObtainedOn = row.DayFirstDate("Resource Field: Security NPC (date obtained)"),
                PrimarySkills = row.CsvList("Resource Field: Primary Skills"),
                SecondarySkills = row.CsvList("Resource Field: Secondary Skills"),
                Clearances = row.CsvList("Resource Field: Security Clearances"),
                Certifications = row.CsvList("Resource Field: Staff Certifications"),
            };
            Resources[email] = resource;

            if (resource.Department is { } d) Departments.Add(d);
            if (resource.Location is { } l) Locations.Add(l);
            if (resource.JobTitle is { } j) JobTitles.Add(j);
            foreach (var s in resource.PrimarySkills) Skills.Add(s);
            foreach (var s in resource.SecondarySkills) Skills.Add(s);
        }
        RowCounts[RgSheet.Resources] = count;
    }

    // ---- Availability Data -------------------------------------------------

    /// <summary>
    /// Reduces the daily bookable-hours rows to one working pattern per person.
    ///
    /// Two things matter here. Resource Guru zeroes Available Hours on any day
    /// consumed by leave or a public holiday, so <b>only the non-zero rows
    /// describe the standard day</b> — averaging or taking the mode over all rows
    /// records anyone on extended leave as having no capacity. And of the non-zero
    /// rows the mode is used, not the mean or the maximum: a mean is dragged down
    /// by part weeks at the edges of the window, a maximum is pulled up by a single
    /// overtime day.
    /// </summary>
    private void ReadAvailability(ResourceGuruArchive archive, ImportIssueLog issues)
    {
        var table = archive.Open(RgSheet.Availability);
        if (table is null) return;

        var tally = new Dictionary<string, Dictionary<int, Dictionary<decimal, int>>>(
            StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var row in table.Rows())
        {
            var email = row.Text("Email");
            var date = row.IsoDate("Date");
            if (email is null || date is null) continue;
            count++;

            var hours = Math.Round(row.Number("Available Hours"), 2);
            var byDay = tally.TryGetValue(email, out var d) ? d : tally[email] = [];
            var day = (int)date.Value.DayOfWeek;
            var byHours = byDay.TryGetValue(day, out var h) ? h : byDay[day] = [];
            byHours[hours] = byHours.GetValueOrDefault(hours) + 1;
        }
        RowCounts[RgSheet.Availability] = count;

        foreach (var (email, byDay) in tally)
        {
            var profile = new RgAvailability();
            foreach (var (day, byHours) in byDay)
                profile.HoursByDay[day] = StandardHours(byHours) ?? 0m;

            // Fewer than two weekdays ever bookable means the window saw this
            // person working almost never, not that they work almost never.
            if (profile.WorkingDaysPerWeek < MinObservedWorkingDays)
            {
                var dayHours = byDay.Values
                    .Select(StandardHours).OfType<decimal>()
                    .DefaultIfEmpty(RgAvailabilityDefaults.DayHours)
                    .Max();
                profile = RgAvailability.StandardWeek(dayHours);
                issues.Add("availability.notObserved",
                    "People the export never saw working a normal week — long-term leave zeroes "
                    + "their bookable hours — were given a standard Monday-to-Friday week rather "
                    + "than no capacity at all. Check their availability after importing.", email);
            }

            Availability[email] = profile;
        }
    }

    /// <summary>
    /// A weekday's standard length: the most common non-zero observation, ties
    /// breaking to the longer day so that someone who moved to a longer week
    /// mid-export is not recorded as still on the old one. Null when the weekday
    /// was never bookable.
    /// </summary>
    private static decimal? StandardHours(Dictionary<decimal, int> byHours) =>
        byHours.Where(x => x.Key > 0)
            .OrderByDescending(x => x.Value).ThenByDescending(x => x.Key)
            .Select(x => (decimal?)x.Key)
            .FirstOrDefault();

    // ---- Scheduled Vs Actuals ---------------------------------------------

    /// <summary>
    /// Catalogues projects — including any with no bookings in the window — and
    /// widens their date range. The actual-hours columns are all zero in the
    /// export (Resource Guru derives actuals from timesheets, which SRA-RMS does
    /// not model), so only the identity and date columns are used.
    /// </summary>
    private void ReadScheduledVsActuals(ResourceGuruArchive archive)
    {
        var table = archive.Open(RgSheet.ScheduledVsActuals);
        if (table is null) return;

        var count = 0;
        var actualHourRows = 0;
        foreach (var row in table.Rows())
        {
            count++;
            var key = KeyFrom(row.Text("Client"), row.Text("Project"), row.Text("Project Code"));
            if (key is null) continue;

            var project = ProjectFor(key);
            if (row.IsoDate("Date") is { } date) project.Observe(date);
            AddActivityType(project, row);
            if (row.Number("Total Actual Hours") != 0) actualHourRows++;
        }
        RowCounts[RgSheet.ScheduledVsActuals] = count;

        UnmappedFields.Add(new ImportUnmappedFieldDto("scheduledVsActuals", "Total Actual Hours",
            "SRA-RMS models scheduled allocations, not timesheet actuals.", actualHourRows));
    }

    // ---- Bookings Data ----------------------------------------------------

    private void ReadBookings(ResourceGuruArchive archive, ImportIssueLog issues)
    {
        var table = archive.Open(RgSheet.Bookings);
        if (table is null) return;

        var count = 0;
        var nonConfirmed = 0;
        var ratedRows = 0;
        foreach (var row in table.Rows())
        {
            count++;
            var email = row.Text("Email");
            var date = row.IsoDate("Date");
            var key = KeyFrom(row.Text("Client"), row.Text("Project"), row.Text("Project Code"));
            if (email is null || date is null || key is null)
            {
                issues.Add("booking.incomplete",
                    "Booking rows with no person, date or project were skipped.",
                    email ?? "(no email)");
                continue;
            }

            var billable = !string.Equals(row["Billable"].Trim(), "non-billable",
                StringComparison.OrdinalIgnoreCase);

            var project = ProjectFor(key);
            project.Observe(date.Value);
            project.HasBookings = true;
            project.AnyBillable |= billable;
            AddActivityType(project, row);

            var status = ImportMaps.ToBookingStatus(row.Text("Booking Status"));
            if (status != BookingStatus.Confirmed) nonConfirmed++;
            if (row.Text("Rate") is not null || row.Text("Billable Rate Total") is not null) ratedRows++;

            var bookingKey = new RgBookingKey(
                email, key, row.Text("Details"), row.Text("Booker"), billable, status);
            var days = Bookings.TryGetValue(bookingKey, out var d) ? d : Bookings[bookingKey] = [];
            // Two rows for the same person, project and day (a split booking) are
            // the same day's work, so their hours add.
            days[date.Value] = days.GetValueOrDefault(date.Value) + row.Number("Hours");
        }
        RowCounts[RgSheet.Bookings] = count;
        UnconfirmedBookingRows = nonConfirmed;

        UnmappedFields.Add(new ImportUnmappedFieldDto("bookings", "Rate, Billable Rate Total",
            "Imported where present; this export carries no rates.", ratedRows));
    }

    // ---- Downtime Data ----------------------------------------------------

    private void ReadDowntime(ResourceGuruArchive archive, ImportIssueLog issues)
    {
        var table = archive.Open(RgSheet.Downtime);
        if (table is null) return;

        var count = 0;
        foreach (var row in table.Rows())
        {
            count++;
            var email = row.Text("Email");
            var date = row.IsoDate("Date");
            if (email is null || date is null)
            {
                issues.Add("downtime.incomplete",
                    "Downtime rows with no person or date were skipped.", email ?? "(no email)");
                continue;
            }

            var key = new RgDowntimeKey(email, ImportMaps.ToTimeOffType(row.Text("Type")), row.Text("Details"));
            var days = Downtime.TryGetValue(key, out var d) ? d : Downtime[key] = [];
            days[date.Value] = Math.Max(days.GetValueOrDefault(date.Value), row.Number("Hours"));
        }
        RowCounts[RgSheet.Downtime] = count;
    }

    // ---- helpers ----------------------------------------------------------

    private RgProject ProjectFor(RgProjectKey key)
    {
        Clients.Add(key.Client);
        return Projects.TryGetValue(key, out var p) ? p : Projects[key] = new RgProject { Key = key };
    }

    private void AddActivityType(RgProject project, CsvRow row)
    {
        var value = row.Text("Activity Type");
        if (value is null || value.Equals("No activity type assigned", StringComparison.OrdinalIgnoreCase))
            return;
        project.ActivityTypes.Add(value);
        ActivityTypes.Add(value);
    }

    /// <summary>
    /// Builds a project key, normalising Resource Guru's placeholder text. Both
    /// "No client assigned" and "No project assigned" are kept as names so the
    /// bookings behind them still import; the code, however, is treated as
    /// absent, because "No project assigned" is not a project code.
    /// </summary>
    private static RgProjectKey? KeyFrom(string? client, string? project, string? code)
    {
        if (project is null) return null;
        if (string.Equals(code, "No project assigned", StringComparison.OrdinalIgnoreCase)) code = null;
        return new RgProjectKey(client ?? ImportMaps.UnassignedClient, project, code ?? "");
    }
}
