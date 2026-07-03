using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Controllers;

[Route("v1/reports")]
[Authorize(Policy = Policies.Report)]
public class ReportsController(AppDbContext db) : BaseApiController
{
    // GET /reports/utilisation
    [HttpGet("utilisation")]
    public async Task<IActionResult> Utilisation(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] string? department, [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (to < from) return BadRequestProblem("'to' must be on or after 'from'.");

        var q = db.Resources.AsNoTracking().Include(r => r.Allocations).AsQueryable();
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(r => r.Department == department);
        var resources = await q.OrderBy(r => r.Name).ToListAsync(ct);

        var weeks = WeeksBetween(from, to);
        var rows = resources.Select(r =>
        {
            var available = (double)r.AvailabilityHoursPerWeek * weeks;
            var allocated = r.Allocations
                .Where(a => a.StartDate <= to && from <= a.EndDate)
                .Sum(a => (double)AllocationService.WeeklyHours(a.Effort, a.EffortUnit, r.AvailabilityHoursPerWeek)
                          * WeeksOverlap(a.StartDate, a.EndDate, from, to));
            var util = available > 0 ? allocated / available : 0;
            return new UtilisationRow(r.Id, r.Name, r.Department,
                Math.Round(available, 2), Math.Round(allocated, 2), Math.Round(util, 4));
        }).ToList();

        if (IsCsv(format))
            return Csv("utilisation.csv",
                ["resourceId", "resourceName", "department", "availableHours", "allocatedHours", "utilisation"],
                rows.Select(r => new[]
                {
                    r.ResourceId.ToString(), r.ResourceName, r.Department ?? "",
                    Num(r.AvailableHours), Num(r.AllocatedHours), Num(r.Utilisation),
                }));

        return Ok(new UtilisationReportDto(from, to, rows));
    }

    // GET /reports/allocation
    [HttpGet("allocation")]
    public async Task<IActionResult> Allocation(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? clientId, [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (to < from) return BadRequestProblem("'to' must be on or after 'from'.");

        var q = db.Allocations.AsNoTracking()
            .Include(a => a.Project).ThenInclude(p => p.Client)
            .Include(a => a.Resource)
            .Where(a => a.StartDate <= to && from <= a.EndDate);
        if (clientId is not null) q = q.Where(a => a.Project.ClientId == clientId);
        var allocs = await q.OrderBy(a => a.Project.Name).ThenBy(a => a.Resource.Name).ToListAsync(ct);

        var rows = allocs.Select(a =>
        {
            var hours = (double)AllocationService.WeeklyHours(a.Effort, a.EffortUnit, a.Resource.AvailabilityHoursPerWeek)
                        * WeeksOverlap(a.StartDate, a.EndDate, from, to);
            return new AllocationReportRow(a.ProjectId, a.Project.Name, a.Project.Client?.Name,
                a.ResourceId, a.Resource.Name, Math.Round(hours, 2), a.Billable);
        }).ToList();

        if (IsCsv(format))
            return Csv("allocation.csv",
                ["projectId", "projectName", "clientName", "resourceId", "resourceName", "allocatedHours", "billable"],
                rows.Select(r => new[]
                {
                    r.ProjectId.ToString(), r.ProjectName, r.ClientName ?? "",
                    r.ResourceId.ToString(), r.ResourceName, Num(r.AllocatedHours), r.Billable.ToString(),
                }));

        return Ok(new AllocationReportDto(from, to, rows));
    }

    // GET /reports/budget
    [HttpGet("budget")]
    public async Task<IActionResult> Budget(
        [FromQuery] Guid? clientId, [FromQuery] string format = "json", CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking().Include(p => p.Client).AsQueryable();
        if (clientId is not null) q = q.Where(p => p.ClientId == clientId);
        var projects = await q.OrderBy(p => p.Client!.Name).ThenBy(p => p.Name).ToListAsync(ct);

        var rows = projects.Select(p =>
        {
            var budget = (double)(p.Budget ?? 0);
            var remaining = (double)(p.Remaining ?? 0);
            var consumed = budget - remaining;
            var pct = budget > 0 ? consumed / budget : 0;
            return new BudgetReportRow(p.Client?.Name, p.Id, p.Name,
                Math.Round(budget, 2), Math.Round(remaining, 2), Math.Round(consumed, 2), Math.Round(pct, 4));
        }).ToList();

        if (IsCsv(format))
            return Csv("budget.csv",
                ["clientName", "projectId", "projectName", "budget", "remaining", "consumed", "percentConsumed"],
                rows.Select(r => new[]
                {
                    r.ClientName ?? "", r.ProjectId.ToString(), r.ProjectName,
                    Num(r.Budget), Num(r.Remaining), Num(r.Consumed), Num(r.PercentConsumed),
                }));

        return Ok(new BudgetReportDto(rows));
    }

    // ---- helpers ----
    private static bool IsCsv(string format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
    private static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);

    private static double WeeksBetween(DateOnly from, DateOnly to) =>
        (to.DayNumber - from.DayNumber + 1) / 7.0;

    private static double WeeksOverlap(DateOnly start, DateOnly end, DateOnly from, DateOnly to)
    {
        var s = start > from ? start : from;
        var e = end < to ? end : to;
        var days = e.DayNumber - s.DayNumber + 1;
        return days > 0 ? days / 7.0 : 0;
    }

    private FileContentResult Csv(string fileName, string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(Escape)));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    private static string Escape(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
