using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Data;

namespace SraRms.Api.Services.Import;

/// <summary>
/// Loads a Resource Guru report export into SRA-RMS (FR-IMP-1..6).
///
/// The whole run happens inside one transaction, which is rolled back when
/// <c>dryRun</c> is set. A preview therefore exercises every foreign key, unique
/// index and check constraint the real import would, and reports exactly the
/// same counts — the SPA previews and then commits the same file.
///
/// Records that already exist are matched on their natural key (client name,
/// project code, person email, and an allocation's project/person/window) and
/// left untouched, so a re-run tops up rather than duplicating.
/// </summary>
public sealed class ResourceGuruImporter(AppDbContext db, BusinessClock clock)
{
    /// <summary>A day gap wider than this never joins two bookings, whatever the calendar says.</summary>
    private const int MaxBridgedGapDays = 31;

    /// <summary>
    /// Slack allowed when deciding whether leave took a whole day. Resource Guru
    /// writes both 8 and 8.0166 hours for the same nominal eight-hour day, and
    /// a 36-second shortfall is not a part day.
    /// </summary>
    private const decimal WholeDayToleranceHours = 0.05m;

    private sealed class Counter
    {
        public int Created;
        public int Updated;
        public int Skipped;
    }

    private readonly ImportIssueLog _issues = new();
    private readonly Dictionary<string, Counter> _counts = [];

    private Counter Count(string entity) =>
        _counts.TryGetValue(entity, out var c) ? c : _counts[entity] = new Counter();

