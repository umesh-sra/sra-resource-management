using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SraRms.Api.Auth;
using SraRms.Api.Contracts;
using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Controllers;

[Route("v1/resources")]
public class ResourcesController(AppDbContext db, IWebHostEnvironment env) : BaseApiController
{
    // GET /resources
    [HttpGet]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<ResourceDto>>> List(
        [FromQuery] ListQuery query,
        [FromQuery(Name = "skill")] string[]? skill,
        [FromQuery] string? department,
        [FromQuery] string? location,
        [FromQuery] ResourceStatus? status,
        CancellationToken ct)
    {
        var q = db.Resources.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Q))
            q = q.Where(r => EF.Functions.ILike(r.Name, $"%{query.Q}%") || EF.Functions.ILike(r.Email, $"%{query.Q}%"));
        if (skill is { Length: > 0 })
            foreach (var s in skill) q = q.Where(r => r.Skills.Contains(s)); // AND semantics
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(r => r.Department == department);
        if (!string.IsNullOrWhiteSpace(location)) q = q.Where(r => r.Location == location);
        if (status is not null) q = q.Where(r => r.Status == status);

        var sort = query.ParseSort();
        q = sort?.Field switch
        {
            "name" => sort.Value.Desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name),
            "email" => sort.Value.Desc ? q.OrderByDescending(r => r.Email) : q.OrderBy(r => r.Email),
            "department" => sort.Value.Desc ? q.OrderByDescending(r => r.Department) : q.OrderBy(r => r.Department),
            "status" => sort.Value.Desc ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
            _ => q.OrderBy(r => r.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var dtos = items.Select(r => r.ToDto()).ToList();
        return Ok(Page<ResourceDto>.Create(dtos, query.Page, query.PageSize, total));
    }

    // POST /resources
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ResourceDto>> Create([FromBody] ResourceCreate body, CancellationToken ct)
    {
        if (await db.Resources.AnyAsync(r => r.Email.ToLower() == body.Email.ToLower(), ct))
            return ConflictProblem($"A resource with email '{body.Email}' already exists.");

        var resource = new Resource
        {
            Name = body.Name,
            Email = body.Email,
            PrimaryJobTitle = body.PrimaryJobTitle,
            SecondaryJobTitle = body.SecondaryJobTitle,
            Status = body.Status,
            Department = body.Department,
            Location = body.Location,
            Notes = body.Notes,
            Skills = body.Skills,
            AvailabilityHoursPerWeek = body.AvailabilityHoursPerWeek,
            WorkingDays = body.WorkingDays,
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync(ct);
        return Created($"/v1/resources/{resource.Id}", resource.ToDto());
    }

    // GET /resources/{resourceId}
    [HttpGet("{resourceId:guid}")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<ResourceDetailDto>> Get(Guid resourceId, CancellationToken ct)
    {
        // NB: do not Include Allocations->Resource — it cycles back to this resource.
        var resource = await db.Resources.AsNoTracking()
            .Include(r => r.Allocations).ThenInclude(a => a.Project)
            .FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null) return NotFoundProblem($"Resource {resourceId} not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = resource.Allocations.Where(a => a.StartDate <= today && today <= a.EndDate);
        var allocatedHours = current.Sum(a =>
            AllocationService.WeeklyHours(a.Effort, a.EffortUnit, resource.AvailabilityHoursPerWeek));

        var dto = resource.ToDto();
        var detail = new ResourceDetailDto
        {
            Id = dto.Id,
            Name = dto.Name,
            Email = dto.Email,
            PrimaryJobTitle = dto.PrimaryJobTitle,
            SecondaryJobTitle = dto.SecondaryJobTitle,
            Status = dto.Status,
            Department = dto.Department,
            Location = dto.Location,
            Notes = dto.Notes,
            Skills = dto.Skills,
            ImageUrl = dto.ImageUrl,
            AvailabilityHoursPerWeek = dto.AvailabilityHoursPerWeek,
            WorkingDays = dto.WorkingDays,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Allocations = resource.Allocations.OrderBy(a => a.StartDate).Select(a => a.ToDto()).ToList(),
            AllocatedHoursPerWeek = allocatedHours,
        };
        return Ok(detail);
    }

    // PUT /resources/{resourceId}
    [HttpPut("{resourceId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ResourceDto>> Update(Guid resourceId, [FromBody] ResourceUpdate body, CancellationToken ct)
    {
        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null) return NotFoundProblem($"Resource {resourceId} not found.");
        if (await db.Resources.AnyAsync(r => r.Id != resourceId && r.Email.ToLower() == body.Email.ToLower(), ct))
            return ConflictProblem($"A resource with email '{body.Email}' already exists.");

        resource.Name = body.Name;
        resource.Email = body.Email;
        resource.PrimaryJobTitle = body.PrimaryJobTitle;
        resource.SecondaryJobTitle = body.SecondaryJobTitle;
        resource.Status = body.Status;
        resource.Department = body.Department;
        resource.Location = body.Location;
        resource.Notes = body.Notes;
        resource.Skills = body.Skills;
        resource.AvailabilityHoursPerWeek = body.AvailabilityHoursPerWeek;
        resource.WorkingDays = body.WorkingDays;
        await db.SaveChangesAsync(ct);
        return Ok(resource.ToDto());
    }

    // DELETE /resources/{resourceId}?cascade=
    [HttpDelete("{resourceId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(Guid resourceId, [FromQuery] bool cascade, CancellationToken ct)
    {
        var resource = await db.Resources.Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null) return NotFoundProblem($"Resource {resourceId} not found.");

        if (resource.Allocations.Count > 0 && !cascade)
            return ConflictProblem($"Resource has {resource.Allocations.Count} allocation(s). Pass cascade=true to delete them too.");

        if (cascade) db.Allocations.RemoveRange(resource.Allocations);
        db.Resources.Remove(resource);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // PUT /resources/{resourceId}/image
    [HttpPut("{resourceId:guid}/image")]
    [Authorize(Policy = Policies.Admin)]
    [Consumes("image/png", "image/jpeg")]
    public async Task<ActionResult<ResourceDto>> UploadImage(Guid resourceId, CancellationToken ct)
    {
        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null) return NotFoundProblem($"Resource {resourceId} not found.");

        var contentType = Request.ContentType ?? "";
        var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png"
            : contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg"
            : null;
        if (ext is null) return BadRequestProblem("Content-Type must be image/png or image/jpeg.");

        var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(root, "uploads", "resources");
        Directory.CreateDirectory(dir);
        var fileName = $"{resourceId}.{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, fileName)))
            await Request.Body.CopyToAsync(fs, ct);

        resource.ImageUrl = $"/uploads/resources/{fileName}";
        await db.SaveChangesAsync(ct);
        return Ok(resource.ToDto());
    }

    // GET /resources/{resourceId}/allocations
    [HttpGet("{resourceId:guid}/allocations")]
    [Authorize(Policy = Policies.Read)]
    public async Task<ActionResult<Page<AllocationDto>>> ListAllocations(
        Guid resourceId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (!await db.Resources.AnyAsync(r => r.Id == resourceId, ct))
            return NotFoundProblem($"Resource {resourceId} not found.");

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 25 : pageSize;

        var q = db.Allocations.AsNoTracking()
            .Include(a => a.Project).Include(a => a.Resource)
            .Where(a => a.ResourceId == resourceId);
        if (from is not null) q = q.Where(a => a.EndDate >= from);
        if (to is not null) q = q.Where(a => a.StartDate <= to);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(a => a.StartDate).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = items.Select(a => a.ToDto()).ToList();
        return Ok(Page<AllocationDto>.Create(dtos, page, pageSize, total));
    }
}
