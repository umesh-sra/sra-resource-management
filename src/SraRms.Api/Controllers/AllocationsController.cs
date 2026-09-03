using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Controllers;

[Route("v1/allocations")]
public class AllocationsController(AppDbContext db, AllocationService allocations) : BaseApiController
{
    // GET /allocations
    [HttpGet]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<AllocationDto>>> List(
        [FromQuery] ListQuery query,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? resourceId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var q = db.Allocations.AsNoTracking()
            .Include(a => a.Project).Include(a => a.Resource).Include(a => a.Booker).AsQueryable();

        if (projectId is not null) q = q.Where(a => a.ProjectId == projectId);
        if (resourceId is not null) q = q.Where(a => a.ResourceId == resourceId);
        if (from is not null) q = q.Where(a => a.EndDate >= from);
        if (to is not null) q = q.Where(a => a.StartDate <= to);

        var sort = query.ParseSort();
        q = sort?.Field switch
        {
            "startDate" => sort.Value.Desc ? q.OrderByDescending(a => a.StartDate) : q.OrderBy(a => a.StartDate),
            "endDate" => sort.Value.Desc ? q.OrderByDescending(a => a.EndDate) : q.OrderBy(a => a.EndDate),
            "effort" => sort.Value.Desc ? q.OrderByDescending(a => a.Effort) : q.OrderBy(a => a.Effort),
            _ => q.OrderBy(a => a.StartDate),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var dtos = items.Select(a => a.ToDto()).ToList();
        return Ok(Page<AllocationDto>.Create(dtos, query.Page, query.PageSize, total));
    }

    // POST /allocations
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<AllocationDto>> Create([FromBody] AllocationCreateFull body, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == body.ProjectId, ct))
            return BadRequestProblem($"Project {body.ProjectId} does not exist.");
        if (!await db.Resources.AnyAsync(r => r.Id == body.ResourceId, ct))
            return BadRequestProblem($"Resource {body.ResourceId} does not exist.");
        if (body.BookerId is { } bookerId && !await db.Resources.AnyAsync(r => r.Id == bookerId, ct))
            return BadRequestProblem($"Booker {bookerId} does not exist.");

        var windowError = await allocations.ValidateWindowAsync(body.ProjectId, body.StartDate, body.EndDate, ct);
        if (windowError is not null) return BadRequestProblem(windowError);

        var billable = body.Billable ?? await db.Projects.Where(p => p.Id == body.ProjectId).Select(p => p.Billable).FirstAsync(ct);

        var allocation = new Allocation
        {
            ProjectId = body.ProjectId,
            ResourceId = body.ResourceId,
            StartDate = body.StartDate,
            EndDate = body.EndDate,
            Effort = body.Effort,
            EffortUnit = body.EffortUnit,
            RoleOnProject = body.RoleOnProject,
            Billable = billable,
            HourlyRate = body.HourlyRate,
            Details = body.Details,
            BookerId = body.BookerId,
        };
        db.Allocations.Add(allocation);
        await db.SaveChangesAsync(ct);

        var warnings = await allocations.ComputeWarningsAsync(
            body.ResourceId, body.StartDate, body.EndDate, body.Effort, body.EffortUnit, allocation.Id, ct);

        await db.Entry(allocation).Reference(a => a.Project).LoadAsync(ct);
        await db.Entry(allocation).Reference(a => a.Resource).LoadAsync(ct);
        await db.Entry(allocation).Reference(a => a.Booker).LoadAsync(ct);
        return Created($"/v1/allocations/{allocation.Id}", allocation.ToDto(warnings));
    }

    // GET /allocations/{allocationId}
    [HttpGet("{allocationId:guid}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<AllocationDto>> Get(Guid allocationId, CancellationToken ct)
    {
        var allocation = await db.Allocations.AsNoTracking()
            .Include(a => a.Project).Include(a => a.Resource).Include(a => a.Booker)
            .FirstOrDefaultAsync(a => a.Id == allocationId, ct);
        if (allocation is null) return NotFoundProblem($"Allocation {allocationId} not found.");
        return Ok(allocation.ToDto());
    }

    // PUT /allocations/{allocationId}
    [HttpPut("{allocationId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<AllocationDto>> Update(Guid allocationId, [FromBody] AllocationUpdate body, CancellationToken ct)
    {
        var allocation = await db.Allocations
            .Include(a => a.Project).Include(a => a.Resource).Include(a => a.Booker)
            .FirstOrDefaultAsync(a => a.Id == allocationId, ct);
        if (allocation is null) return NotFoundProblem($"Allocation {allocationId} not found.");

        if (body.BookerId is { } bookerId && !await db.Resources.AnyAsync(r => r.Id == bookerId, ct))
            return BadRequestProblem($"Booker {bookerId} does not exist.");

        var windowError = await allocations.ValidateWindowAsync(allocation.ProjectId, body.StartDate, body.EndDate, ct);
        if (windowError is not null) return BadRequestProblem(windowError);

        allocation.StartDate = body.StartDate;
        allocation.EndDate = body.EndDate;
        allocation.Effort = body.Effort;
        allocation.EffortUnit = body.EffortUnit;
        allocation.RoleOnProject = body.RoleOnProject;
        if (body.Billable is not null) allocation.Billable = body.Billable.Value;
        allocation.HourlyRate = body.HourlyRate;
        allocation.Details = body.Details;
        allocation.BookerId = body.BookerId;
        await db.SaveChangesAsync(ct);
        // The booker may have changed; reload it so BookerName reflects the write.
        await db.Entry(allocation).Reference(a => a.Booker).LoadAsync(ct);

        var warnings = await allocations.ComputeWarningsAsync(
            allocation.ResourceId, body.StartDate, body.EndDate, body.Effort, body.EffortUnit, allocation.Id, ct);
        return Ok(allocation.ToDto(warnings));
    }

    // DELETE /allocations/{allocationId}
    [HttpDelete("{allocationId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid allocationId, CancellationToken ct)
    {
        var allocation = await db.Allocations.FirstOrDefaultAsync(a => a.Id == allocationId, ct);
        if (allocation is null) return NotFoundProblem($"Allocation {allocationId} not found.");
        db.Allocations.Remove(allocation);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
