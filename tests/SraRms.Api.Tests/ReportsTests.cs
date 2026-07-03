using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class ReportsTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    [Fact]
    public async Task Utilisation_report_computes_full_allocation_as_100_percent()
    {
        var start = new DateOnly(2026, 7, 1);
        var end = new DateOnly(2026, 7, 28); // 4 whole weeks

        var client = await CreateClient("Acme");
        var project = await CreateProject(client.Id, "ACME-1", start, end);
        var resource = await CreateResource("ava@sra.com.au", availabilityHoursPerWeek: 38);

        // Allocate the full availability for the whole window.
        var res = await PostJson($"/v1/projects/{project.Id}/allocations", new
        {
            resourceId = resource.Id, startDate = start, endDate = end,
            effort = 38, effortUnit = "hoursPerWeek",
        });
        res.EnsureSuccessStatusCode();

        var report = await Client.GetFromJsonAsync<UtilisationReportDto>(
            $"/v1/reports/utilisation?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", ApiFixture.Json);

        Assert.NotNull(report);
        var row = Assert.Single(report!.Rows);
        Assert.Equal(resource.Id, row.ResourceId);
        Assert.Equal(1.0, row.Utilisation, precision: 2); // ~100%
    }

    [Fact]
    public async Task Utilisation_report_exports_csv()
    {
        var start = new DateOnly(2026, 7, 1);
        var end = new DateOnly(2026, 7, 28);
        await CreateResource("solo@sra.com.au", 38);

        var res = await Client.GetAsync(
            $"/v1/reports/utilisation?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}&format=csv");
        res.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("resourceId,resourceName,department,availableHours,allocatedHours,utilisation", csv);
        Assert.Contains("solo@sra.com.au".Split('@')[0], csv);
    }
}
