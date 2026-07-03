using Microsoft.EntityFrameworkCore;
using SraRms.Api.Data;

namespace SraRms.Api.Services;

/// <summary>
/// Allocation validation and capacity maths shared by the allocation, dashboard,
/// and report endpoints. Encapsulates the over-allocation rule (FR-ALL-6): effort
/// is converted to a weekly-hours figure and compared against a resource's
/// availability over overlapping date windows.
/// </summary>
public class AllocationService(AppDbContext db)
{
    /// <summary>Weekly hours an allocation consumes, given the resource's availability.</summary>
    public static decimal WeeklyHours(decimal effort, EffortUnit unit, decimal availability) =>
        unit == EffortUnit.HoursPerWeek ? effort : effort / 100m * availability;

    public static bool Overlaps(DateOnly s1, DateOnly e1, DateOnly s2, DateOnly e2) =>
        s1 <= e2 && s2 <= e1;

    /// <summary>
    /// Validates an allocation's window against its project. Returns an error
    /// message if the dates fall outside the project window, otherwise null
    /// (FR-ALL-5). End-before-start is caught by model validation / DB check.
    /// </summary>
    public async Task<string?> ValidateWindowAsync(Guid projectId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.StartDate, x.EndDate })
            .FirstOrDefaultAsync(ct);

        if (p is null) return "Project not found.";
        if (end < start) return "Allocation end date must be on or after its start date.";
        if (start < p.StartDate || end > p.EndDate)
            return $"Allocation window {start:yyyy-MM-dd}..{end:yyyy-MM-dd} falls outside the project window {p.StartDate:yyyy-MM-dd}..{p.EndDate:yyyy-MM-dd}.";
        return null;
    }

    /// <summary>
    /// Non-blocking warnings for a prospective allocation — currently
    /// over-allocation when the resource's overlapping weekly hours would exceed
    /// their availability (FR-ALL-6). Pass the id of the allocation being edited
    /// to exclude it from the existing total.
    /// </summary>
    public async Task<List<string>> ComputeWarningsAsync(
        Guid resourceId, DateOnly start, DateOnly end, decimal effort, EffortUnit unit,
        Guid? excludeAllocationId, CancellationToken ct)
    {
        var warnings = new List<string>();

        var resource = await db.Resources.AsNoTracking()
            .Where(r => r.Id == resourceId)
            .Select(r => new { r.AvailabilityHoursPerWeek })
            .FirstOrDefaultAsync(ct);
        if (resource is null) return warnings;

        var availability = resource.AvailabilityHoursPerWeek;

        var overlapping = await db.Allocations.AsNoTracking()
            .Where(a => a.ResourceId == resourceId
                        && (excludeAllocationId == null || a.Id != excludeAllocationId)
                        && a.StartDate <= end && start <= a.EndDate)
            .Select(a => new { a.Effort, a.EffortUnit })
            .ToListAsync(ct);

        var existing = overlapping.Sum(a => WeeklyHours(a.Effort, a.EffortUnit, availability));
        var prospective = WeeklyHours(effort, unit, availability);
        var total = existing + prospective;

        if (availability > 0 && total > availability)
            warnings.Add(
                $"Over-allocation: {total:0.##}h/week allocated against {availability:0.##}h availability for overlapping dates.");

        return warnings;
    }
}
