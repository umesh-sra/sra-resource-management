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

    /// <param name="team">
    /// Optional roster for the project. Callers that have not loaded allocations
    /// pass null and get an empty list rather than a lazy-load surprise.
    /// </param>
    public static ProjectDto ToDto(this Project p, IReadOnlyList<ResourceSummaryDto>? team = null) => new()
    {
        Team = team ?? [],
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
        BudgetType = p.BudgetType,
        BudgetHours = p.BudgetHours,
        RemainingHours = p.RemainingHours,
        ActivityTypes = p.ActivityTypes,
        Details = p.Details,
        Colour = p.Colour,
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
        JobRole = r.JobRole,
        ManagerId = r.ManagerId,
        // Only populated when the caller has Include()d Manager; the person
        // panel shows a name, list endpoints leave it null rather than N+1.
        ManagerName = r.Manager?.Name,
        Phone = r.Phone,
        SecondarySkills = r.SecondarySkills,
        SecurityClearances = r.SecurityClearances,
        SecurityNpcObtainedOn = r.SecurityNpcObtainedOn,
        Certifications = r.Certifications,
        TimeZone = r.TimeZone,
        BookableStatus = r.BookableStatus,
        PublicHolidayCalendar = r.PublicHolidayCalendar,
        DefaultRateHourly = r.DefaultRateHourly,
        Colour = r.Colour,
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
        HourlyRate = a.HourlyRate,
        Details = a.Details,
        BookerId = a.BookerId,
        BookerName = a.Booker?.Name,
        Warnings = warnings ?? [],
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
    };

    // ---- V002 -------------------------------------------------------------

    public static ProjectPhaseDto ToDto(this ProjectPhase p) => new()
    {
        Id = p.Id,
        ProjectId = p.ProjectId,
        Name = p.Name,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        Colour = p.Colour,
        SortOrder = p.SortOrder,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };

    public static ProjectMilestoneDto ToDto(this ProjectMilestone m) => new()
    {
        Id = m.Id,
        ProjectId = m.ProjectId,
        Name = m.Name,
        DueDate = m.DueDate,
        Status = m.Status,
        Note = m.Note,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };

    public static TimeOffDto ToDto(this TimeOff t) => new()
    {
        Id = t.Id,
        ResourceId = t.ResourceId,
        ResourceName = t.Resource?.Name,
        StartDate = t.StartDate,
        EndDate = t.EndDate,
        Type = t.Type,
        HoursPerDay = t.HoursPerDay,
        Note = t.Note,
        BookerId = t.BookerId,
        BookerName = t.Booker?.Name,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };

    public static ReferenceItemDto ToDto(this ReferenceItem r) => new(r.Id, r.Value, r.Active);
}
