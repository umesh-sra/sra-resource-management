using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;

namespace SraRms.Api.Controllers;

/// <summary>
/// Time off (Requirements §3.8, FR-TIMEOFF-*). Leave is recorded per resource and
/// per date range. It never blocks an allocation — consistent with over-allocation
/// being a warning rather than an error (FR-ALL-6) — but it reduces effective
/// capacity in the utilisation report (FR-REP-6) and is drawn on the Schedule.
/// </summary>
[Route("v1/timeoff")]
public class TimeOffController(AppDbContext db) : BaseApiController
{
    // GET /timeoff?resourceId=&from=&to=&type=
    [HttpGet]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<TimeOffDto>>> List(
        [FromQuery] ListQuery query,
        [FromQuery] Guid? resourceId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] TimeOffType? type,
        CancellationToken ct)
    {
        var q = db.TimeOff.AsNoTracking().Include(t => t.Resource).Include(t => t.Booker).AsQueryable();

        if (resourceId is not null) q = q.Where(t => t.ResourceId == resourceId);
        if (type is not null) q = q.Where(t => t.Type == type);
        // Overlap, not containment: leave spanning the window edges still counts.
        if (from is not null) q = q.Where(t => t.EndDate >= from);
        if (to is not null) q = q.Where(t => t.StartDate <= to);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(t => t.StartDate).ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        return Ok(Page<TimeOffDto>.Create(
            rows.Select(t => t.ToDto()).ToList(), query.Page, query.PageSize, total));
    }

    // GET /timeoff/{timeOffId}
    [HttpGet("{timeOffId:guid}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<TimeOffDto>> Get(Guid timeOffId, CancellationToken ct)
    {
        var row = await db.TimeOff.AsNoTracking().Include(t => t.Resource).Include(t => t.Booker)
            .FirstOrDefaultAsync(t => t.Id == timeOffId, ct);
        return row is null ? NotFoundProblem($"Time off {timeOffId} not found.") : Ok(row.ToDto());
    }

    // POST /timeoff
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<TimeOffDto>> Create([FromBody] TimeOffCreate body, CancellationToken ct)
    {
        if (body.EndDate < body.StartDate)
            return BadRequestProblem("End date must be on or after start date.");
        if (!await db.Resources.AnyAsync(r => r.Id == body.ResourceId, ct))
            return BadRequestProblem($"Resource {body.ResourceId} does not exist.");
        if (body.BookerId is { } bookerId && !await db.Resources.AnyAsync(r => r.Id == bookerId, ct))
            return BadRequestProblem($"Booker {bookerId} does not exist.");
        if (await OverlapProblem(body.ResourceId, body.StartDate, body.EndDate, null, ct) is { } overlap)
            return ConflictProblem(overlap);

        var row = new TimeOff
        {
            ResourceId = body.ResourceId,
            StartDate = body.StartDate,
            EndDate = body.EndDate,
            Type = body.Type,
            HoursPerDay = body.HoursPerDay,
            Note = body.Note,
            BookerId = body.BookerId,
        };
        db.TimeOff.Add(row);
        await db.SaveChangesAsync(ct);

        await db.Entry(row).Reference(t => t.Resource).LoadAsync(ct);
        await db.Entry(row).Reference(t => t.Booker).LoadAsync(ct);
        return Created($"/v1/timeoff/{row.Id}", row.ToDto());
    }

    // PUT /timeoff/{timeOffId}
    [HttpPut("{timeOffId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<TimeOffDto>> Update(
        Guid timeOffId, [FromBody] TimeOffUpdate body, CancellationToken ct)
    {
        var row = await db.TimeOff.Include(t => t.Resource).Include(t => t.Booker)
            .FirstOrDefaultAsync(t => t.Id == timeOffId, ct);
        if (row is null) return NotFoundProblem($"Time off {timeOffId} not found.");
        if (body.EndDate < body.StartDate)
            return BadRequestProblem("End date must be on or after start date.");
        if (body.BookerId is { } bookerId && !await db.Resources.AnyAsync(r => r.Id == bookerId, ct))
            return BadRequestProblem($"Booker {bookerId} does not exist.");
        if (await OverlapProblem(row.ResourceId, body.StartDate, body.EndDate, timeOffId, ct) is { } overlap)
            return ConflictProblem(overlap);

        row.StartDate = body.StartDate;
        row.EndDate = body.EndDate;
        row.Type = body.Type;
        row.HoursPerDay = body.HoursPerDay;
        row.Note = body.Note;
        row.BookerId = body.BookerId;
        await db.SaveChangesAsync(ct);
        // The booker may have changed; reload it so BookerName reflects the write.
        await db.Entry(row).Reference(t => t.Booker).LoadAsync(ct);
        return Ok(row.ToDto());
    }

    // DELETE /timeoff/{timeOffId}
    [HttpDelete("{timeOffId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid timeOffId, CancellationToken ct)
    {
        var row = await db.TimeOff.FirstOrDefaultAsync(t => t.Id == timeOffId, ct);
        if (row is null) return NotFoundProblem($"Time off {timeOffId} not found.");
        db.TimeOff.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Two leave records for the same person over the same days would double-count
    /// against capacity, so overlap is rejected (FR-TIMEOFF-4). This is a genuine
    /// data error, unlike over-allocation which is a legitimate warning state.
    /// </summary>
    private async Task<string?> OverlapProblem(
        Guid resourceId, DateOnly start, DateOnly end, Guid? excludeId, CancellationToken ct)
    {
        var clash = await db.TimeOff.AsNoTracking()
            .Where(t => t.ResourceId == resourceId && t.Id != excludeId)
            .Where(t => t.StartDate <= end && t.EndDate >= start)
            .OrderBy(t => t.StartDate)
            .FirstOrDefaultAsync(ct);
        return clash is null
            ? null
            : $"Overlaps existing time off {clash.StartDate:yyyy-MM-dd}..{clash.EndDate:yyyy-MM-dd} ({clash.Id}).";
    }
}
