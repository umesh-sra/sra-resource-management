using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Controllers;

[Route("v1/projects")]
public class ProjectsController(AppDbContext db, AllocationService allocations) : BaseApiController
{
    // GET /projects
    [HttpGet]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<ProjectDto>>> List(
        [FromQuery] ListQuery query,
        [FromQuery] Guid? clientId,
        [FromQuery] bool? billable,
        [FromQuery] ProjectStatus? status,
        [FromQuery] DateOnly? startsAfter,
        [FromQuery] DateOnly? endsBefore,
        CancellationToken ct)
    {
        var q = db.Projects.AsNoTracking().Include(p => p.Client).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
            q = q.Where(p => EF.Functions.ILike(p.Name, $"%{query.Q}%") || EF.Functions.ILike(p.Code, $"%{query.Q}%"));
        if (clientId is not null) q = q.Where(p => p.ClientId == clientId);
        if (billable is not null) q = q.Where(p => p.Billable == billable);
        if (status is not null) q = q.Where(p => p.Status == status);
        if (startsAfter is not null) q = q.Where(p => p.StartDate >= startsAfter);
        if (endsBefore is not null) q = q.Where(p => p.EndDate <= endsBefore);

        var sort = query.ParseSort();
        q = sort?.Field switch
        {
            "name" => sort.Value.Desc ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
            "code" => sort.Value.Desc ? q.OrderByDescending(p => p.Code) : q.OrderBy(p => p.Code),
            "startDate" => sort.Value.Desc ? q.OrderByDescending(p => p.StartDate) : q.OrderBy(p => p.StartDate),
            "endDate" => sort.Value.Desc ? q.OrderByDescending(p => p.EndDate) : q.OrderBy(p => p.EndDate),
            "status" => sort.Value.Desc ? q.OrderByDescending(p => p.Status) : q.OrderBy(p => p.Status),
            _ => q.OrderBy(p => p.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var dtos = items.Select(p => p.ToDto()).ToList();
        return Ok(Page<ProjectDto>.Create(dtos, query.Page, query.PageSize, total));
    }

    // POST /projects
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] ProjectCreate body, CancellationToken ct)
    {
        if (body.EndDate < body.StartDate)
            return BadRequestProblem("End date must be on or after start date.");
        if (!await db.Clients.AnyAsync(c => c.Id == body.ClientId, ct))
            return BadRequestProblem($"Client {body.ClientId} does not exist.");
        if (await db.Projects.AnyAsync(p => p.Code == body.Code, ct))
            return ConflictProblem($"Project code '{body.Code}' is already in use.");

        var project = new Project
        {
            ClientId = body.ClientId,
            Name = body.Name,
            Code = body.Code,
            StartDate = body.StartDate,
            EndDate = body.EndDate,
            Budget = body.Budget,
            Remaining = body.Remaining ?? body.Budget,
            Billable = body.Billable,
            Status = body.Status,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        await db.Entry(project).Reference(p => p.Client).LoadAsync(ct);
        return Created($"/v1/projects/{project.Id}", project.ToDto());
    }

    // GET /projects/{projectId}
    [HttpGet("{projectId:guid}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<ProjectDetailDto>> Get(Guid projectId, CancellationToken ct)
    {
        // NB: do not Include Allocations->Project — it cycles back to this project.
        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Allocations).ThenInclude(a => a.Resource)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");

        var dto = project.ToDto();
        var detail = new ProjectDetailDto
        {
            Id = dto.Id,
            ClientId = dto.ClientId,
            ClientName = dto.ClientName,
            Name = dto.Name,
            Code = dto.Code,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Budget = dto.Budget,
            Remaining = dto.Remaining,
            Billable = dto.Billable,
            Status = dto.Status,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Allocations = project.Allocations.Select(a => a.ToDto()).ToList(),
        };
        return Ok(detail);
    }

    // PUT /projects/{projectId}
    [HttpPut("{projectId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectDto>> Update(Guid projectId, [FromBody] ProjectUpdate body, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Client).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");
        if (body.EndDate < body.StartDate)
            return BadRequestProblem("End date must be on or after start date.");
        if (await db.Projects.AnyAsync(p => p.Id != projectId && p.Code == body.Code, ct))
            return ConflictProblem($"Project code '{body.Code}' is already in use.");
        if (body.ClientId is not null && body.ClientId != project.ClientId
            && !await db.Clients.AnyAsync(c => c.Id == body.ClientId, ct))
            return BadRequestProblem($"Client {body.ClientId} does not exist.");

        project.Name = body.Name;
        project.Code = body.Code;
        if (body.ClientId is not null) project.ClientId = body.ClientId.Value;
        project.StartDate = body.StartDate;
        project.EndDate = body.EndDate;
        project.Budget = body.Budget;
        project.Remaining = body.Remaining;
        project.Billable = body.Billable;
        project.Status = body.Status;
        await db.SaveChangesAsync(ct);

        await db.Entry(project).Reference(p => p.Client).LoadAsync(ct);
        return Ok(project.ToDto());
    }

    // DELETE /projects/{projectId}?cascade=
    [HttpDelete("{projectId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid projectId, [FromQuery] bool cascade, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");

        if (project.Allocations.Count > 0 && !cascade)
            return ConflictProblem($"Project has {project.Allocations.Count} allocation(s). Pass cascade=true to delete them too.");

        if (cascade) db.Allocations.RemoveRange(project.Allocations);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /projects/{projectId}/allocations
    [HttpGet("{projectId:guid}/allocations")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<AllocationDto>>> ListAllocations(
        Guid projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            return NotFoundProblem($"Project {projectId} not found.");

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 25 : pageSize;

        var q = db.Allocations.AsNoTracking()
            .Include(a => a.Project).Include(a => a.Resource)
            .Where(a => a.ProjectId == projectId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(a => a.StartDate).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = items.Select(a => a.ToDto()).ToList();
        return Ok(Page<AllocationDto>.Create(dtos, page, pageSize, total));
    }

    // POST /projects/{projectId}/allocations
    [HttpPost("{projectId:guid}/allocations")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<AllocationDto>> CreateAllocation(
        Guid projectId, [FromBody] AllocationCreate body, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            return NotFoundProblem($"Project {projectId} not found.");
        if (!await db.Resources.AnyAsync(r => r.Id == body.ResourceId, ct))
            return BadRequestProblem($"Resource {body.ResourceId} does not exist.");

        var windowError = await allocations.ValidateWindowAsync(projectId, body.StartDate, body.EndDate, ct);
        if (windowError is not null) return BadRequestProblem(windowError);

        var billable = body.Billable ?? await db.Projects.Where(p => p.Id == projectId).Select(p => p.Billable).FirstAsync(ct);

        var allocation = new Allocation
        {
            ProjectId = projectId,
            ResourceId = body.ResourceId,
            StartDate = body.StartDate,
            EndDate = body.EndDate,
            Effort = body.Effort,
            EffortUnit = body.EffortUnit,
            RoleOnProject = body.RoleOnProject,
            Billable = billable,
        };
        db.Allocations.Add(allocation);
        await db.SaveChangesAsync(ct);

        var warnings = await allocations.ComputeWarningsAsync(
            body.ResourceId, body.StartDate, body.EndDate, body.Effort, body.EffortUnit, allocation.Id, ct);

        await db.Entry(allocation).Reference(a => a.Project).LoadAsync(ct);
        await db.Entry(allocation).Reference(a => a.Resource).LoadAsync(ct);
        return Created($"/v1/allocations/{allocation.Id}", allocation.ToDto(warnings));
    }
}
