using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;

namespace SraRms.Api.Controllers;

[Route("v1/reference")]
public class ReferenceDataController(AppDbContext db) : BaseApiController
{
    // GET /reference/{collection}
    [HttpGet("{collection}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<IEnumerable<ReferenceItemDto>>> List(string collection, CancellationToken ct)
    {
        // resourceStatuses is enum-backed and read-only.
        if (collection == "resourceStatuses")
            return Ok(Enum.GetValues<ResourceStatus>()
                .Select(s => new ReferenceItemDto(Guid.Empty, JsonToken(s), true)));

        var entity = ReferenceCollections.FromPath(collection);
        if (entity is null) return NotFoundProblem($"Unknown reference collection '{collection}'.");

        var items = await db.Set<ReferenceItem>(entity).AsNoTracking()
            .OrderBy(r => r.Value).ToListAsync(ct);
        return Ok(items.Select(r => r.ToDto()));
    }

    // POST /reference/{collection}
    [HttpPost("{collection}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ReferenceItemDto>> Create(string collection, [FromBody] ReferenceItemCreate body, CancellationToken ct)
    {
        if (collection == "resourceStatuses")
            return BadRequestProblem("resourceStatuses is a fixed enumeration and cannot be extended.");

        var entity = ReferenceCollections.FromPath(collection);
        if (entity is null) return NotFoundProblem($"Unknown reference collection '{collection}'.");

        var set = db.Set<ReferenceItem>(entity);
        if (await set.AnyAsync(r => r.Value.ToLower() == body.Value.ToLower(), ct))
            return ConflictProblem($"'{body.Value}' already exists in {collection}.");

        var item = new ReferenceItem { Value = body.Value, Active = true };
        set.Add(item);
        await db.SaveChangesAsync(ct);
        return Created($"/v1/reference/{collection}/{item.Id}", item.ToDto());
    }

    // camelCase token matching the OpenAPI ResourceStatus enum.
    private static string JsonToken(ResourceStatus s) => s switch
    {
        ResourceStatus.Active => "active",
        ResourceStatus.Inactive => "inactive",
        ResourceStatus.OnLeave => "onLeave",
        _ => s.ToString(),
    };
}
