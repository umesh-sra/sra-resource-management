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

    // V002 - Budget tab. BudgetType decides which budget fields must be set; the
    // pairing is validated in ProjectsController and by ck_project_budget_type.
    public ProjectBudgetType BudgetType { get; set; } = ProjectBudgetType.None;
    [Range(0, double.MaxValue)] public decimal? BudgetHours { get; set; }
    [Range(0, double.MaxValue)] public decimal? RemainingHours { get; set; }
    public List<string> ActivityTypes { get; set; } = new();
    public string? Details { get; set; }
    [RegularExpression("^#[0-9A-Fa-f]{6}$")] public string? Colour { get; set; }
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

    // V002 - Budget tab. BudgetType decides which budget fields must be set; the
    // pairing is validated in ProjectsController and by ck_project_budget_type.
    public ProjectBudgetType BudgetType { get; set; } = ProjectBudgetType.None;
    [Range(0, double.MaxValue)] public decimal? BudgetHours { get; set; }
    [Range(0, double.MaxValue)] public decimal? RemainingHours { get; set; }
    public List<string> ActivityTypes { get; set; } = new();
    public string? Details { get; set; }
    [RegularExpression("^#[0-9A-Fa-f]{6}$")] public string? Colour { get; set; }
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

    // V002 - Budget tab (new_project_budget.png) and Overview extras.
    public ProjectBudgetType BudgetType { get; init; }
    public decimal? BudgetHours { get; init; }
    public decimal? RemainingHours { get; init; }
    public IReadOnlyList<string> ActivityTypes { get; init; } = [];
    public string? Details { get; init; }
    public string? Colour { get; init; }

    /// <summary>
    /// Distinct people allocated to the project. Present on list responses so the
    /// projects grid can render a team roster without an extra call per row.
    /// </summary>
    public IReadOnlyList<ResourceSummaryDto> Team { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public record ProjectDetailDto : ProjectDto
{
    public IReadOnlyList<AllocationDto> Allocations { get; init; } = [];
    public IReadOnlyList<ProjectPhaseDto> Phases { get; init; } = [];
    public IReadOnlyList<ProjectMilestoneDto> Milestones { get; init; } = [];
}

// ---- Phases & milestones (V002) --------------------------------------------
public class ProjectPhaseCreate
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = null!;
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [RegularExpression("^#[0-9A-Fa-f]{6}$")] public string? Colour { get; set; }
    public int SortOrder { get; set; }
}

public class ProjectPhaseUpdate : ProjectPhaseCreate { }

public record ProjectPhaseDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = null!;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? Colour { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public class ProjectMilestoneCreate
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; set; } = null!;
    [Required] public DateOnly DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Pending;
    public string? Note { get; set; }
}

public class ProjectMilestoneUpdate : ProjectMilestoneCreate { }

public record ProjectMilestoneDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = null!;
    public DateOnly DueDate { get; init; }
    public MilestoneStatus Status { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
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

    // V002 - person panel groups (person_overview.png, new_person_part*.png).
    public string? JobRole { get; set; }
    public Guid? ManagerId { get; set; }
    [Phone] public string? Phone { get; set; }
    public List<string> SecondarySkills { get; set; } = new();
    public List<string> SecurityClearances { get; set; } = new();
    public DateOnly? SecurityNpcObtainedOn { get; set; }
    public List<string> Certifications { get; set; } = new();
    public string? TimeZone { get; set; }
    public BookableStatus BookableStatus { get; set; } = BookableStatus.Bookable;
    public string? PublicHolidayCalendar { get; set; }
    [Range(0, double.MaxValue)] public decimal? DefaultRateHourly { get; set; }
    [RegularExpression("^#[0-9A-Fa-f]{6}$")] public string? Colour { get; set; }
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

    // V002 - person panel groups.
    public string? JobRole { get; init; }
    public Guid? ManagerId { get; init; }
    public string? ManagerName { get; init; }
    public string? Phone { get; init; }
    public IReadOnlyList<string> SecondarySkills { get; init; } = [];
    public IReadOnlyList<string> SecurityClearances { get; init; } = [];
    public DateOnly? SecurityNpcObtainedOn { get; init; }
    public IReadOnlyList<string> Certifications { get; init; } = [];
    public string? TimeZone { get; init; }
    public BookableStatus BookableStatus { get; init; }
    public string? PublicHolidayCalendar { get; init; }
    public decimal? DefaultRateHourly { get; init; }
    public string? Colour { get; init; }

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
    public IReadOnlyList<TimeOffDto> TimeOff { get; init; } = [];
}

