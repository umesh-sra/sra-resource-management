using System.ComponentModel.DataAnnotations;
using SraRms.Api.Data;

namespace SraRms.Api.Contracts;

// ----------------------------------------------------------------------------
// Client
// ----------------------------------------------------------------------------
public class ClientCreate
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = null!;
}

public class ClientUpdate : ClientCreate { }

public record ClientDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public int ProjectCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public record ClientDetailDto : ClientDto
{
    public IReadOnlyList<ProjectDto> Projects { get; init; } = [];
    public IReadOnlyList<ResourceSummaryDto> Team { get; init; } = [];
}

// ----------------------------------------------------------------------------
// Project
// ----------------------------------------------------------------------------
public class ProjectCreate
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = null!;
    [Required, StringLength(50, MinimumLength = 1)] public string Code { get; set; } = null!;
    [Required] public Guid ClientId { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Range(0, double.MaxValue)] public decimal? Budget { get; set; }
    [Range(0, double.MaxValue)] public decimal? Remaining { get; set; }
    public bool Billable { get; set; } = true;
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
}

public class ProjectUpdate
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = null!;
    [Required, StringLength(50, MinimumLength = 1)] public string Code { get; set; } = null!;
    public Guid? ClientId { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Range(0, double.MaxValue)] public decimal? Budget { get; set; }
    [Range(0, double.MaxValue)] public decimal? Remaining { get; set; }
    public bool Billable { get; set; } = true;
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
}

public record ProjectDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string? ClientName { get; init; }
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal? Budget { get; init; }
    public decimal? Remaining { get; init; }
    public bool Billable { get; init; }
    public ProjectStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public record ProjectDetailDto : ProjectDto
{
    public IReadOnlyList<AllocationDto> Allocations { get; init; } = [];
}

// ----------------------------------------------------------------------------
// Resource
// ----------------------------------------------------------------------------
public class ResourceCreate
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = null!;
    [Required, EmailAddress] public string Email { get; set; } = null!;
    [Required] public string PrimaryJobTitle { get; set; } = null!;
    public string? SecondaryJobTitle { get; set; }
    public ResourceStatus Status { get; set; } = ResourceStatus.Active;
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public List<string> Skills { get; set; } = new();
    [Required, Range(0, 168)] public decimal AvailabilityHoursPerWeek { get; set; }
    public List<Weekday> WorkingDays { get; set; } = new();
}

public class ResourceUpdate : ResourceCreate { }

public record ResourceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Email { get; init; } = null!;
    public string PrimaryJobTitle { get; init; } = null!;
    public string? SecondaryJobTitle { get; init; }
    public ResourceStatus Status { get; init; }
    public string? Department { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    public string? ImageUrl { get; init; }
    public decimal AvailabilityHoursPerWeek { get; init; }
    public IReadOnlyList<Weekday> WorkingDays { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public record ResourceSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? PrimaryJobTitle { get; init; }
    public string? ImageUrl { get; init; }
}

public record ResourceDetailDto : ResourceDto
{
    public IReadOnlyList<AllocationDto> Allocations { get; init; } = [];
    public decimal AllocatedHoursPerWeek { get; init; }
}

// ----------------------------------------------------------------------------
// Allocation
// ----------------------------------------------------------------------------
public class AllocationCreate
{
    [Required] public Guid ResourceId { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Required, Range(0, double.MaxValue)] public decimal Effort { get; set; }
    [Required] public EffortUnit EffortUnit { get; set; }
    public string? RoleOnProject { get; set; }
    public bool? Billable { get; set; }
}

public class AllocationCreateFull : AllocationCreate
{
    [Required] public Guid ProjectId { get; set; }
}

public class AllocationUpdate
{
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Required, Range(0, double.MaxValue)] public decimal Effort { get; set; }
    [Required] public EffortUnit EffortUnit { get; set; }
    public string? RoleOnProject { get; set; }
    public bool? Billable { get; set; }
}

public record AllocationDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public Guid ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal Effort { get; init; }
    public EffortUnit EffortUnit { get; init; }
    public string? RoleOnProject { get; init; }
    public bool Billable { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// ----------------------------------------------------------------------------
// Dashboard
// ----------------------------------------------------------------------------
public record DashboardSummaryDto
{
    public int ActiveProjects { get; init; }
    public int TotalResources { get; init; }
    public double AverageUtilisation { get; init; }
    public int OverAllocatedResources { get; init; }
    public int UnderAllocatedResources { get; init; }
    public double BudgetAtRisk { get; init; }
    public IReadOnlyList<ProjectDto> UpcomingProjectStarts { get; init; } = [];
    public IReadOnlyList<AllocationDto> UpcomingRollOffs { get; init; } = [];
}

public record GanttResponseDto
{
    public string View { get; init; } = null!;
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public IReadOnlyList<GanttRowDto> Rows { get; init; } = [];
}

public record GanttRowDto(string Id, string Label, IReadOnlyList<GanttBarDto> Bars);

public record GanttBarDto
{
    public Guid? RefId { get; init; }
    public string? Label { get; init; }
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public double? Effort { get; init; }
    public bool? OverAllocated { get; init; }
}

// ----------------------------------------------------------------------------
// Reports
// ----------------------------------------------------------------------------
public record UtilisationReportDto(DateOnly From, DateOnly To, IReadOnlyList<UtilisationRow> Rows);
public record UtilisationRow(Guid ResourceId, string ResourceName, string? Department,
    double AvailableHours, double AllocatedHours, double Utilisation);

public record AllocationReportDto(DateOnly From, DateOnly To, IReadOnlyList<AllocationReportRow> Rows);
public record AllocationReportRow(Guid ProjectId, string ProjectName, string? ClientName,
    Guid ResourceId, string ResourceName, double AllocatedHours, bool Billable);

public record BudgetReportDto(IReadOnlyList<BudgetReportRow> Rows);
public record BudgetReportRow(string? ClientName, Guid ProjectId, string ProjectName,
    double Budget, double Remaining, double Consumed, double PercentConsumed);

// ----------------------------------------------------------------------------
// Reference data
// ----------------------------------------------------------------------------
public record ReferenceItemDto(Guid Id, string Value, bool Active);

public class ReferenceItemCreate
{
    [Required, MinLength(1)] public string Value { get; set; } = null!;
}
