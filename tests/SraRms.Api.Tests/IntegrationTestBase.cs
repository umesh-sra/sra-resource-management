using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

[Collection("api")]
public abstract class IntegrationTestBase(ApiFixture fx) : IAsyncLifetime
{
    protected ApiFixture Fx { get; } = fx;
    protected HttpClient Client => Fx.Client;

    // Reset the DB before each test so tests are independent (collection runs serially).
    public Task InitializeAsync() => Fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- helpers ----
    protected async Task<HttpResponseMessage> PostJson(string url, object body) =>
        await Client.PostAsJsonAsync(url, body, ApiFixture.Json);

    protected async Task<T> ReadAs<T>(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;

    protected async Task<ClientDto> CreateClient(string name)
    {
        var res = await PostJson("/v1/clients", new { name });
        res.EnsureSuccessStatusCode();
        return await ReadAs<ClientDto>(res);
    }

    protected async Task<ProjectDto> CreateProject(
        Guid clientId, string code, DateOnly start, DateOnly end, bool billable = true)
    {
        var res = await PostJson("/v1/projects", new
        {
            name = $"Project {code}", code, clientId,
            startDate = start, endDate = end, billable, status = "active",
        });
        res.EnsureSuccessStatusCode();
        return await ReadAs<ProjectDto>(res);
    }

    protected async Task<ResourceDto> CreateResource(string email, decimal availabilityHoursPerWeek)
    {
        var res = await PostJson("/v1/resources", new
        {
            name = email.Split('@')[0],
            email,
            primaryJobTitle = "Engineer",
            availabilityHoursPerWeek,
            skills = new[] { "C#" },
        });
        res.EnsureSuccessStatusCode();
        return await ReadAs<ResourceDto>(res);
    }
}