// ---- Time off (V002) --------------------------------------------------------
public class TimeOffCreate
{
    [Required] public Guid ResourceId { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; } = TimeOffType.AnnualLeave;
    [Range(0.01, 24)] public decimal? HoursPerDay { get; set; }
    /// <summary>Surfaced as "Details" on the time-off dialog.</summary>
    public string? Note { get; set; }
    /// <summary>V003 - resource the leave was arranged by; must exist.</summary>
    public Guid? BookerId { get; set; }
}

public class TimeOffUpdate
{
    [Required] public DateOnly StartDate { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; } = TimeOffType.AnnualLeave;
    [Range(0.01, 24)] public decimal? HoursPerDay { get; set; }
    /// <summary>Surfaced as "Details" on the time-off dialog.</summary>
    public string? Note { get; set; }
    /// <summary>V003 - resource the leave was arranged by; must exist.</summary>
    public Guid? BookerId { get; set; }
}

public record TimeOffDto
{
    public Guid Id { get; init; }
    public Guid ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public TimeOffType Type { get; init; }
    public decimal? HoursPerDay { get; init; }
    public string? Note { get; init; }
    public Guid? BookerId { get; init; }
    /// <summary>Resolved from the booker resource; null when no booker is set.</summary>
    public string? BookerName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
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
    /// <summary>V002 - per-person billable rate (project Team tab).</summary>
    [Range(0, double.MaxValue)] public decimal? HourlyRate { get; set; }
    /// <summary>V003 - free-text Details on the booking dialog.</summary>
    public string? Details { get; set; }
    /// <summary>V003 - resource the booking was arranged by; must exist.</summary>
    public Guid? BookerId { get; set; }
    /// <summary>V004 - how firm the booking is; defaults to confirmed.</summary>
    public BookingStatus BookingStatus { get; set; } = BookingStatus.Confirmed;
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
    /// <summary>V002 - per-person billable rate (project Team tab).</summary>
    [Range(0, double.MaxValue)] public decimal? HourlyRate { get; set; }
    /// <summary>V003 - free-text Details on the booking dialog.</summary>
    public string? Details { get; set; }
    /// <summary>V003 - resource the booking was arranged by; must exist.</summary>
    public Guid? BookerId { get; set; }
    /// <summary>V004 - how firm the booking is; defaults to confirmed.</summary>
    public BookingStatus BookingStatus { get; set; } = BookingStatus.Confirmed;
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
    public decimal? HourlyRate { get; init; }
    public string? Details { get; init; }
    public Guid? BookerId { get; init; }
    /// <summary>Resolved from the booker resource; null when no booker is set.</summary>
    public string? BookerName { get; init; }
    /// <summary>V004 - how firm the booking is.</summary>
    public BookingStatus BookingStatus { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// ----------------------------------------------------------------------------
// Dashboard
// ----------------------------------------------------------------------------
public record DashboardSummaryDto
{
    /// <summary>
    /// The business date the figures were computed for, in the organisation's
    /// time zone. Exposed so the SPA can anchor on the same day the server used
    /// instead of the browser's local date, which may differ.
    /// </summary>
    public DateOnly Today { get; init; }

    /// <summary>IANA name of the business time zone (see App:TimeZone).</summary>
    public string TimeZone { get; init; } = "UTC";

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
    /// <summary>Unit for <see cref="Effort"/>, so a bar can be labelled without a second lookup.</summary>
    public EffortUnit? EffortUnit { get; init; }
    public bool? OverAllocated { get; init; }
    /// <summary>V004 - so an unconfirmed booking can be drawn as provisional.</summary>
    public BookingStatus? BookingStatus { get; init; }
}

// ----------------------------------------------------------------------------
// Reports
// ----------------------------------------------------------------------------
public record UtilisationReportDto(DateOnly From, DateOnly To, IReadOnlyList<UtilisationRow> Rows);
/// <param name="AvailableHours">Gross availability over the window.</param>
/// <param name="TimeOffHours">Working-day hours lost to leave in the window (V002).</param>
/// <param name="EffectiveCapacityHours">AvailableHours minus TimeOffHours (FR-REP-6).</param>
/// <param name="Utilisation">
/// AllocatedHours / AvailableHours. Deliberately measured against gross
/// availability so the figure is unchanged by V002; the reference app reports
/// effective capacity alongside it rather than folding leave into the ratio.
/// </param>
/// <param name="UnconfirmedHours">
/// V004 — the part of AllocatedHours that comes from tentative or waiting
/// bookings. Reported alongside rather than deducted, for the same reason as
/// TimeOffHours: Utilisation stays comparable across releases, and the caller
/// can see how much of the load is not yet firm (FR-REP-7).
/// </param>
public record UtilisationRow(Guid ResourceId, string ResourceName, string? Department,
    double AvailableHours, double AllocatedHours, double Utilisation,
    double TimeOffHours = 0, double EffectiveCapacityHours = 0, double UnconfirmedHours = 0);

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
