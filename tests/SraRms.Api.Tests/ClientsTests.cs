using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class ClientsTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    [Fact]
    public async Task List_is_empty_initially()
    {
        var page = await Client.GetFromJsonAsync<Page<ClientDto>>("/v1/clients", ApiFixture.Json);
        Assert.NotNull(page);
        Assert.Equal(0, page!.Meta.TotalItems);
    }

    [Fact]
    public async Task Create_then_appears_in_list()
    {
        var created = await CreateClient("Acme Corporation");
        Assert.NotEqual(Guid.Empty, created.Id);

        var page = await Client.GetFromJsonAsync<Page<ClientDto>>("/v1/clients", ApiFixture.Json);
        Assert.Equal(1, page!.Meta.TotalItems);
        Assert.Equal("Acme Corporation", page.Items[0].Name);
    }

    [Fact]
    public async Task Duplicate_name_returns_409()
    {
        await CreateClient("Globex");
        var res = await PostJson("/v1/clients", new { name = "globex" }); // case-insensitive
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Delete_with_projects_requires_cascade()
    {
        var client = await CreateClient("Initech");
        await CreateProject(client.Id, "INI-1", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31));

        var blocked = await Client.DeleteAsync($"/v1/clients/{client.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var cascaded = await Client.DeleteAsync($"/v1/clients/{client.Id}?cascade=true");
        Assert.Equal(HttpStatusCode.NoContent, cascaded.StatusCode);

        var page = await Client.GetFromJsonAsync<Page<ClientDto>>("/v1/clients", ApiFixture.Json);
        Assert.Equal(0, page!.Meta.TotalItems);
    }
}
