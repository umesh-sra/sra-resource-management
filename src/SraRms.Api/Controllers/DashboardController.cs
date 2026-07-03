using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Controllers;

[Route("v1/dashboard")]
public class DashboardController(AppDbContext db) : BaseApiController
{
    // Heuristic thresholds for the headline metrics (documented in Requirements §4.6).
    private const decimal UnderAllocatedBelow = 0.5m; // utilisation under 50% = under-allocated
    private const decimal BudgetAtRiskConsumed = 0.9m; // >=90% of budget consumed = at risk

    // GET /dashboard/summary
    [HttpGet("summary")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizonFrom = from ?? today;
        var horizonTo = to ?? today.AddDays(30);

        var resources = await db.Resources.AsNoTracking()
            .Include(r => r.Allocations).ToListAsync(ct);

        // Per-resource current utilisation (allocations overlapping today).
        var utilisations = new List<decimal>();
        int over = 0, under = 0;
        foreach (var r in resources)
        {
            if (r.AvailabilityHoursPerWeek <= 0) continue;
            var hours = r.Allocations
                .Where(a => a.StartDate <= today && today <= a.EndDate)
                .Sum(a => AllocationService.WeeklyHours(a.Effort, a.EffortUnit, r.AvailabilityHoursPerWeek));
            var util = hours / r.AvailabilityHoursPerWeek;
            utilisations.Add(util);
            if (util > 1m) over++;
            else if (util < UnderAllocatedBelow) under++;
        }

        var activeProjects = await db.Projects.CountAsync(p => p.Status == ProjectStatus.Active, ct);

        var budgetAtRisk = await db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Active && p.Budget != null && p.Budget > 0 && p.Remaining != null)
            .Where(p => (p.Budget!.Value - p.Remaining!.Value) / p.Budget.Value >= BudgetAtRiskConsumed)
            .SumAsync(p => p.Budget ?? 0, ct);

        var upcomingStarts = await db.Projects.AsNoTracking().Include(p => p.Client)
            .Where(p => p.StartDate >= horizonFrom && p.StartDate <= horizonTo)
            .OrderBy(p => p.StartDate).ToListAsync(ct);

        var upcomingRollOffs = await db.Allocations.AsNoTracking()
            .Include(a => a.Project).Include(a => a.Resource)
            .Where(a => a.EndDate >= horizonFrom && a.EndDate <= horizonTo)
            .OrderBy(a => a.EndDate).ToListAsync(ct);

        var dto = new DashboardSummaryDto
        {
            ActiveProjects = activeProjects,
            TotalResources = resources.Count,
            AverageUtilisation = utilisations.Count > 0 ? (double)utilisations.Average() : 0,
            OverAllocatedResources = over,
            UnderAllocatedResources = under,
            BudgetAtRisk = (double)budgetAtRisk,
            UpcomingProjectStarts = upcomingStarts.Select(p => p.ToDto()).ToList(),
            UpcomingRollOffs = upcomingRollOffs.Select(a => a.ToDto()).ToList(),
        };
        return Ok(dto);
    }

    // GET /dashboard/gantt
    [HttpGet("gantt")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<GanttResponseDto>> Gantt(
        [FromQuery] string view, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? clientId, [FromQuery] string? department, CancellationToken ct)
    {
        if (view is not ("projects" or "resources"))
            return BadRequestProblem("view must be 'projects' or 'resources'.");
        if (to < from) return BadRequestProblem("'to' must be on or after 'from'.");

        var rows = view == "projects"
            ? await ProjectRows(from, to, clientId, ct)
            : await ResourceRows(from, to, department, ct);

        return Ok(new GanttResponseDto { View = view, From = from, To = to, Rows = rows });
    }

    private async Task<List<GanttRowDto>> ProjectRows(DateOnly from, DateOnly to, Guid? clientId, CancellationToken ct)
    {
        var q = db.Projects.AsNoTracking()
            .Include(p => p.Allocations).ThenInclude(a => a.Resource)
            .Where(p => p.StartDate <= to && from <= p.EndDate);
        if (clientId is not null) q = q.Where(p => p.ClientId == clientId);

        var projects = await q.OrderBy(p => p.Name).ToListAsync(ct);
        return projects.Select(p => new GanttRowDto(
            p.Id.ToString(),
            $"{p.Code} — {p.Name}",
            p.Allocations
                .Where(a => a.StartDate <= to && from <= a.EndDate)
                .Select(a => new GanttBarDto
                {
                    RefId = a.Id,
                    Label = a.Resource?.Name,
                    Start = Max(a.StartDate, from),
                    End = Min(a.EndDate, to),
                    Effort = (double)a.Effort,
                    OverAllocated = false,
                }).ToList())).ToList();
    }

    private async Task<List<GanttRowDto>> ResourceRows(DateOnly from, DateOnly to, string? department, CancellationToken ct)
    {
        var q = db.Resources.AsNoTracking()
            .Include(r => r.Allocations).ThenInclude(a => a.Project)
            .Where(r => r.Allocations.Any(a => a.StartDate <= to && from <= a.EndDate));
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(r => r.Department == department);

        var resources = await q.OrderBy(r => r.Name).ToListAsync(ct);
        return resources.Select(r => new GanttRowDto(
            r.Id.ToString(),
            r.Name,
            r.Allocations
                .Where(a => a.StartDate <= to && from <= a.EndDate)
                .Select(a => new GanttBarDto
                {
                    RefId = a.Id,
                    Label = a.Project?.Name,
                    Start = Max(a.StartDate, from),
                    End = Min(a.EndDate, to),
                    Effort = (double)a.Effort,
                    OverAllocated = IsOverAllocatedDuring(r, a),
                }).ToList())).ToList();
    }

    // True if the resource's weekly hours across allocations overlapping this one exceed availability.
    private static bool IsOverAllocatedDuring(Resource r, Allocation a)
    {
        if (r.AvailabilityHoursPerWeek <= 0) return false;
        var total = r.Allocations
            .Where(x => AllocationService.Overlaps(x.StartDate, x.EndDate, a.StartDate, a.EndDate))
            .Sum(x => AllocationService.WeeklyHours(x.Effort, x.EffortUnit, r.AvailabilityHoursPerWeek));
        return total > r.AvailabilityHoursPerWeek;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
