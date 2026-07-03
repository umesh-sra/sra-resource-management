using System.Net;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class AllocationsTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    [Fact]
    public async Task Allocation_within_capacity_has_no_warnings()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("ava@sra.com.au", availabilityHoursPerWeek: 38);

        var res = await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id,
            startDate = Start, endDate = End,
            effort = 30, effortUnit = "hoursPerWeek",
        });
        res.EnsureSuccessStatusCode();
        var alloc = await ReadAs<AllocationDto>(res);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Empty(alloc.Warnings);
        Assert.True(alloc.Billable); // defaulted from the (billable) project
    }

    [Fact]
    public async Task Overlapping_allocations_beyond_availability_warn()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("liam@sra.com.au", availabilityHoursPerWeek: 38);

        // 30h then an overlapping 15h => 45h > 38h availability.
        await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id, startDate = Start, endDate = new DateOnly(2026, 8, 31),
            effort = 30, effortUnit = "hoursPerWeek",
        });
        var res = await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id, startDate = new DateOnly(2026, 8, 1), endDate = new DateOnly(2026, 9, 30),
            effort = 15, effortUnit = "hoursPerWeek",
        });
        var alloc = await ReadAs<AllocationDto>(res);

        Assert.NotEmpty(alloc.Warnings);
        Assert.Contains("Over-allocation", alloc.Warnings[0]);
    }

    [Fact]
    public async Task Allocation_outside_project_window_is_rejected()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("sofia@sra.com.au", availabilityHoursPerWeek: 38);

        var res = await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id,
            startDate = new DateOnly(2026, 1, 1), endDate = new DateOnly(2026, 2, 1),
            effort = 10, effortUnit = "hoursPerWeek",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
