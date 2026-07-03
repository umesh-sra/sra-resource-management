using SraRms.Api.Data;

namespace SraRms.Api.Contracts;

/// <summary>Entity -> DTO projections.</summary>
public static class Mapping
{
    public static ClientDto ToDto(this Client c, int projectCount) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ProjectCount = projectCount,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    public static ProjectDto ToDto(this Project p) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
        ClientName = p.Client?.Name,
        Name = p.Name,
        Code = p.Code,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Budget = p.Budget,
        Remaining = p.Remaining,
        Billable = p.Billable,
        Status = p.Status,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };

    public static ResourceDto ToDto(this Resource r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Email = r.Email,
        PrimaryJobTitle = r.PrimaryJobTitle,
        SecondaryJobTitle = r.SecondaryJobTitle,
        Status = r.Status,
        Department = r.Department,
        Location = r.Location,
        Notes = r.Notes,
        Skills = r.Skills,
        ImageUrl = r.ImageUrl,
        AvailabilityHoursPerWeek = r.AvailabilityHoursPerWeek,
        WorkingDays = r.WorkingDays,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    public static ResourceSummaryDto ToSummary(this Resource r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        PrimaryJobTitle = r.PrimaryJobTitle,
        ImageUrl = r.ImageUrl,
    };

    public static AllocationDto ToDto(this Allocation a, IReadOnlyList<string>? warnings = null) => new()
    {
        Id = a.Id,
        ProjectId = a.ProjectId,
        ProjectName = a.Project?.Name,
        ResourceId = a.ResourceId,
        ResourceName = a.Resource?.Name,
        StartDate = a.StartDate,
        EndDate = a.EndDate,
        Effort = a.Effort,
        EffortUnit = a.EffortUnit,
        RoleOnProject = a.RoleOnProject,
        Billable = a.Billable,
        Warnings = warnings ?? [],
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };

    public static ReferenceItemDto ToDto(this ReferenceItem r) => new(r.Id, r.Value, r.Active);
}
