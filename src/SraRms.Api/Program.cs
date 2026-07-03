using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using SraRms.Api.Auth;
using SraRms.Api.Data;
using SraRms.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Database: Npgsql data source with CLR<->PostgreSQL enum mappings, EF Core
// with snake_case naming to match db/migrations/V001.
// ---------------------------------------------------------------------------
var connString = builder.Configuration.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// CLR<->PostgreSQL enum mapping + snake_case naming (see DbSetup).
builder.Services.AddDbContext<AppDbContext>(o => o.ConfigureNpgsql(connString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AllocationService>();

// ---------------------------------------------------------------------------
// MVC + JSON: enums serialise as the OpenAPI camelCase tokens.
// ---------------------------------------------------------------------------
builder.Services.AddControllers(o =>
    {
        // Concurrent duplicate inserts race past the controllers' AnyAsync
        // checks; map the resulting Postgres 23505 to the contract's 409.
        o.Filters.Add<SraRms.Api.Filters.UniqueViolationExceptionFilter>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails(); // RFC 9457 problem+json for errors

// ---------------------------------------------------------------------------
// Authentication & authorization.
//   Production: Microsoft Entra ID (JWT bearer) via Microsoft.Identity.Web.
//   Development + Auth:Mode=Dev: synthetic all-roles user (DevAuthHandler).
// ---------------------------------------------------------------------------
var devAuth = builder.Environment.IsDevelopment()
              && builder.Configuration["Auth:Mode"] == "Dev";

if (devAuth)
{
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Administrator))
    .AddPolicy(Policies.Read, p => p.RequireRole(Roles.Administrator, Roles.General))
    .AddPolicy(Policies.Report, p => p.RequireRole(Roles.Report));

// ---------------------------------------------------------------------------
// CORS for the Vue SPA.
// ---------------------------------------------------------------------------
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// RFC 9457 problem+json for unhandled exceptions (500) and bodyless status
// codes produced by middleware (e.g. 401/403 from authn/authz), per the
// error contract in docs/openapi.yaml.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // serves uploaded resource images under wwwroot/uploads
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so the integration test project can use WebApplicationFactory<Program>.
public partial class Program { }
