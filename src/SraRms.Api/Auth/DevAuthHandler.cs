using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SraRms.Api.Auth;

/// <summary>
/// Development-only authentication that signs every request in as a synthetic
/// user holding all three roles, so the API can be run and reviewed locally
/// without a configured Entra tenant. Wired up ONLY when the environment is
/// Development AND Auth:Mode=Dev (see Program.cs); it must never run elsewhere.
/// </summary>
public class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "dev@sra.com.au"),
            new Claim("preferred_username", "dev@sra.com.au"),
            new Claim(ClaimTypes.Role, Roles.Administrator),
            new Claim(ClaimTypes.Role, Roles.General),
            new Claim(ClaimTypes.Role, Roles.Report),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
