using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;

namespace SraRms.Api.Controllers;

[Route("v1/clients")]
public class ClientsController(AppDbContext db) : BaseApiController
{
    // GET /clients
    [HttpGet]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<ClientDto>>> List([FromQuery] ListQuery query, CancellationToken ct)
    {
        var q = db.Clients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
            q = q.Where(c => EF.Functions.ILike(c.Name, $"%{query.Q}%"));

        var sort = query.ParseSort();
        q = sort?.Field switch
        {
            "name" => sort.Value.Desc ? q.OrderByDescending(c => c.Name) : q.OrderBy(c => c.Name),
            "createdAt" => sort.Value.Desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            _ => q.OrderBy(c => c.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.PageSize)
            .Select(c => new { Client = c, Count = c.Projects.Count() })
            .ToListAsync(ct);

        var dtos = items.Select(x => x.Client.ToDto(x.Count)).ToList();
        return Ok(Page<ClientDto>.Create(dtos, query.Page, query.PageSize, total));
    }

    // POST /clients
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ClientDto>> Create([FromBody] ClientCreate body, CancellationToken ct)
    {
        if (await db.Clients.AnyAsync(c => c.Name.ToLower() == body.Name.ToLower(), ct))
            return ConflictProblem($"A client named '{body.Name}' already exists.");

        var client = new Client { Name = body.Name };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        var dto = client.ToDto(0);
        return Created($"/v1/clients/{client.Id}", dto);
    }

    // GET /clients/{clientId}
    [HttpGet("{clientId:guid}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<ClientDetailDto>> Get(Guid clientId, CancellationToken ct)
    {
        // NB: do not Include Projects->Client — it cycles back to this client.
        var client = await db.Clients.AsNoTracking()
            .Include(c => c.Projects).ThenInclude(p => p.Allocations).ThenInclude(a => a.Resource)
            .FirstOrDefaultAsync(c => c.Id == clientId, ct);

        if (client is null) return NotFoundProblem($"Client {clientId} not found.");

        // Aggregated team: distinct resources allocated across the client's projects.
        var team = client.Projects
            .SelectMany(p => p.Allocations)
            .Select(a => a.Resource)
            .Where(r => r is not null)
            .DistinctBy(r => r.Id)
            .Select(r => r.ToSummary())
            .ToList();

        var detail = new ClientDetailDto
        {
            Id = client.Id,
            Name = client.Name,
            ProjectCount = client.Projects.Count,
            CreatedAt = client.CreatedAt,
            UpdatedAt = client.UpdatedAt,
            Projects = client.Projects.Select(p => p.ToDto()).ToList(),
            Team = team,
        };
        return Ok(detail);
    }

    // PUT /clients/{clientId}
    [HttpPut("{clientId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ClientDto>> Update(Guid clientId, [FromBody] ClientUpdate body, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null) return NotFoundProblem($"Client {clientId} not found.");

        if (await db.Clients.AnyAsync(c => c.Id != clientId && c.Name.ToLower() == body.Name.ToLower(), ct))
            return ConflictProblem($"A client named '{body.Name}' already exists.");

        client.Name = body.Name;
        await db.SaveChangesAsync(ct);

        var count = await db.Projects.CountAsync(p => p.ClientId == clientId, ct);
        return Ok(client.ToDto(count));
    }

    // DELETE /clients/{clientId}?cascade=
    [HttpDelete("{clientId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid clientId, [FromQuery] bool cascade, CancellationToken ct)
    {
        var client = await db.Clients.Include(c => c.Projects).ThenInclude(p => p.Allocations)
            .FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null) return NotFoundProblem($"Client {clientId} not found.");

        if (client.Projects.Count > 0 && !cascade)
            return ConflictProblem($"Client has {client.Projects.Count} project(s). Pass cascade=true to delete them too.");

        if (cascade)
        {
            foreach (var p in client.Projects)
                db.Allocations.RemoveRange(p.Allocations);
            db.Projects.RemoveRange(client.Projects);
        }

        db.Clients.Remove(client);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /clients/{clientId}/projects
    [HttpGet("{clientId:guid}/projects")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<ProjectDto>>> ListProjects(
        Guid clientId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct))
            return NotFoundProblem($"Client {clientId} not found.");

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 25 : pageSize;

        var q = db.Projects.AsNoTracking().Include(p => p.Client).Where(p => p.ClientId == clientId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var dtos = items.Select(p => p.ToDto()).ToList();
        return Ok(Page<ProjectDto>.Create(dtos, page, pageSize, total));
    }
}
