using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class ProjectsTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    private static object UpdateBody(ProjectDto p, DateOnly start, DateOnly end) => new
    {
        name = p.Name,
        code = p.Code,
        clientId = p.ClientId,
        startDate = start,
        endDate = end,
        budget = p.Budget,
        remaining = p.Remaining,
        billable = p.Billable,
        status = "active",
    };

    private async Task<(ProjectDto Project, ResourceDto Resource)> SeedProjectWithResource()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACM-1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var resource = await CreateResource("jane.smith@sra.com.au", 38);
        return (project, resource);
    }

    private async Task AllocateAsync(Guid projectId, Guid resourceId, DateOnly start, DateOnly end)
    {
        var res = await PostJson($"/v1/projects/{projectId}/allocations", new
        {
            resourceId,
            startDate = start,
            endDate = end,
            effort = 20,
            effortUnit = "hoursPerWeek",
        });
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Shrinking_window_with_stranded_allocation_returns_409()
    {
        var (project, resource) = await SeedProjectWithResource();
        await AllocateAsync(project.Id, resource.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 6, 30));

        // New window starts after the allocation does -> allocation is stranded.
        var update = await Client.PutAsJsonAsync($"/v1/projects/{project.Id}",
            UpdateBody(project, new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31)), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal("application/problem+json", update.Content.Headers.ContentType?.MediaType);
        var body = await update.Content.ReadAsStringAsync();
        Assert.Contains("outside the new project window", body);

        // Project window is unchanged.
        var detail = await Client.GetFromJsonAsync<ProjectDetailDto>($"/v1/projects/{project.Id}", ApiFixture.Json);
        Assert.Equal(new DateOnly(2026, 1, 1), detail!.StartDate);
    }

    [Fact]
    public async Task Shrinking_window_that_still_covers_allocations_succeeds()
    {
        var (project, resource) = await SeedProjectWithResource();
        await AllocateAsync(project.Id, resource.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 6, 30));

        // Shrink to exactly the allocation's span -> still valid.
        var update = await Client.PutAsJsonAsync($"/v1/projects/{project.Id}",
            UpdateBody(project, new DateOnly(2026, 2, 1), new DateOnly(2026, 6, 30)), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var dto = await ReadAs<ProjectDto>(update);
        Assert.Equal(new DateOnly(2026, 6, 30), dto.EndDate);
    }

    [Fact]
    public async Task Widening_window_succeeds_with_allocations_present()
    {
        var (project, resource) = await SeedProjectWithResource();
        await AllocateAsync(project.Id, resource.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 6, 30));

        var update = await Client.PutAsJsonAsync($"/v1/projects/{project.Id}",
            UpdateBody(project, new DateOnly(2025, 12, 1), new DateOnly(2027, 6, 30)), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task Shrinking_window_with_no_allocations_succeeds()
    {
        var client = await CreateClient("Globex");
        var project = await CreateProject(client.Id, "GLX-1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var update = await Client.PutAsJsonAsync($"/v1/projects/{project.Id}",
            UpdateBody(project, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }
}
