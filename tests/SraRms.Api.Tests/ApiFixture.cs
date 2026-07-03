using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace SraRms.Api.Tests;

/// <summary>
/// Shared fixture: spins up a throwaway PostgreSQL container, applies the real
/// V001 migration, and hosts the API in-process pointed at that container.
/// Reused across the "api" collection so the container starts once.
/// </summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("sra_rms_test")
        .Build();

    private ApiWebAppFactory _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>JSON options matching the API (camelCase + enum tokens).</summary>
    public static readonly JsonSerializerOptions Json = CreateJson();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ApplySchemaAsync(_container.GetConnectionString());
        _factory = new ApiWebAppFactory(_container.GetConnectionString());
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Truncates all business tables — call before each test for isolation.</summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE allocation, project, resource, client, department, location, job_title, skill RESTART IDENTITY CASCADE;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ApplySchemaAsync(string connectionString)
    {
        var sql = await File.ReadAllTextAsync(FindRepoFile("db/migrations/V001__initial_schema.sql"));
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SraRms.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate repo root (SraRms.sln).");
        return Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}

/// <summary>Hosts the API against the test container, with the Dev auth bypass on.</summary>
public class ApiWebAppFactory : WebApplicationFactory<Program>
{
    public ApiWebAppFactory(string connectionString)
    {
        // Environment variables are layered above appsettings.* by the default
        // host config, so this reliably points the single DbContext registration
        // at the test container (config-source overrides lose to appsettings).
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development + Auth:Mode=Dev (appsettings.Development.json) => all-roles user.
        builder.UseEnvironment("Development");
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
