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

    // ---- V003: Details and Booker (screens/shedule_booking.png) ------------

    [Fact]
    public async Task Booking_round_trips_details_booker_and_rate()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 38);
        var booker = await CreateResource("booker@sra.com.au", availabilityHoursPerWeek: 38);

        var res = await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End,
            effort = 10, effortUnit = "hoursPerWeek",
            details = "Discovery workshop, on site Tuesdays.",
            bookerId = booker.Id,
            hourlyRate = 185.50m,
        });
        res.EnsureSuccessStatusCode();
        var created = await ReadAs<AllocationDto>(res);

        Assert.Equal("Discovery workshop, on site Tuesdays.", created.Details);
        Assert.Equal(booker.Id, created.BookerId);
        Assert.Equal("booker", created.BookerName);
        Assert.Equal(185.50m, created.HourlyRate);

        // The values survive a re-read, not just the create response.
        var fetched = await ReadAs<AllocationDto>(await Client.GetAsync($"/v1/allocations/{created.Id}"));
        Assert.Equal(created.Details, fetched.Details);
        Assert.Equal(booker.Id, fetched.BookerId);
        Assert.Equal("booker", fetched.BookerName);
        Assert.Equal(185.50m, fetched.HourlyRate);
    }

    [Fact]
    public async Task Booking_with_unknown_booker_is_rejected()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("mia@sra.com.au", availabilityHoursPerWeek: 38);

        var res = await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End,
            effort = 10, effortUnit = "hoursPerWeek",
            bookerId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_booker_nulls_the_reference_rather_than_blocking()
    {
        // Being named as a booker is descriptive, like resource.manager_id: it
        // must not make the person undeletable (V003).
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("leo@sra.com.au", availabilityHoursPerWeek: 38);
        var booker = await CreateResource("temp@sra.com.au", availabilityHoursPerWeek: 38);

        var created = await ReadAs<AllocationDto>(await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End,
            effort = 10, effortUnit = "hoursPerWeek",
            bookerId = booker.Id,
        }));

        var del = await Client.DeleteAsync($"/v1/resources/{booker.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var fetched = await ReadAs<AllocationDto>(await Client.GetAsync($"/v1/allocations/{created.Id}"));
        Assert.Null(fetched.BookerId);
        Assert.Null(fetched.BookerName);
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
