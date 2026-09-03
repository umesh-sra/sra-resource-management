using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;
using SraRms.Api.Data;

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

    // ---- V004: booking status (FR-ALL-9) -----------------------------------

    [Fact]
    public async Task Booking_status_defaults_to_confirmed()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 38);

        // No bookingStatus in the body: a client written against the previous
        // contract must keep working, and its bookings must land as firm.
        var res = await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 10, effortUnit = "hoursPerWeek",
        });
        res.EnsureSuccessStatusCode();

        var created = await ReadAs<AllocationDto>(res);
        Assert.Equal(BookingStatus.Confirmed, created.BookingStatus);
    }

    [Fact]
    public async Task Booking_status_round_trips_and_can_be_changed()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 38);

        var res = await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 10, effortUnit = "hoursPerWeek",
            bookingStatus = "tentative",
        });
        res.EnsureSuccessStatusCode();
        var created = await ReadAs<AllocationDto>(res);
        Assert.Equal(BookingStatus.Tentative, created.BookingStatus);

        var fetched = await ReadAs<AllocationDto>(await Client.GetAsync($"/v1/allocations/{created.Id}"));
        Assert.Equal(BookingStatus.Tentative, fetched.BookingStatus);

        // Firming a booking up is the whole point of the field.
        var put = await Client.PutAsJsonAsync($"/v1/allocations/{created.Id}", new
        {
            startDate = Start, endDate = End, effort = 10, effortUnit = "hoursPerWeek",
            bookingStatus = "confirmed",
        }, ApiFixture.Json);
        put.EnsureSuccessStatusCode();
        Assert.Equal(BookingStatus.Confirmed, (await ReadAs<AllocationDto>(put)).BookingStatus);
    }

    [Fact]
    public async Task Allocations_can_be_filtered_to_the_unconfirmed()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 38);

        foreach (var status in new[] { "confirmed", "tentative", "waiting" })
            (await PostJson("/v1/allocations", new
            {
                projectId = project.Id, resourceId = resource.Id,
                startDate = Start, endDate = End, effort = 1, effortUnit = "hoursPerWeek",
                bookingStatus = status, details = status,
            })).EnsureSuccessStatusCode();

        var tentative = await ReadAs<Page<AllocationDto>>(
            await Client.GetAsync("/v1/allocations?bookingStatus=tentative"));
        Assert.Equal(1, tentative.Meta.TotalItems);
        Assert.Equal(BookingStatus.Tentative, tentative.Items[0].BookingStatus);

        var all = await ReadAs<Page<AllocationDto>>(await Client.GetAsync("/v1/allocations"));
        Assert.Equal(3, all.Meta.TotalItems);
    }

    [Fact]
    public async Task Tentative_booking_still_counts_toward_over_allocation()
    {
        // FR-ALL-9: the status is descriptive. A pencilled-in booking that would
        // push someone past their availability is exactly when a warning matters,
        // so it must not be excused by being tentative.
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 38);

        (await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 30, effortUnit = "hoursPerWeek",
        })).EnsureSuccessStatusCode();

        var res = await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 20, effortUnit = "hoursPerWeek",
            bookingStatus = "tentative",
        });
        res.EnsureSuccessStatusCode();

        var created = await ReadAs<AllocationDto>(res);
        Assert.NotEmpty(created.Warnings);
    }

    [Fact]
    public async Task Utilisation_reports_the_unconfirmed_share_without_deducting_it()
    {
        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", Start, End);
        var resource = await CreateResource("noah@sra.com.au", availabilityHoursPerWeek: 40);

        (await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 10, effortUnit = "hoursPerWeek",
        })).EnsureSuccessStatusCode();
        (await PostJson("/v1/allocations", new
        {
            projectId = project.Id, resourceId = resource.Id,
            startDate = Start, endDate = End, effort = 10, effortUnit = "hoursPerWeek",
            bookingStatus = "waiting",
        })).EnsureSuccessStatusCode();

        var report = await ReadAs<UtilisationReportDto>(await Client.GetAsync(
            $"/v1/reports/utilisation?from={Start:yyyy-MM-dd}&to={End:yyyy-MM-dd}"));
        var row = report.Rows.Single(r => r.ResourceId == resource.Id);

        // Half the load is unconfirmed, and all of it still counts as allocated.
        Assert.Equal(row.AllocatedHours / 2, row.UnconfirmedHours, 2);
        Assert.Equal(row.AllocatedHours / row.AvailableHours, row.Utilisation, 3);
    }
}
