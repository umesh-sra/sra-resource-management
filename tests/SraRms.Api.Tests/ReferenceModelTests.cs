using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;
using SraRms.Api.Data;

namespace SraRms.Api.Tests;

/// <summary>
/// Covers the V002 model: project phases and milestones, time off, the richer
/// person profile, and budget-type validation.
/// </summary>
public class ReferenceModelTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private async Task<ProjectDto> SeedProject() =>
        await CreateProject((await CreateClient("Acme")).Id, "ACM-1", Start, End);

    // ---- Phases (FR-PHASE-*) ------------------------------------------------

    [Fact]
    public async Task Phase_inside_project_window_is_created()
    {
        var project = await SeedProject();
        var res = await PostJson($"/v1/projects/{project.Id}/phases", new
        {
            name = "Discovery",
            startDate = new DateOnly(2026, 2, 1),
            endDate = new DateOnly(2026, 3, 31),
            colour = "#0B3B73",
            sortOrder = 1,
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var phase = await ReadAs<ProjectPhaseDto>(res);
        Assert.Equal("Discovery", phase.Name);
        Assert.Equal(project.Id, phase.ProjectId);
    }

    [Fact]
    public async Task Phase_outside_project_window_returns_400()
    {
        var project = await SeedProject();
        var res = await PostJson($"/v1/projects/{project.Id}/phases", new
        {
            name = "Too early",
            startDate = new DateOnly(2025, 1, 1),
            endDate = new DateOnly(2025, 6, 30),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Phase_end_before_start_returns_400()
    {
        var project = await SeedProject();
        var res = await PostJson($"/v1/projects/{project.Id}/phases", new
        {
            name = "Backwards",
            startDate = new DateOnly(2026, 6, 1),
            endDate = new DateOnly(2026, 5, 1),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Project_detail_returns_phases_and_milestones_in_order()
    {
        var project = await SeedProject();
        // Created out of order; the detail response must sort them.
        await PostJson($"/v1/projects/{project.Id}/phases",
            new { name = "Build", startDate = new DateOnly(2026, 4, 1), endDate = new DateOnly(2026, 8, 1), sortOrder = 2 });
        await PostJson($"/v1/projects/{project.Id}/phases",
            new { name = "Discovery", startDate = new DateOnly(2026, 2, 1), endDate = new DateOnly(2026, 3, 31), sortOrder = 1 });
        await PostJson($"/v1/projects/{project.Id}/milestones",
            new { name = "Go live", dueDate = new DateOnly(2026, 9, 1) });
        await PostJson($"/v1/projects/{project.Id}/milestones",
            new { name = "Design sign-off", dueDate = new DateOnly(2026, 3, 15) });

        var detail = await Client.GetFromJsonAsync<ProjectDetailDto>($"/v1/projects/{project.Id}", ApiFixture.Json);

        Assert.Equal(["Discovery", "Build"], detail!.Phases.Select(p => p.Name));
        Assert.Equal(["Design sign-off", "Go live"], detail.Milestones.Select(m => m.Name));
    }

    [Fact]
    public async Task Milestone_outside_project_window_returns_400()
    {
        var project = await SeedProject();
        var res = await PostJson($"/v1/projects/{project.Id}/milestones",
            new { name = "Late", dueDate = new DateOnly(2027, 6, 1) });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Deleting_project_with_phases_returns_409_then_cascades()
    {
        var project = await SeedProject();
        await PostJson($"/v1/projects/{project.Id}/phases",
            new { name = "Discovery", startDate = new DateOnly(2026, 2, 1), endDate = new DateOnly(2026, 3, 31) });

        var blocked = await Client.DeleteAsync($"/v1/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var cascaded = await Client.DeleteAsync($"/v1/projects/{project.Id}?cascade=true");
        Assert.Equal(HttpStatusCode.NoContent, cascaded.StatusCode);
    }

    // ---- Budget type (V002) -------------------------------------------------

    [Theory]
    [InlineData("fee", null)]
    [InlineData("hours", null)]
    public async Task Budget_type_without_its_amount_returns_400(string budgetType, decimal? _)
    {
        var client = await CreateClient("Acme");
        var res = await PostJson("/v1/projects", new
        {
            name = "P", code = "P-1", clientId = client.Id,
            startDate = Start, endDate = End, billable = true, status = "active",
            budgetType,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Hours_budget_round_trips()
    {
        var client = await CreateClient("Acme");
        var res = await PostJson("/v1/projects", new
        {
            name = "P", code = "P-1", clientId = client.Id,
            startDate = Start, endDate = End, billable = true, status = "active",
            budgetType = "hours", budgetHours = 1200m,
        });
        res.EnsureSuccessStatusCode();

        var project = await ReadAs<ProjectDto>(res);
        Assert.Equal(ProjectBudgetType.Hours, project.BudgetType);
        Assert.Equal(1200m, project.BudgetHours);
        // Remaining defaults to the full budget when not supplied.
        Assert.Equal(1200m, project.RemainingHours);
    }

    // ---- Time off (FR-TIMEOFF-*) --------------------------------------------

    [Fact]
    public async Task Time_off_is_created_and_listed_by_overlap()
    {
        var resource = await CreateResource("leave@sra.com.au", 38);
        var res = await PostJson("/v1/timeoff", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 3, 2),
            endDate = new DateOnly(2026, 3, 6),
            type = "annualLeave",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // A window that only clips the tail must still match (overlap, not containment).
        var page = await Client.GetFromJsonAsync<Page<TimeOffDto>>(
            "/v1/timeoff?from=2026-03-06&to=2026-03-31", ApiFixture.Json);
        Assert.Single(page!.Items);
        Assert.Equal(resource.Id, page.Items[0].ResourceId);
    }

    [Fact]
    public async Task Overlapping_time_off_for_same_resource_returns_409()
    {
        var resource = await CreateResource("leave@sra.com.au", 38);
        var first = await PostJson("/v1/timeoff", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 3, 2),
            endDate = new DateOnly(2026, 3, 6),
        });
        first.EnsureSuccessStatusCode();

        var clash = await PostJson("/v1/timeoff", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 3, 5),
            endDate = new DateOnly(2026, 3, 10),
        });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Time_off_reduces_effective_capacity_but_not_utilisation()
    {
        var resource = await CreateResource("leave@sra.com.au", 38);
        // Mon-Fri of one week: 5 working days at 38/5 = 7.6h => 38h of leave.
        await PostJson("/v1/timeoff", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 3, 2),
            endDate = new DateOnly(2026, 3, 6),
        });

        var report = await Client.GetFromJsonAsync<UtilisationReportDto>(
            "/v1/reports/utilisation?from=2026-03-02&to=2026-03-08", ApiFixture.Json);

        var row = Assert.Single(report!.Rows);
        Assert.Equal(38, row.AvailableHours, 1);
        Assert.Equal(38, row.TimeOffHours, 1);
        Assert.Equal(0, row.EffectiveCapacityHours, 1);
        // Utilisation stays measured against gross availability (see UtilisationRow).
        Assert.Equal(0, row.Utilisation, 4);
    }

    [Fact]
    public async Task Deleting_resource_with_time_off_returns_409_then_cascades()
    {
        var resource = await CreateResource("leave@sra.com.au", 38);
        await PostJson("/v1/timeoff", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 3, 2),
            endDate = new DateOnly(2026, 3, 6),
        });

        var blocked = await Client.DeleteAsync($"/v1/resources/{resource.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var cascaded = await Client.DeleteAsync($"/v1/resources/{resource.Id}?cascade=true");
        Assert.Equal(HttpStatusCode.NoContent, cascaded.StatusCode);
    }

    // ---- Person profile (FR-RES-8) ------------------------------------------

    [Fact]
    public async Task Person_profile_fields_round_trip()
    {
        var manager = await CreateResource("boss@sra.com.au", 38);
        var res = await PostJson("/v1/resources", new
        {
            name = "Umesh Kodippili",
            email = "person@sra.com.au",
            primaryJobTitle = "Capability Lead",
            availabilityHoursPerWeek = 38m,
            skills = new[] { "C#" },
            jobRole = "Tech Lead",
            managerId = manager.Id,
            phone = "+61400000000",
            secondarySkills = new[] { "Vue" },
            securityClearances = new[] { "Baseline" },
            securityNpcObtainedOn = new DateOnly(2025, 6, 1),
            certifications = new[] { "AZ-204" },
            timeZone = "Australia/Adelaide",
            bookableStatus = "bookable",
            publicHolidayCalendar = "AU-SA",
            defaultRateHourly = 185.50m,
            colour = "#F4004E",
        });
        res.EnsureSuccessStatusCode();

        var created = await ReadAs<ResourceDto>(res);
        Assert.Equal("Tech Lead", created.JobRole);
        Assert.Equal(manager.Id, created.ManagerId);
        Assert.Equal(185.50m, created.DefaultRateHourly);

        // The detail endpoint resolves the manager's name for the person panel.
        var detail = await Client.GetFromJsonAsync<ResourceDetailDto>(
            $"/v1/resources/{created.Id}", ApiFixture.Json);
        Assert.Equal("boss", detail!.ManagerName);
        Assert.Equal(["Vue"], detail.SecondarySkills);
        Assert.Equal("Australia/Adelaide", detail.TimeZone);
    }

    [Fact]
    public async Task Resource_cannot_be_its_own_manager()
    {
        var resource = await CreateResource("self@sra.com.au", 38);
        var res = await Client.PutAsJsonAsync($"/v1/resources/{resource.Id}", new
        {
            name = resource.Name,
            email = resource.Email,
            primaryJobTitle = resource.PrimaryJobTitle,
            availabilityHoursPerWeek = resource.AvailabilityHoursPerWeek,
            skills = resource.Skills,
            managerId = resource.Id,
        }, ApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_manager_returns_400()
    {
        var res = await PostJson("/v1/resources", new
        {
            name = "Orphan",
            email = "orphan@sra.com.au",
            primaryJobTitle = "Engineer",
            availabilityHoursPerWeek = 38m,
            managerId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---- Reference data -----------------------------------------------------

    [Fact]
    public async Task Activity_types_reference_collection_round_trips()
    {
        var created = await PostJson("/v1/reference/activityTypes", new { value = "Development" });
        created.EnsureSuccessStatusCode();

        var items = await Client.GetFromJsonAsync<List<ReferenceItemDto>>(
            "/v1/reference/activityTypes", ApiFixture.Json);
        Assert.Contains(items!, i => i.Value == "Development");
    }
}