    public async Task<ImportReportDto> RunAsync(ResourceGuruArchive archive, bool dryRun, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var source = ResourceGuruSource.Read(archive, _issues);

        if (source.Resources.Count == 0 && source.Projects.Count == 0)
            throw new InvalidDataException(
                "The export contained no people and no projects. Check that the .zip is the "
                + "Resource Guru report download and includes the Resource Data sheet.");

        if (source.UnconfirmedBookingRows > 0)
            _issues.Add("booking.unconfirmed",
                $"{source.UnconfirmedBookingRows} booking row(s) were tentative or waiting rather "
                + "than confirmed. They import with that status (V004) and still count toward "
                + "capacity and over-allocation warnings, so the Schedule shows them as provisional "
                + "rather than hiding them.");

        if (source.Availability.Count == 0)
            _issues.Add("availability.missing",
                $"No Availability sheet, so everyone imports on a standard "
                + $"{RgAvailabilityDefaults.WeeklyHours:0.##}-hour Monday-to-Friday week.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await ImportReferenceDataAsync(source, ct);
            var clients = await ImportClientsAsync(source, ct);
            var resources = await ImportResourcesAsync(source, ct);
            var projects = await ImportProjectsAsync(source, clients, ct);
            await ImportAllocationsAsync(source, resources, projects, ct);
            await ImportTimeOffAsync(source, resources, ct);

            if (dryRun) await tx.RollbackAsync(ct);
            else await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return new ImportReportDto
        {
            DryRun = dryRun,
            SourceFiles = archive.FileNames,
            SheetsRead = archive.Sheets.Select(s => Camel(s.ToString())).Order(StringComparer.Ordinal).ToList(),
            SourceRows = source.RowCounts
                .OrderBy(r => r.Key.ToString(), StringComparer.Ordinal)
                .Select(r => new ImportSourceCountDto(Camel(r.Key.ToString()), r.Value)).ToList(),
            Entities = _counts
                .OrderBy(c => Array.IndexOf(EntityOrder, c.Key))
                .Select(c => new ImportEntityCountDto(c.Key, c.Value.Created, c.Value.Updated, c.Value.Skipped))
                .ToList(),
            Warnings = _issues.ToList(),
            UnmappedFields = source.UnmappedFields.Where(f => f.NonEmptyRows > 0).ToList(),
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
        };
    }

    /// <summary>Report order: the dependency order the import itself runs in.</summary>
    private static readonly string[] EntityOrder =
    [
        "clients", "projects", "resources", "allocations", "timeOff",
        "departments", "locations", "jobTitles", "skills", "activityTypes",
    ];

    private static string Camel(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    // ------------------------------------------------------------------ reference data

    private async Task ImportReferenceDataAsync(ResourceGuruSource source, CancellationToken ct)
    {
        await AddMissingAsync(db.Departments, "departments", source.Departments, ct);
        await AddMissingAsync(db.Locations, "locations", source.Locations, ct);
        await AddMissingAsync(db.JobTitles, "jobTitles", source.JobTitles, ct);
        await AddMissingAsync(db.Skills, "skills", source.Skills, ct);
        await AddMissingAsync(db.ActivityTypes, "activityTypes", source.ActivityTypes, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task AddMissingAsync(
        DbSet<ReferenceItem> set, string entity, IEnumerable<string> values, CancellationToken ct)
    {
        var counter = Count(entity);
        var existing = (await set.AsNoTracking().Select(i => i.Value).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.Add(value)) { counter.Skipped++; continue; }
            set.Add(new ReferenceItem { Value = value, Active = true });
            counter.Created++;
        }
    }

    // ------------------------------------------------------------------ clients

    private async Task<Dictionary<string, Guid>> ImportClientsAsync(
        ResourceGuruSource source, CancellationToken ct)
    {
        var counter = Count("clients");
        var byName = (await db.Clients.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var added = new List<Client>();
        foreach (var name in source.Clients.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (byName.ContainsKey(name)) { counter.Skipped++; continue; }
            var client = new Client { Name = name };
            db.Clients.Add(client);
            added.Add(client);
            counter.Created++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var client in added) byName[client.Name] = client.Id;

        if (source.Clients.Contains(ImportMaps.UnassignedClient))
            _issues.Add("client.placeholder",
                $"Resource Guru's \"{ImportMaps.UnassignedClient}\" placeholder was imported as a client so "
                + "that bookings behind it are not lost. Reassign those projects and delete it afterwards.");

        return byName;
    }

    // ------------------------------------------------------------------ resources

    private async Task<Dictionary<string, Guid>> ImportResourcesAsync(
        ResourceGuruSource source, CancellationToken ct)
    {
        var counter = Count("resources");
        var existing = await db.Resources.AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.Email }).ToListAsync(ct);

        var byEmail = existing.GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Name -> ids, because manager and booker are recorded as names only. A
        // name shared by two people cannot be resolved and is reported instead.
        var byName = existing.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        var added = new List<(Resource Entity, RgResource Source)>();
        foreach (var rg in source.Resources.Values.OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase))
        {
            if (byEmail.ContainsKey(rg.Email)) { counter.Skipped++; continue; }

            var availability = source.Availability.GetValueOrDefault(rg.Email);
            var resource = new Resource
            {
                Name = rg.Name,
                Email = rg.Email,
                // primary_job_title is NOT NULL; the export leaves it blank for a
                // few people, whose job role is the closest thing on the row.
                PrimaryJobTitle = rg.JobTitle ?? rg.JobRole ?? ImportMaps.UnspecifiedJobTitle,
                JobRole = rg.JobRole,
                Department = rg.Department,
                Location = rg.Location,
                Phone = rg.Phone,
                Skills = rg.PrimarySkills,
                SecondarySkills = rg.SecondarySkills,
                SecurityClearances = rg.Clearances,
                SecurityNpcObtainedOn = rg.NpcObtainedOn,
                Certifications = rg.Certifications,
                DefaultRateHourly = rg.Rate is > 0 ? rg.Rate : null,
                Status = ResourceStatus.Active,
                BookableStatus = BookableStatus.Bookable,
                AvailabilityHoursPerWeek = Math.Clamp(
                    availability?.WeeklyHours ?? RgAvailabilityDefaults.WeeklyHours, 0m, 168m),
                WorkingDays = availability?.WorkingDays() ?? RgAvailabilityDefaults.WorkingDays,
            };
            if (rg.JobTitle is null)
                _issues.Add("resource.noJobTitle",
                    $"People with no Job Title imported with \"{resource.PrimaryJobTitle}\" as their "
                    + "primary job title.", rg.Email);

            db.Resources.Add(resource);
            added.Add((resource, rg));
            counter.Created++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var (entity, _) in added)
        {
            byEmail[entity.Email] = entity.Id;
            if (byName.TryGetValue(entity.Name, out var ids)) ids.Add(entity.Id);
            else byName[entity.Name] = [entity.Id];
        }

        // Managers resolve by name, so they need every person to exist first.
        foreach (var (entity, rg) in added)
        {
            if (rg.Manager is null) continue;
            var managerId = ResolveByName(rg.Manager, byName, "resource.managerUnresolved",
                "Managers named in the export who are not themselves people in it were left unset.");
            if (managerId is null) continue;
            if (managerId == entity.Id)
            {
                _issues.Add("resource.selfManager",
                    "People recorded as their own manager were left with no manager.", rg.Email);
                continue;
            }
            entity.ManagerId = managerId;
        }
        await db.SaveChangesAsync(ct);

        _peopleByName = byName;
        return byEmail;
    }

    /// <summary>Person name to ids, retained so bookers can be resolved during the allocation pass.</summary>
    private Dictionary<string, List<Guid>> _peopleByName = [];

    private Guid? ResolveByName(
        string name, Dictionary<string, List<Guid>> byName, string code, string message)
    {
        if (!byName.TryGetValue(name, out var ids) || ids.Count == 0)
        {
            _issues.Add(code, message, name);
            return null;
        }
        if (ids.Count > 1)
        {
            _issues.Add($"{code}.ambiguous",
                "Names shared by more than one person could not be resolved and were left unset.", name);
            return null;
        }
        return ids[0];
    }

    // ------------------------------------------------------------------ projects

    private async Task<Dictionary<RgProjectKey, Guid>> ImportProjectsAsync(
        ResourceGuruSource source, Dictionary<string, Guid> clients, CancellationToken ct)
    {
        var counter = Count("projects");
        var today = clock.Today;

        var existingProjects = await db.Projects.ToListAsync(ct);
        var byCode = existingProjects
            .GroupBy(p => p.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        var result = new Dictionary<RgProjectKey, Guid>();
        var pending = new List<(RgProjectKey Key, Project Project)>();
        var synthesised = 0;

        // Deterministic order so a re-run of the same export invents the same codes.
        var ordered = source.Projects.Values
            .OrderBy(p => p.Key.Client, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Code, StringComparer.Ordinal);

        foreach (var rg in ordered)
        {
            if (!clients.TryGetValue(rg.Key.Client, out var clientId))
            {
                _issues.Add("project.unknownClient",
                    "Projects whose client could not be created were skipped.", rg.Key.Name);
                continue;
            }

            var start = rg.First ?? today;
            var end = rg.Last ?? start;

            // Settle the code before deciding whether this is a new project.
            // The suffix ladder is walked the same way on every run, so a project
            // that was given "APL-1-2" or a generated code last time lands back on
            // it and is adopted rather than duplicated.
            var preferred = rg.Key.Code.Length > 0 ? rg.Key.Code : SynthesiseCode(rg.Key);
            if (rg.Key.Code.Length == 0) synthesised++;

            var code = preferred;
            for (var n = 2; claimed.Contains(code); n++) code = $"{preferred}-{n}";
            claimed.Add(code);

            if (code != preferred)
                _issues.Add("project.duplicateCode",
                    "Project codes used by more than one Resource Guru project were suffixed to keep "
                    + "them unique (project.code is unique in SRA-RMS).",
                    $"{preferred} -> {code}");

            // A project already carrying this code is this project: code is the
            // unique business key (FR-PRJ-1).
            if (byCode.TryGetValue(code, out var match))
            {
                result[rg.Key] = match.Id;
                // Widen the window so imported bookings stay inside it (FR-ALL-5).
                var widened = false;
                if (start < match.StartDate) { match.StartDate = start; widened = true; }
                if (end > match.EndDate) { match.EndDate = end; widened = true; }
                if (widened)
                {
                    counter.Updated++;
                    _issues.Add("project.windowWidened",
                        "Existing projects had their start or end date extended to cover imported "
                        + "bookings.", match.Code);
                }
                else
                {
                    counter.Skipped++;
                }
                continue;
            }

            var project = new Project
            {
                ClientId = clientId,
                Name = rg.Key.Name,
                Code = code,
                StartDate = start,
                EndDate = end,
                Billable = rg.AnyBillable,
                Status = ImportMaps.StatusFor(start, end, today),
                BudgetType = ProjectBudgetType.None,
                ActivityTypes = rg.ActivityTypes.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            };
            db.Projects.Add(project);
            counter.Created++;
            pending.Add((rg.Key, project));
        }

        await db.SaveChangesAsync(ct);
        foreach (var (key, project) in pending) result[key] = project.Id;

        if (synthesised > 0)
            _issues.Add("project.codeSynthesised",
                $"{synthesised} project(s) had no code in the export and were matched on a generated "
                + $"\"{ImportMaps.SyntheticCodePrefix}...\" code, because project.code is required "
                + "and unique. The generated code is derived from the project and client names, so a "
                + "re-import lands on the same project.");

        return result;
    }

    private static string SynthesiseCode(RgProjectKey key)
    {
        var slug = ImportMaps.Slug(key.Name, 28);
        if (slug.Length == 0) slug = "PROJECT";
        // Placeholder names repeat across clients, so the client narrows them.
        var client = ImportMaps.Slug(key.Client, 14);
        return client.Length == 0
            ? ImportMaps.SyntheticCodePrefix + slug
            : $"{ImportMaps.SyntheticCodePrefix}{slug}-{client}";
    }

    // ------------------------------------------------------------------ allocations

    private async Task ImportAllocationsAsync(
        ResourceGuruSource source,
        Dictionary<string, Guid> resources,
        Dictionary<RgProjectKey, Guid> projects,
        CancellationToken ct)
    {
        var counter = Count("allocations");

        var resourceIds = resources.Values.ToHashSet();
        var projectIds = projects.Values.ToHashSet();
        // The natural key of an imported booking is everything the booking dialog
        // holds except its effort — not just the window. Two Resource Guru
        // bookings can cover the same person, project and dates and still be
        // different bookings, arranged by different bookers or against different
        // details, whose hours add; keying on the window alone would drop one of
        // them and lose real scheduled effort.
        var seen = (await db.Allocations.AsNoTracking()
                .Where(a => resourceIds.Contains(a.ResourceId) && projectIds.Contains(a.ProjectId))
                .Select(a => new
                {
                    a.ProjectId, a.ResourceId, a.StartDate, a.EndDate, a.Details, a.Billable, a.BookerId,
                    a.BookingStatus,
                })
                .ToListAsync(ct))
            .Select(a => (a.ProjectId, a.ResourceId, a.StartDate, a.EndDate, a.Details, a.Billable,
                a.BookerId, a.BookingStatus))
            .ToHashSet();

        // Deterministic order, so a warning's examples are stable between runs.
        var ordered = source.Bookings
            .OrderBy(b => b.Key.Email, StringComparer.Ordinal)
            .ThenBy(b => b.Key.Project.Name, StringComparer.Ordinal)
            .ThenBy(b => b.Key.Project.Code, StringComparer.Ordinal)
            .ThenBy(b => b.Key.Details, StringComparer.Ordinal)
            .ThenBy(b => b.Key.Status);

        foreach (var (key, days) in ordered)
        {
            if (!resources.TryGetValue(key.Email, out var resourceId))
            {
                _issues.Add("booking.unknownResource",
                    "Bookings for people who are not in the Resource Data sheet were skipped.", key.Email);
                continue;
            }
            if (!projects.TryGetValue(key.Project, out var projectId))
            {
                _issues.Add("booking.unknownProject",
                    "Bookings whose project could not be created were skipped.", key.Project.Name);
                continue;
            }

            var availability = source.Availability.GetValueOrDefault(key.Email);
            var bookerId = key.Booker is null
                ? null
                : ResolveByName(key.Booker, _peopleByName, "booking.unknownBooker",
                    "Bookers named in the export who are not people in it were left unset "
                    + "(the import is still attributed to you through created_by).");

            foreach (var (start, end, dailyHours) in Collapse(days, availability))
            {
                if (!seen.Add((projectId, resourceId, start, end, key.Details, key.Billable, bookerId,
                        key.Status)))
                {
                    counter.Skipped++;
                    _issues.Add("booking.duplicate",
                        "Bookings identical to one already recorded — same person, project, window, "
                        + "details, billability, booker and status — were skipped. On a re-import "
                        + "this is every booking that came across the first time.",
                        $"{key.Email} / {key.Project.Name} / {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
                    continue;
                }

                db.Allocations.Add(new Allocation
                {
                    ProjectId = projectId,
                    ResourceId = resourceId,
                    StartDate = start,
                    EndDate = end,
                    // Resource Guru books hours per day; SRA-RMS effort is per week.
                    Effort = Math.Round(dailyHours * WorkingDaysPerWeek(availability), 2),
                    EffortUnit = EffortUnit.HoursPerWeek,
                    Billable = key.Billable,
                    Details = key.Details,
                    BookerId = bookerId,
                    BookingStatus = key.Status,
                });
                counter.Created++;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ time off

    private async Task ImportTimeOffAsync(
        ResourceGuruSource source, Dictionary<string, Guid> resources, CancellationToken ct)
    {
        var counter = Count("timeOff");

        var resourceIds = resources.Values.ToHashSet();
        var booked = (await db.TimeOff.AsNoTracking()
                .Where(t => resourceIds.Contains(t.ResourceId))
                .Select(t => new { t.ResourceId, t.StartDate, t.EndDate })
                .ToListAsync(ct))
            .GroupBy(t => t.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(t => (t.StartDate, t.EndDate)).ToList());

        var ordered = source.Downtime
            .OrderBy(d => d.Key.Email, StringComparer.Ordinal)
            .ThenBy(d => d.Value.Keys.First())
            .ThenBy(d => d.Key.Type)
            .ThenBy(d => d.Key.Note, StringComparer.Ordinal);

        foreach (var (key, days) in ordered)
        {
            if (!resources.TryGetValue(key.Email, out var resourceId))
            {
                _issues.Add("downtime.unknownResource",
                    "Downtime for people who are not in the Resource Data sheet was skipped.", key.Email);
                continue;
            }

            var availability = source.Availability.GetValueOrDefault(key.Email);
            var taken = booked.TryGetValue(resourceId, out var list) ? list : booked[resourceId] = [];

            foreach (var (start, end, dailyHours) in Collapse(days, availability))
            {
                // Overlapping leave for one person is rejected by the time-off
                // endpoint (FR-TIMEOFF-4) as a data error rather than a warning
                // state, so the importer must not write it either.
                if (taken.Any(t => start <= t.EndDate && t.StartDate <= end))
                {
                    counter.Skipped++;
                    _issues.Add("timeOff.overlap",
                        "Leave that overlapped leave already recorded for the same person was skipped; "
                        + "SRA-RMS does not allow a person two overlapping time-off records.",
                        $"{key.Email} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
                    continue;
                }
                taken.Add((start, end));

                var wholeDay = WholeDayHours(start, end, availability);
                db.TimeOff.Add(new TimeOff
                {
                    ResourceId = resourceId,
                    StartDate = start,
                    EndDate = end,
                    Type = key.Type,
                    // Null means the whole working day is gone. A part-day is only
                    // recorded when the downtime is genuinely shorter than the day.
                    HoursPerDay = dailyHours > 0 && dailyHours < wholeDay - WholeDayToleranceHours
                        ? Math.Min(dailyHours, 24m)
                        : null,
                    Note = key.Note,
                });
                counter.Created++;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ collapsing

    /// <summary>
    /// The shortest working day the range covers — the length a whole day of
    /// leave has to match. The shortest rather than the average, because a
    /// person's days differ in length and Resource Guru records a whole day off
    /// as exactly that day's bookable hours; against an average, a normal
    /// eight-hour day off an 8.01-hour average reads as a part day.
    /// </summary>
    private static decimal WholeDayHours(DateOnly start, DateOnly end, RgAvailability? availability)
    {
        if (availability is null) return RgAvailabilityDefaults.DayHours;
        var shortest = decimal.MaxValue;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var hours = availability.HoursOn(d);
            if (hours > 0 && hours < shortest) shortest = hours;
        }
        return shortest == decimal.MaxValue ? RgAvailabilityDefaults.DayHours : shortest;
    }

    private static decimal WorkingDaysPerWeek(RgAvailability? availability) =>
        availability is { WorkingDaysPerWeek: > 0 } a ? a.WorkingDaysPerWeek : RgAvailabilityDefaults.WorkingDayCount;

    /// <summary>
    /// Folds dated daily values into contiguous ranges. Two dates join when they
    /// carry the same hours and every date between them is a non-working day for
    /// that person — so a Monday-to-Friday booking repeated over four weeks
    /// becomes one allocation, while a real mid-week break still splits it.
    /// </summary>
    public static List<(DateOnly Start, DateOnly End, decimal Hours)> Collapse(
        SortedDictionary<DateOnly, decimal> days, RgAvailability? availability)
    {
        var runs = new List<(DateOnly, DateOnly, decimal)>();
        DateOnly start = default, end = default;
        var hours = 0m;
        var open = false;

        foreach (var (date, value) in days)
        {
            var rounded = Math.Round(value, 2);
            if (open && rounded == hours && JoinsOn(end, date, availability))
            {
                end = date;
                continue;
            }
            if (open) runs.Add((start, end, hours));
            start = end = date;
            hours = rounded;
            open = true;
        }
        if (open) runs.Add((start, end, hours));
        return runs;
    }

    /// <summary>True when nothing but non-working days separates <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static bool JoinsOn(DateOnly a, DateOnly b, RgAvailability? availability)
    {
        if (b.DayNumber - a.DayNumber > MaxBridgedGapDays) return false;
        for (var d = a.AddDays(1); d < b; d = d.AddDays(1))
            if (availability?.IsWorkingDay(d) ?? RgAvailabilityDefaults.IsWorkingDay(d))
                return false;
        return true;
    }
}

/// <summary>
/// The working pattern assumed for a person the Availability sheet does not
/// cover: a standard Monday-to-Friday, eight-hour-a-day week.
/// </summary>
public static class RgAvailabilityDefaults
{
    public const decimal DayHours = 8m;
    public const int WorkingDayCount = 5;
    public const decimal WeeklyHours = DayHours * WorkingDayCount;

    public static List<Weekday> WorkingDays =>
        [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];

    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
