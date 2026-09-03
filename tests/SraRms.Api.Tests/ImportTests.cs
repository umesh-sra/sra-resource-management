using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services.Import;
using static SraRms.Api.Tests.ImportFixtureBuilder;

namespace SraRms.Api.Tests;

/// <summary>
/// Covers the Resource Guru migration endpoint (FR-IMP-1..7): sheet discovery,
/// the day-row to allocation fold, derived values, code collisions, dry runs and
/// re-run behaviour.
/// </summary>
public class ImportTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    // A two-week window whose weekends the fold has to bridge.
    private static readonly DateOnly Mon1 = new(2026, 1, 5);
    private static readonly DateOnly Tue2 = new(2026, 1, 13);
    private static readonly DateOnly Wed2 = new(2026, 1, 14);

    private const string Ada = "ada@example.com";
    private const string Grace = "grace@example.com";
    private const string Linus = "linus@example.com";
    private const string Edsger = "edsger@example.com";

    // ---- the sample export -------------------------------------------------

    private static string ResourceSheet() => Sheet(ResourceHeader,
    [
        Row(("Id", "1"), ("Name", "Ada Lovelace"), ("Email", Ada), ("Type", "Person"),
            ("Resource Field: Department", "ENG"), ("Resource Field: Job Role", "Tech Lead"),
            ("Resource Field: Job Title", "Principal Consultant"), ("Resource Field: Location", "SA"),
            ("Resource Field: Manager", "Grace Hopper"),
            ("Resource Field: Primary Skills", ".NET, Postgres"),
            ("Resource Field: Secondary Skills", "Python"),
            ("Resource Field: Security Clearances", "NPC, WWCC"),
            ("Resource Field: Security NPC (date obtained)", "28/09/2023"),
            ("Resource Field: Staff Certifications", "PRINCE2 Foundation"),
            ("Phone", "0400 000 001")),
        Row(("Id", "2"), ("Name", "Grace Hopper"), ("Email", Grace), ("Type", "Person"),
            ("Rate", "210.50"), ("Resource Field: Department", "ENG"),
            ("Resource Field: Job Title", "Service Delivery Manager"),
            ("Resource Field: Location", "SA"),
            ("Resource Field: Primary Skills", "Project Manager")),
        // No job title, and a manager who is not in the export.
        Row(("Id", "3"), ("Name", "Linus Torvalds"), ("Email", Linus), ("Type", "Person"),
            ("Resource Field: Job Role", "Developer"), ("Resource Field: Location", "NT"),
            ("Resource Field: Manager", "Nobody Here")),
        Row(("Id", "4"), ("Name", "Edsger Dijkstra"), ("Email", Edsger), ("Type", "Person"),
            ("Resource Field: Job Title", "Consultant"), ("Resource Field: Location", "NT")),
    ]);

    /// <summary>
    /// Ada on a standard 8-hour week, Grace on 4-hour days, Edsger on long-term
    /// leave. Linus is absent from the sheet entirely.
    ///
    /// Resource Guru zeroes Available Hours on any day taken by leave or a public
    /// holiday, which the zeroed rows here reproduce: Ada's Australia Day and
    /// leave week, and all but one of Edsger's days.
    /// </summary>
    private static string AvailabilitySheet()
    {
        var adaOff = new HashSet<DateOnly>
        {
            new(2026, 1, 26), // Australia Day
            new(2026, 2, 2), new(2026, 2, 3), new(2026, 2, 4), new(2026, 2, 5), new(2026, 2, 6),
        };
        var edsgerWorked = new DateOnly(2026, 1, 8); // a Thursday

        return Sheet(AvailabilityHeader,
            Weekdays(Mon1, new DateOnly(2026, 2, 27)).SelectMany(d => new[]
            {
                // Ada's Mondays are minutely longer than her other days — Resource
                // Guru really does export 8 and 8.02 for the same nominal day.
                Row(("Date", d.ToString("yyyy-MM-dd")),
                    ("Available Hours", adaOff.Contains(d) ? "0"
                        : d.DayOfWeek == DayOfWeek.Monday ? "8.02" : "8"),
                    ("Resource", "Ada Lovelace"), ("Email", Ada)),
                Row(("Date", d.ToString("yyyy-MM-dd")), ("Available Hours", "4"),
                    ("Resource", "Grace Hopper"), ("Email", Grace)),
                Row(("Date", d.ToString("yyyy-MM-dd")),
                    ("Available Hours", d == edsgerWorked ? "8" : "0"),
                    ("Resource", "Edsger Dijkstra"), ("Email", Edsger)),
            }));
    }

    private static string BookingSheet()
    {
        var rows = new List<Dictionary<string, string>>();

        // Ada: 4h/day on Apollo across two working weeks -> one allocation.
        foreach (var d in Weekdays(Mon1, Tue2))
            rows.Add(Booking(d, "4", Ada, "Ada Lovelace", "Apollo", "APL-1", "NASA",
                booker: "Grace Hopper"));

        // Same project, different hours the next day -> a second allocation.
        rows.Add(Booking(Wed2, "8", Ada, "Ada Lovelace", "Apollo", "APL-1", "NASA",
            booker: "Grace Hopper"));

        // Grace: non-billable bench time on a project with no code.
        foreach (var d in Weekdays(Mon1, new DateOnly(2026, 1, 6)))
            rows.Add(Booking(d, "2", Grace, "Grace Hopper", "Bench Tasks", "", "SRA",
                billable: "non-billable"));

        // Linus: a different project sharing Apollo's code, booked tentatively.
        rows.Add(Booking(Mon1, "8", Linus, "Linus Torvalds", "Gemini", "APL-1", "NASA",
            status: "tentative"));

        // Grace, same project and day as her confirmed bench time but waiting
        // approval: status is part of a booking's identity, so this must import
        // as a second allocation rather than being folded into the first.
        rows.Add(Booking(Mon1, "2", Grace, "Grace Hopper", "Bench Tasks", "", "SRA",
            billable: "non-billable", status: "waiting"));

        return Sheet(BookingHeader, rows);
    }

    private static Dictionary<string, string> Booking(
        DateOnly date, string hours, string email, string name, string project, string code,
        string client, string billable = "billable", string booker = "Admin Admin",
        string details = "", string status = "confirmed") =>
        Row(("Date", date.ToString("yyyy-MM-dd")), ("Hours", hours), ("Booker", booker),
            ("Resource", name), ("Email", email), ("Resource Type", "Person"),
            ("Billable", billable), ("Approval Status", "approved"), ("Booking Status", status),
            ("Project", project), ("Project Code", code),
            ("Activity Type", "No activity type assigned"), ("Client", client), ("Details", details));

    private static string DowntimeSheet()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Downtime(new DateOnly(2026, 1, 26), "Public holiday", "8", Ada, "Ada Lovelace",
                "Australia Day"),
            // Two hours off a four-hour day: a genuine part day.
            Downtime(new DateOnly(2026, 1, 7), "Sick leave", "2", Grace, "Grace Hopper", ""),
        };
        // A full working week of annual leave -> one record.
        foreach (var d in Weekdays(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6)))
            rows.Add(Downtime(d, "Holiday (personal)", "8", Ada, "Ada Lovelace", "Overseas"));
        return Sheet(DowntimeHeader, rows);
    }

    private static Dictionary<string, string> Downtime(
        DateOnly date, string type, string hours, string email, string name, string details) =>
        Row(("Date", date.ToString("yyyy-MM-dd")), ("Type", type), ("Hours", hours),
            ("Resource", name), ("Email", email), ("Resource Type", "Person"), ("Details", details));

    /// <summary>Carries a project nobody is booked on, which must still be created.</summary>
    private static string ScheduledVsActualsSheet() => Sheet(ScheduledVsActualsHeader,
    [
        Row(("Date", "2026-03-02"), ("Project ID", "900"), ("Project", "Voyager"),
            ("Project Code", "VYG-9"), ("Client ID", "77"), ("Client", "ESA"),
            ("Activity Type", "Design"), ("Total Scheduled Hours", "8"),
            ("Total Actual Hours", "0")),
        Row(("Date", "2026-03-06"), ("Project ID", "900"), ("Project", "Voyager"),
            ("Project Code", "VYG-9"), ("Client ID", "77"), ("Client", "ESA"),
            ("Activity Type", "Design"), ("Total Scheduled Hours", "8"),
            ("Total Actual Hours", "0")),
    ]);

    private static byte[] SampleExport() => Zip(
    [
        ("Resource Guru Resource Data 1 Jan - 31 Mar 2026.csv", ResourceSheet()),
        ("Resource Guru Availability Data 1 Jan - 31 Mar 2026.csv", AvailabilitySheet()),
        ("Resource Guru Bookings Data 1 Jan - 31 Mar 2026.csv", BookingSheet()),
        ("Resource Guru Downtime Data 1 Jan - 31 Mar 2026.csv", DowntimeSheet()),
        ("Resource Guru Scheduled Vs Actuals Data 1 Jan - 31 Mar 2026.csv", ScheduledVsActualsSheet()),
    ]);

    private async Task<ImportReportDto> Import(bool dryRun, byte[]? export = null, string name = "export.zip")
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(export ?? SampleExport());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(content, "file", name);

        var res = await Client.PostAsync($"/v1/import/resource-guru?dryRun={dryRun}", form);
        res.EnsureSuccessStatusCode();
        return await ReadAs<ImportReportDto>(res);
    }

    private static int Created(ImportReportDto report, string entity) =>
        report.Entities.Single(e => e.Entity == entity).Created;

    private async Task<Dictionary<string, ResourceDto>> PeopleByEmail()
    {
        var page = await Client.GetFromJsonAsync<Page<ResourceDto>>(
            "/v1/resources?pageSize=200", ApiFixture.Json);
        return page!.Items.ToDictionary(r => r.Email);
    }

    private async Task<List<AllocationDto>> Allocations()
    {
        var page = await Client.GetFromJsonAsync<Page<AllocationDto>>(
            "/v1/allocations?pageSize=200", ApiFixture.Json);
        return page!.Items.ToList();
    }

    // ---- FR-IMP-5: dry run --------------------------------------------------

    [Fact]
    public async Task Dry_run_reports_counts_but_writes_nothing()
    {
        var report = await Import(dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(3, Created(report, "clients"));   // NASA, SRA, ESA
        Assert.Equal(4, Created(report, "projects"));  // Apollo, Gemini, Bench Tasks, Voyager
        Assert.Equal(4, Created(report, "resources"));

        var clients = await Client.GetFromJsonAsync<Page<ClientDto>>("/v1/clients", ApiFixture.Json);
        Assert.Empty(clients!.Items);
        var resources = await Client.GetFromJsonAsync<Page<ResourceDto>>("/v1/resources", ApiFixture.Json);
        Assert.Empty(resources!.Items);
    }

    [Fact]
    public async Task Dry_run_and_commit_report_the_same_counts()
    {
        var preview = await Import(dryRun: true);
        var committed = await Import(dryRun: false);

        Assert.False(committed.DryRun);
        Assert.Equal(
            preview.Entities.Select(e => (e.Entity, e.Created, e.Updated, e.Skipped)),
            committed.Entities.Select(e => (e.Entity, e.Created, e.Updated, e.Skipped)));
    }

    // ---- FR-IMP-2: people and derived values -------------------------------

    [Fact]
    public async Task People_import_with_profile_and_derived_availability()
    {
        await Import(dryRun: false);

        var people = await PeopleByEmail();
        var ada = people[Ada];

        Assert.Equal("Ada Lovelace", ada.Name);
        Assert.Equal("Principal Consultant", ada.PrimaryJobTitle);
        Assert.Equal("Tech Lead", ada.JobRole);
        Assert.Equal("ENG", ada.Department);
        Assert.Equal("SA", ada.Location);
        Assert.Equal("0400 000 001", ada.Phone);
        Assert.Equal([".NET", "Postgres"], ada.Skills);
        Assert.Equal(["Python"], ada.SecondarySkills);
        Assert.Equal(["NPC", "WWCC"], ada.SecurityClearances);
        Assert.Equal(new DateOnly(2023, 9, 28), ada.SecurityNpcObtainedOn);
        Assert.Equal(["PRINCE2 Foundation"], ada.Certifications);

        // 8 hours over five weekdays, from the Availability sheet.
        Assert.Equal(40.02m, ada.AvailabilityHoursPerWeek);
        Assert.Equal(5, ada.WorkingDays.Count);
        Assert.DoesNotContain(Weekday.Saturday, ada.WorkingDays);

        var grace = people[Grace];
        Assert.Equal(20m, grace.AvailabilityHoursPerWeek);
        Assert.Equal(210.50m, grace.DefaultRateHourly);

        // Absent from the Availability sheet: a standard week is assumed.
        var linus = people[Linus];
        Assert.Equal(40m, linus.AvailabilityHoursPerWeek);
        Assert.Equal("Developer", linus.PrimaryJobTitle); // job role stands in for a missing title
    }

    [Fact]
    public async Task Availability_ignores_days_zeroed_by_leave()
    {
        var report = await Import(dryRun: false);
        var people = await PeopleByEmail();

        // Ada's Australia Day and her leave week are zeroed in the source. They
        // describe her leave, not her contract, so her week is still 40 hours.
        Assert.Equal(40.02m, people[Ada].AvailabilityHoursPerWeek);
        Assert.Equal(5, people[Ada].WorkingDays.Count);

        // Edsger was bookable on exactly one day in the window: too little to
        // believe, so he gets a standard week and a warning rather than no
        // capacity at all.
        Assert.Equal(40m, people[Edsger].AvailabilityHoursPerWeek);
        Assert.Equal(5, people[Edsger].WorkingDays.Count);

        var warning = report.Warnings.Single(w => w.Code == "availability.notObserved");
        Assert.Equal([Edsger], warning.Examples);
    }

    [Fact]
    public async Task Manager_resolves_by_name_and_an_unknown_manager_warns()
    {
        var report = await Import(dryRun: false);

        var people = await PeopleByEmail();
        Assert.Equal(people[Grace].Id, people[Ada].ManagerId);

        var warning = report.Warnings.Single(w => w.Code == "resource.managerUnresolved");
        Assert.Equal(1, warning.Count);
        Assert.Contains("Nobody Here", warning.Examples);
    }

    // ---- FR-IMP-3: the day-row fold ----------------------------------------

    [Fact]
    public async Task Consecutive_booked_days_fold_into_one_allocation_with_weekly_effort()
    {
        await Import(dryRun: false);

        var ada = (await PeopleByEmail())[Ada];
        var apollo = (await Allocations())
            .Where(a => a.ResourceId == ada.Id)
            .OrderBy(a => a.StartDate)
            .ToList();

        Assert.Equal(2, apollo.Count);

        // Mon 5 Jan to Tue 13 Jan: one allocation, because only a weekend breaks it.
        Assert.Equal(Mon1, apollo[0].StartDate);
        Assert.Equal(Tue2, apollo[0].EndDate);
        Assert.Equal(EffortUnit.HoursPerWeek, apollo[0].EffortUnit);
        Assert.Equal(20m, apollo[0].Effort); // 4 h/day x 5 working days
        Assert.True(apollo[0].Billable);

        // A different daily figure starts a new allocation.
        Assert.Equal(Wed2, apollo[1].StartDate);
        Assert.Equal(Wed2, apollo[1].EndDate);
        Assert.Equal(40m, apollo[1].Effort);
    }

    [Fact]
    public async Task Booker_is_imported_as_business_data()
    {
        var report = await Import(dryRun: false);

        var people = await PeopleByEmail();
        var allocations = await Allocations();
        var adaBookings = allocations.Where(a => a.ResourceId == people[Ada].Id).ToList();

        Assert.NotEmpty(adaBookings);
        Assert.All(adaBookings, a => Assert.Equal(people[Grace].Id, a.BookerId));

        // "Admin Admin" is a Resource Guru account, not a person in the export.
        Assert.Contains(report.Warnings, w => w.Code == "booking.unknownBooker");
    }

    [Fact]
    public async Task Non_billable_bookings_keep_their_flag_and_scale_to_a_part_time_week()
    {
        await Import(dryRun: false);

        var grace = (await PeopleByEmail())[Grace];
        var bench = (await Allocations())
            .Single(a => a.ResourceId == grace.Id && a.BookingStatus == BookingStatus.Confirmed);

        Assert.False(bench.Billable);
        Assert.Equal(Mon1, bench.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 6), bench.EndDate);
        Assert.Equal(10m, bench.Effort); // 2 h/day x Grace's five working days
    }

    // ---- FR-IMP-2: projects -------------------------------------------------

    [Fact]
    public async Task Project_without_a_code_gets_a_generated_one()
    {
        var report = await Import(dryRun: false);

        var page = await Client.GetFromJsonAsync<Page<ProjectDto>>(
            "/v1/projects?pageSize=200", ApiFixture.Json);
        var bench = page!.Items.Single(p => p.Name == "Bench Tasks");

        Assert.StartsWith("RG-", bench.Code);
        Assert.Contains("BENCH-TASKS", bench.Code);
        Assert.Contains(report.Warnings, w => w.Code == "project.codeSynthesised");
    }

    [Fact]
    public async Task Two_projects_sharing_a_code_are_kept_apart()
    {
        var report = await Import(dryRun: false);

        var page = await Client.GetFromJsonAsync<Page<ProjectDto>>(
            "/v1/projects?pageSize=200", ApiFixture.Json);
        var apollo = page!.Items.Single(p => p.Name == "Apollo");
        var gemini = page.Items.Single(p => p.Name == "Gemini");

        Assert.Equal("APL-1", apollo.Code);
        Assert.Equal("APL-1-2", gemini.Code);
        Assert.Contains(report.Warnings, w => w.Code == "project.duplicateCode");
    }

    [Fact]
    public async Task Project_window_covers_its_bookings_and_a_project_with_none_still_imports()
    {
        await Import(dryRun: false);

        var page = await Client.GetFromJsonAsync<Page<ProjectDto>>(
            "/v1/projects?pageSize=200", ApiFixture.Json);

        var apollo = page!.Items.Single(p => p.Name == "Apollo");
        Assert.Equal(Mon1, apollo.StartDate);
        Assert.Equal(Wed2, apollo.EndDate);

        // Present only in Scheduled Vs Actuals — no bookings, but a real project.
        var voyager = page.Items.Single(p => p.Name == "Voyager");
        Assert.Equal(new DateOnly(2026, 3, 2), voyager.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 6), voyager.EndDate);
        Assert.Equal("ESA", voyager.ClientName);
    }

    // ---- FR-IMP-2: time off -------------------------------------------------

    [Fact]
    public async Task Downtime_imports_as_time_off_with_mapped_types()
    {
        await Import(dryRun: false);

        var page = await Client.GetFromJsonAsync<Page<TimeOffDto>>(
            "/v1/timeoff?pageSize=200&from=2026-01-01&to=2026-12-31", ApiFixture.Json);
        var rows = page!.Items.OrderBy(t => t.StartDate).ToList();
        Assert.Equal(3, rows.Count);

        var sick = rows.Single(t => t.Type == TimeOffType.Sick);
        Assert.Equal(new DateOnly(2026, 1, 7), sick.StartDate);
        Assert.Equal(2m, sick.HoursPerDay); // two hours off a four-hour day

        // Australia Day 2026 is a Monday, one of Ada's 8.02-hour days, and the
        // source records 8 hours off. That 36-second shortfall is not a part day.
        var holiday = rows.Single(t => t.Type == TimeOffType.PublicHoliday);
        Assert.Equal(new DateOnly(2026, 1, 26), holiday.StartDate);
        Assert.Null(holiday.HoursPerDay);
        Assert.Equal("Australia Day", holiday.Note);

        // Monday to Friday of leave is one record, not five.
        var annual = rows.Single(t => t.Type == TimeOffType.AnnualLeave);
        Assert.Equal(new DateOnly(2026, 2, 2), annual.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 6), annual.EndDate);
    }

    // ---- FR-IMP-2: reference pick-lists ------------------------------------

    [Fact]
    public async Task Reference_pick_lists_are_populated_from_the_export()
    {
        await Import(dryRun: false);

        var skills = (await Client.GetFromJsonAsync<List<ReferenceItemDto>>(
            "/v1/reference/skills", ApiFixture.Json))!.Select(s => s.Value).ToList();
        Assert.Contains(".NET", skills);
        Assert.Contains("Python", skills); // secondary skills count too

        var locations = await Client.GetFromJsonAsync<List<ReferenceItemDto>>(
            "/v1/reference/locations", ApiFixture.Json);
        Assert.Equal(["NT", "SA"], locations!.Select(l => l.Value).Order());

        var activities = await Client.GetFromJsonAsync<List<ReferenceItemDto>>(
            "/v1/reference/activityTypes", ApiFixture.Json);
        Assert.Equal(["Design"], activities!.Select(a => a.Value));
    }

    // ---- FR-IMP-4: re-runs --------------------------------------------------

    [Fact]
    public async Task Re_running_the_same_export_creates_nothing_new()
    {
        await Import(dryRun: false);
        var again = await Import(dryRun: false);

        Assert.All(again.Entities, e => Assert.Equal(0, e.Created));
        Assert.Equal(3, again.Entities.Single(e => e.Entity == "clients").Skipped);
        Assert.Equal(4, again.Entities.Single(e => e.Entity == "resources").Skipped);

        var allocations = await Client.GetFromJsonAsync<Page<AllocationDto>>(
            "/v1/allocations?pageSize=200", ApiFixture.Json);
        Assert.Equal(5, allocations!.Meta.TotalItems);
    }

    // ---- FR-IMP-6: report ---------------------------------------------------

    [Fact]
    public async Task Report_lists_sheets_rows_and_unmapped_fields()
    {
        var report = await Import(dryRun: true);

        Assert.Contains("bookings", report.SheetsRead);
        Assert.Contains("downtime", report.SheetsRead);
        Assert.Equal(4, report.SourceRows.Single(r => r.Sheet == "resources").Rows);
        Assert.Equal(5, report.SourceFiles.Count);
        // Booking Status is imported as of V004, so it is no longer a loss.
        Assert.DoesNotContain(report.UnmappedFields, f => f.Field == "Booking Status");

        // Nothing is listed as unmapped unless a source row actually carried a value.
        Assert.DoesNotContain(report.UnmappedFields, f => f.NonEmptyRows == 0);
    }

    // ---- validation ---------------------------------------------------------

    [Fact]
    public async Task Upload_that_is_not_an_archive_is_rejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0x00, 0x01, 0x02, 0x03]), "file", "export.zip");

        var res = await Client.PostAsync("/v1/import/resource-guru?dryRun=true", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Archive_without_recognised_sheets_is_rejected()
    {
        var export = Zip([("notes.txt", "nothing to see here")]);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(export), "file", "export.zip");

        var res = await Client.PostAsync("/v1/import/resource-guru?dryRun=true", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Single_csv_upload_imports_just_that_sheet()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(ResourceSheet()), "file",
            "Resource Guru Resource Data 1 Jan - 31 Mar 2026.csv");

        var res = await Client.PostAsync("/v1/import/resource-guru?dryRun=false", form);
        res.EnsureSuccessStatusCode();
        var report = await ReadAs<ImportReportDto>(res);

        Assert.Equal(4, Created(report, "resources"));
        Assert.Equal(0, Created(report, "clients"));
    }

    // ---- FR-IMP-3 / FR-ALL-9: booking status --------------------------------

    [Fact]
    public async Task Booking_status_is_imported_and_splits_otherwise_identical_bookings()
    {
        var report = await Import(dryRun: false);
        var people = await PeopleByEmail();
        var allocations = await Allocations();

        var tentative = allocations.Single(a => a.ResourceId == people[Linus].Id);
        Assert.Equal(BookingStatus.Tentative, tentative.BookingStatus);

        // Grace was booked on Bench Tasks twice on the same day, for the same
        // hours and billability, differing only in status. Both must survive.
        var bench = allocations.Where(a => a.ResourceId == people[Grace].Id).ToList();
        Assert.Equal(2, bench.Count);
        Assert.Equal(
            [BookingStatus.Confirmed, BookingStatus.Waiting],
            bench.Select(a => a.BookingStatus).Order());

        // Everything else in the export was confirmed.
        Assert.Equal(BookingStatus.Confirmed,
            allocations.Single(a => a.ResourceId == people[Ada].Id && a.StartDate == Mon1)
                .BookingStatus);

        // The report says how much of the load arrived unconfirmed.
        var warning = report.Warnings.Single(w => w.Code == "booking.unconfirmed");
        Assert.Equal(1, warning.Count);
        Assert.Contains("2 booking row(s)", warning.Message);
    }
}
