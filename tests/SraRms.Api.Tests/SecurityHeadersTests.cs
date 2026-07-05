using System.Net;

namespace SraRms.Api.Tests;

/// <summary>
/// NFR-SEC-1 / NFR-SEC-4: every API response must carry the OWASP secure
/// headers, including problem-details error responses produced by middleware.
/// </summary>
public class SecurityHeadersTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    private static void AssertSecurityHeaders(HttpResponseMessage res)
    {
        Assert.Equal("nosniff", Assert.Single(res.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(res.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(res.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'",
            Assert.Single(res.Headers.GetValues("Content-Security-Policy")));
        Assert.True(res.Headers.CacheControl?.NoStore, "Cache-Control must default to no-store");
    }

    [Fact]
    public async Task Success_responses_carry_security_headers()
    {
        var res = await Client.GetAsync("/v1/clients");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        AssertSecurityHeaders(res);
    }

    [Fact]
    public async Task Error_responses_carry_security_headers()
    {
        // 404 problem-details from an unknown resource id
        var res = await Client.GetAsync($"/v1/clients/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        AssertSecurityHeaders(res);
    }

    [Fact]
    public async Task Validation_errors_carry_security_headers()
    {
        var res = await PostJson("/v1/clients", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        AssertSecurityHeaders(res);
    }
}
