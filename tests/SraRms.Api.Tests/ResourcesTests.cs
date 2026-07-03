using System.Net;
using System.Net.Http.Json;
using SraRms.Api.Contracts;

namespace SraRms.Api.Tests;

public class ResourcesTests(ApiFixture fx) : IntegrationTestBase(fx)
{
    // Minimal valid-signature payloads (signature check only inspects the header).
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private async Task<HttpResponseMessage> PutImage(Guid resourceId, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return await Client.PutAsync($"/v1/resources/{resourceId}/image", content);
    }

    [Fact]
    public async Task Image_upload_then_get_roundtrip()
    {
        var resource = await CreateResource("pic@sra.com.au", 38);

        var put = await PutImage(resource.Id, PngBytes, "image/png");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = await ReadAs<ResourceDto>(put);
        Assert.Equal($"/v1/resources/{resource.Id}/image", dto.ImageUrl);

        var get = await Client.GetAsync($"/v1/resources/{resource.Id}/image");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Image_replace_changes_served_content_type()
    {
        var resource = await CreateResource("swap@sra.com.au", 38);
        (await PutImage(resource.Id, PngBytes, "image/png")).EnsureSuccessStatusCode();
        (await PutImage(resource.Id, JpegBytes, "image/jpeg")).EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"/v1/resources/{resource.Id}/image");
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(JpegBytes, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Image_with_mismatched_signature_returns_400()
    {
        var resource = await CreateResource("fake@sra.com.au", 38);
        var res = await PutImage(resource.Id, [0x4D, 0x5A, 1, 2, 3], "image/png"); // MZ header, not PNG
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Image_get_returns_404_when_none_uploaded()
    {
        var resource = await CreateResource("noimg@sra.com.au", 38);
        var get = await Client.GetAsync($"/v1/resources/{resource.Id}/image");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Image_is_not_served_from_static_uploads_path()
    {
        var resource = await CreateResource("static@sra.com.au", 38);
        (await PutImage(resource.Id, PngBytes, "image/png")).EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"/uploads/resources/{resource.Id}.png");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

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
