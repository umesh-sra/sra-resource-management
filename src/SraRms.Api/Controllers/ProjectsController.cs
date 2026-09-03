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
        var teams = await TeamsForAsync(items.Select(p => p.Id).ToList(), ct);
        var dtos = items
            .Select(p => p.ToDto(teams.TryGetValue(p.Id, out var t) ? t : []))
            .ToList();
        return Ok(Page<ProjectDto>.Create(dtos, query.Page, query.PageSize, total));
    }

    /// <summary>
    /// Distinct allocated people per project, in one round trip for the whole page.
    /// </summary>
    private async Task<Dictionary<Guid, List<ResourceSummaryDto>>> TeamsForAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0) return [];

        var rows = await db.Allocations.AsNoTracking()
            .Where(a => projectIds.Contains(a.ProjectId))
            .Select(a => new
            {
                a.ProjectId,
                a.ResourceId,
                a.Resource!.Name,
                a.Resource.PrimaryJobTitle,
                a.Resource.ImageUrl,
            })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                      .Select(r => new ResourceSummaryDto
                      {
                          Id = r.ResourceId,
                          Name = r.Name,
                          PrimaryJobTitle = r.PrimaryJobTitle,
                          ImageUrl = r.ImageUrl,
                      })
                      .ToList());
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
        if (BudgetProblem(body.BudgetType, body.Budget, body.BudgetHours) is { } budgetError)
            return BadRequestProblem(budgetError);

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
            BudgetType = body.BudgetType,
            BudgetHours = body.BudgetHours,
            RemainingHours = body.RemainingHours ?? body.BudgetHours,
            ActivityTypes = body.ActivityTypes,
            Details = body.Details,
            Colour = body.Colour,
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
            .Include(p => p.Phases)
            .Include(p => p.Milestones)
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
            BudgetType = dto.BudgetType,
            BudgetHours = dto.BudgetHours,
            RemainingHours = dto.RemainingHours,
            ActivityTypes = dto.ActivityTypes,
            Details = dto.Details,
            Colour = dto.Colour,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Team = project.Allocations
                .Where(a => a.Resource is not null)
                .Select(a => a.Resource!)
                .DistinctBy(r => r.Id)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => r.ToSummary())
                .ToList(),
            Allocations = project.Allocations.Select(a => a.ToDto()).ToList(),
            Phases = project.Phases
                .OrderBy(ph => ph.SortOrder).ThenBy(ph => ph.StartDate)
                .Select(ph => ph.ToDto()).ToList(),
            Milestones = project.Milestones
                .OrderBy(m => m.DueDate)
                .Select(m => m.ToDto()).ToList(),
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
        if (BudgetProblem(body.BudgetType, body.Budget, body.BudgetHours) is { } budgetError)
            return BadRequestProblem(budgetError);
        if (body.ClientId is not null && body.ClientId != project.ClientId
            && !await db.Clients.AnyAsync(c => c.Id == body.ClientId, ct))
            return BadRequestProblem($"Client {body.ClientId} does not exist.");

        // SRS §3.5 / FR-ALL-5: narrowing the project window must not strand
        // existing allocations outside it. Reject with 409 so the caller adjusts
        // or deletes the offending allocations first (or widens the dates).
        if (body.StartDate > project.StartDate || body.EndDate < project.EndDate)
        {
            var stranded = await db.Allocations.AsNoTracking()
                .Where(a => a.ProjectId == projectId
                            && (a.StartDate < body.StartDate || a.EndDate > body.EndDate))
                .OrderBy(a => a.StartDate)
                .Select(a => new { a.Id, a.StartDate, a.EndDate, ResourceName = a.Resource!.Name })
                .ToListAsync(ct);
            if (stranded.Count > 0)
            {
                var sample = string.Join("; ", stranded.Take(5)
                    .Select(a => $"{a.ResourceName} {a.StartDate:yyyy-MM-dd}..{a.EndDate:yyyy-MM-dd} ({a.Id})"));
                var more = stranded.Count > 5 ? $"; and {stranded.Count - 5} more" : "";
                return ConflictProblem(
                    $"{stranded.Count} allocation(s) fall outside the new project window "
                    + $"{body.StartDate:yyyy-MM-dd}..{body.EndDate:yyyy-MM-dd}: {sample}{more}. "
                    + "Adjust or delete these allocations first, or widen the project dates.");
            }
        }

        project.Name = body.Name;
        project.Code = body.Code;
        if (body.ClientId is not null) project.ClientId = body.ClientId.Value;
        project.StartDate = body.StartDate;
        project.EndDate = body.EndDate;
        project.Budget = body.Budget;
        project.Remaining = body.Remaining;
        project.Billable = body.Billable;
        project.Status = body.Status;
        project.BudgetType = body.BudgetType;
        project.BudgetHours = body.BudgetHours;
        project.RemainingHours = body.RemainingHours;
        project.ActivityTypes = body.ActivityTypes;
        project.Details = body.Details;
        project.Colour = body.Colour;
        await db.SaveChangesAsync(ct);

        await db.Entry(project).Reference(p => p.Client).LoadAsync(ct);
        return Ok(project.ToDto());
    }

    // DELETE /projects/{projectId}?cascade=
    [HttpDelete("{projectId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid projectId, [FromQuery] bool cascade, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Allocations)
            .Include(p => p.Phases)
            .Include(p => p.Milestones)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");

        // Phases and milestones are FK-RESTRICTed too (V002), so they must be
        // counted in the 409 and cleared on cascade or the delete fails at the DB.
        var dependents = project.Allocations.Count + project.Phases.Count + project.Milestones.Count;
        if (dependents > 0 && !cascade)
            return ConflictProblem(
                $"Project has {project.Allocations.Count} allocation(s), {project.Phases.Count} phase(s) "
                + $"and {project.Milestones.Count} milestone(s). Pass cascade=true to delete them too.");

        if (cascade)
        {
            db.Allocations.RemoveRange(project.Allocations);
            db.ProjectPhases.RemoveRange(project.Phases);
            db.ProjectMilestones.RemoveRange(project.Milestones);
        }
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Budget-type validation (V002) -------------------------------------
    // Mirrors ck_project_budget_type in db/migrations/V002 so the caller gets a
    // 400 with a readable message instead of a 500 from the check constraint.
    private static string? BudgetProblem(ProjectBudgetType type, decimal? budget, decimal? budgetHours) => type switch
    {
        ProjectBudgetType.Fee when budget is null => "budgetType 'fee' requires a budget amount.",
        ProjectBudgetType.Hours when budgetHours is null => "budgetType 'hours' requires budgetHours.",
        _ => null,
    };

    // ---- Phases (FR-PHASE-*) ------------------------------------------------

    // GET /projects/{projectId}/phases
    [HttpGet("{projectId:guid}/phases")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectPhaseDto>>> ListPhases(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            return NotFoundProblem($"Project {projectId} not found.");
        var rows = await db.ProjectPhases.AsNoTracking()
            .Where(ph => ph.ProjectId == projectId)
            .OrderBy(ph => ph.SortOrder).ThenBy(ph => ph.StartDate)
            .ToListAsync(ct);
        return Ok(rows.Select(ph => ph.ToDto()).ToList());
    }

    // POST /projects/{projectId}/phases
    [HttpPost("{projectId:guid}/phases")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectPhaseDto>> CreatePhase(
        Guid projectId, [FromBody] ProjectPhaseCreate body, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");
        if (PhaseProblem(body.StartDate, body.EndDate, project) is { } error) return BadRequestProblem(error);

        var phase = new ProjectPhase
        {
            ProjectId = projectId,
            Name = body.Name,
            StartDate = body.StartDate,
            EndDate = body.EndDate,
            Colour = body.Colour,
            SortOrder = body.SortOrder,
        };
        db.ProjectPhases.Add(phase);
        await db.SaveChangesAsync(ct);
        return Created($"/v1/projects/{projectId}/phases/{phase.Id}", phase.ToDto());
    }

    // PUT /projects/{projectId}/phases/{phaseId}
    [HttpPut("{projectId:guid}/phases/{phaseId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectPhaseDto>> UpdatePhase(
        Guid projectId, Guid phaseId, [FromBody] ProjectPhaseUpdate body, CancellationToken ct)
    {
        var phase = await db.ProjectPhases.FirstOrDefaultAsync(ph => ph.Id == phaseId && ph.ProjectId == projectId, ct);
        if (phase is null) return NotFoundProblem($"Phase {phaseId} not found on project {projectId}.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
        if (PhaseProblem(body.StartDate, body.EndDate, project) is { } error) return BadRequestProblem(error);

        phase.Name = body.Name;
        phase.StartDate = body.StartDate;
        phase.EndDate = body.EndDate;
        phase.Colour = body.Colour;
        phase.SortOrder = body.SortOrder;
        await db.SaveChangesAsync(ct);
        return Ok(phase.ToDto());
    }

    // DELETE /projects/{projectId}/phases/{phaseId}
    [HttpDelete("{projectId:guid}/phases/{phaseId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> DeletePhase(Guid projectId, Guid phaseId, CancellationToken ct)
    {
        var phase = await db.ProjectPhases.FirstOrDefaultAsync(ph => ph.Id == phaseId && ph.ProjectId == projectId, ct);
        if (phase is null) return NotFoundProblem($"Phase {phaseId} not found on project {projectId}.");
        db.ProjectPhases.Remove(phase);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // FR-PHASE-4: a phase is a stage *of* the project, so it must sit inside the
    // project window - the same rule allocations obey (FR-ALL-5).
    private static string? PhaseProblem(DateOnly start, DateOnly end, Project project)
    {
        if (end < start) return "End date must be on or after start date.";
        if (start < project.StartDate || end > project.EndDate)
            return $"Phase {start:yyyy-MM-dd}..{end:yyyy-MM-dd} falls outside the project window "
                   + $"{project.StartDate:yyyy-MM-dd}..{project.EndDate:yyyy-MM-dd}.";
        return null;
    }

    // ---- Milestones (FR-MILE-*) ---------------------------------------------

    // GET /projects/{projectId}/milestones
    [HttpGet("{projectId:guid}/milestones")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectMilestoneDto>>> ListMilestones(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            return NotFoundProblem($"Project {projectId} not found.");
        var rows = await db.ProjectMilestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId).OrderBy(m => m.DueDate).ToListAsync(ct);
        return Ok(rows.Select(m => m.ToDto()).ToList());
    }

    // POST /projects/{projectId}/milestones
    [HttpPost("{projectId:guid}/milestones")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectMilestoneDto>> CreateMilestone(
        Guid projectId, [FromBody] ProjectMilestoneCreate body, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFoundProblem($"Project {projectId} not found.");
        if (body.DueDate < project.StartDate || body.DueDate > project.EndDate)
            return BadRequestProblem(
                $"Milestone {body.DueDate:yyyy-MM-dd} falls outside the project window "
                + $"{project.StartDate:yyyy-MM-dd}..{project.EndDate:yyyy-MM-dd}.");

        var milestone = new ProjectMilestone
        {
            ProjectId = projectId,
            Name = body.Name,
            DueDate = body.DueDate,
            Status = body.Status,
            Note = body.Note,
        };
        db.ProjectMilestones.Add(milestone);
        await db.SaveChangesAsync(ct);
        return Created($"/v1/projects/{projectId}/milestones/{milestone.Id}", milestone.ToDto());
    }

    // PUT /projects/{projectId}/milestones/{milestoneId}
    [HttpPut("{projectId:guid}/milestones/{milestoneId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ProjectMilestoneDto>> UpdateMilestone(
        Guid projectId, Guid milestoneId, [FromBody] ProjectMilestoneUpdate body, CancellationToken ct)
    {
        var milestone = await db.ProjectMilestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct);
        if (milestone is null) return NotFoundProblem($"Milestone {milestoneId} not found on project {projectId}.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
        if (body.DueDate < project.StartDate || body.DueDate > project.EndDate)
            return BadRequestProblem(
                $"Milestone {body.DueDate:yyyy-MM-dd} falls outside the project window "
                + $"{project.StartDate:yyyy-MM-dd}..{project.EndDate:yyyy-MM-dd}.");

        milestone.Name = body.Name;
        milestone.DueDate = body.DueDate;
        milestone.Status = body.Status;
        milestone.Note = body.Note;
        await db.SaveChangesAsync(ct);
        return Ok(milestone.ToDto());
    }

    // DELETE /projects/{projectId}/milestones/{milestoneId}
    [HttpDelete("{projectId:guid}/milestones/{milestoneId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> DeleteMilestone(Guid projectId, Guid milestoneId, CancellationToken ct)
    {
        var milestone = await db.ProjectMilestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct);
        if (milestone is null) return NotFoundProblem($"Milestone {milestoneId} not found on project {projectId}.");
        db.ProjectMilestones.Remove(milestone);
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
            .Include(a => a.Project).Include(a => a.Resource).Include(a => a.Booker)
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
        if (body.BookerId is { } bookerId && !await db.Resources.AnyAsync(r => r.Id == bookerId, ct))
            return BadRequestProblem($"Booker {bookerId} does not exist.");

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
}
