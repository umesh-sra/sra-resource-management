using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class ResourcesTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        await CreateResource("dup@sra.com.au", 38);
        var res = await PostJson("/v1/resources", new
        {
            name = "Dup", email = "DUP@sra.com.au", // case-insensitive
            primaryJobTitle = "Engineer", availabilityHoursPerWeek = 38,
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Skill_filter_uses_AND_semantics()
    {
        await PostJson("/v1/resources", new
        {
            name = "Full Stack", email = "fs@sra.com.au", primaryJobTitle = "Engineer",
            availabilityHoursPerWeek = 38, skills = new[] { "C#", "Vue.js" },
        });
        await PostJson("/v1/resources", new
        {
            name = "Backend", email = "be@sra.com.au", primaryJobTitle = "Engineer",
            availabilityHoursPerWeek = 38, skills = new[] { "C#" },
        });

        // Requires BOTH skills -> only the full-stack resource matches.
        var page = await Client.GetFromJsonAsync<Page<ResourceDto>>(
            "/v1/resources?skill=C%23&skill=Vue.js", ApiFixture.Json);

        Assert.Equal(1, page!.Meta.TotalItems);
        Assert.Equal("fs@sra.com.au", page.Items[0].Email);
    }
}
