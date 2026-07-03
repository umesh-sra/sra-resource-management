using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

/// <summary>
/// Detail endpoints eager-load related data. Regression cover for the cyclic
/// Include bug (Client->Projects->Client etc.) that made them return 500.
/// </summary>
public class DetailEndpointsTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private async Task<(ClientDto client, ProjectDto project, ResourceDto resource)> Seed()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("ava@sra.com.au", 38);
        var res = await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id, startDate = Start, endDate = End,
            effort = 20, effortUnit = "hoursPerWeek", roleOnProject = "Engineer",
        });
        res.EnsureSuccessStatusCode();
        return (client, project, resource);
    }

    [Fact]
    public async Task Client_detail_loads_projects_and_team()
    {
        var (client, _, resource) = await Seed();

        var detail = await Client.GetFromJsonAsync<ClientDetailDto>($"/v1/clients/{client.Id}", ApiFixture.Json);

        Assert.NotNull(detail);
        var project = Assert.Single(detail!.Projects);
        Assert.Equal("Acme", project.ClientName); // inverse-nav fixup still populates this
        Assert.Contains(detail.Team, m => m.Id == resource.Id);
    }

    [Fact]
    public async Task Project_detail_loads_allocations_with_resource_name()
    {
        var (_, project, resource) = await Seed();

        var detail = await Client.GetFromJsonAsync<ProjectDetailDto>($"/v1/projects/{project.Id}", ApiFixture.Json);

        Assert.NotNull(detail);
        var alloc = Assert.Single(detail!.Allocations);
        Assert.Equal(resource.Id, alloc.ResourceId);
        Assert.Equal(resource.Name, alloc.ResourceName);
    }

    [Fact]
    public async Task Resource_detail_loads_allocations_and_capacity()
    {
        var (_, project, resource) = await Seed();

        var detail = await Client.GetFromJsonAsync<ResourceDetailDto>($"/v1/resources/{resource.Id}", ApiFixture.Json);

        Assert.NotNull(detail);
        var alloc = Assert.Single(detail!.Allocations);
        Assert.Equal(project.Id, alloc.ProjectId);
        Assert.Equal("Project ACME-1", alloc.ProjectName);
        Assert.Equal(20, detail.AllocatedHoursPerWeek); // 20h/wk, allocation spans today
    }
}
