namespace SraRms.Api.Data;

// EF Core entities. Table and column names resolve to snake_case via
// UseSnakeCaseNamingConvention(), matching the schema in db/migrations/V001.

public abstract class AuditableEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public class Client : AuditableEntity
{
    public string Name { get; set; } = null!;
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}

public class Project : AuditableEntity
{
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal? Budget { get; set; }
    public decimal? Remaining { get; set; }
    public bool Billable { get; set; } = true;
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    // V002 — Budget tab (new_project_budget.png). Budget/Remaining hold the fee
    // budget; BudgetHours/RemainingHours the hour budget. BudgetType says which
    // is in force and is enforced by ck_project_budget_type.
    public ProjectBudgetType BudgetType { get; set; } = ProjectBudgetType.None;
    public decimal? BudgetHours { get; set; }
    public decimal? RemainingHours { get; set; }
    public List<string> ActivityTypes { get; set; } = new();
    public string? Details { get; set; }
    public string? Colour { get; set; }

    public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
    public ICollection<ProjectPhase> Phases { get; set; } = new List<ProjectPhase>();
    public ICollection<ProjectMilestone> Milestones { get; set; } = new List<ProjectMilestone>();
}

public class Resource : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PrimaryJobTitle { get; set; } = null!;
    public string? SecondaryJobTitle { get; set; }
    public ResourceStatus Status { get; set; } = ResourceStatus.Active;
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public List<string> Skills { get; set; } = new();
    public string? ImageUrl { get; set; }
    public decimal AvailabilityHoursPerWeek { get; set; }
    public List<Weekday> WorkingDays { get; set; } = new();

    // V002 — the person panel's Overview / Extra Details / Scheduling /
    // Financial groups (person_overview.png, new_person_part*.png).
    // JobRole is distinct from PrimaryJobTitle: the reference shows both.
    public string? JobRole { get; set; }
    public Guid? ManagerId { get; set; }
    public Resource? Manager { get; set; }
    public string? Phone { get; set; }
    /// <summary>Secondary skills; <see cref="Skills"/> carries the primary set.</summary>
    public List<string> SecondarySkills { get; set; } = new();
    public List<string> SecurityClearances { get; set; } = new();
    public DateOnly? SecurityNpcObtainedOn { get; set; }
    public List<string> Certifications { get; set; } = new();
    /// <summary>IANA time-zone name, e.g. "Australia/Adelaide".</summary>
    public string? TimeZone { get; set; }
    public BookableStatus BookableStatus { get; set; } = BookableStatus.Bookable;
    public string? PublicHolidayCalendar { get; set; }
    public decimal? DefaultRateHourly { get; set; }
    public string? Colour { get; set; }

    public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
    public ICollection<TimeOff> TimeOff { get; set; } = new List<TimeOff>();
    public ICollection<Resource> DirectReports { get; set; } = new List<Resource>();
}

public class Allocation : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Effort { get; set; }
    public EffortUnit EffortUnit { get; set; }
    public string? RoleOnProject { get; set; }
    public bool Billable { get; set; } = true;
    /// <summary>V002 — per-person billable rate, set on the project's Team tab.</summary>
    public decimal? HourlyRate { get; set; }
    /// <summary>V003 — free-text Details on the booking dialog.</summary>
    public string? Details { get; set; }
    /// <summary>
    /// V003 — the person the booking was arranged by. Business data chosen in
    /// the dialog, distinct from the <c>CreatedBy</c> audit stamp.
    /// </summary>
    public Guid? BookerId { get; set; }
    public Resource? Booker { get; set; }
}

// ---- V002: reference-application model -------------------------------------

/// <summary>A named, dated stage within a project (Requirements §3.6).</summary>
public class ProjectPhase : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Colour { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A dated checkpoint on a project (Requirements §3.7).</summary>
public class ProjectMilestone : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateOnly DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Pending;
    public string? Note { get; set; }
}

/// <summary>
/// Leave for one resource over a date range (Requirements §3.8). Does not block
/// allocation; reduces effective capacity in the utilisation report (FR-REP-6).
/// </summary>
public class TimeOff : AuditableEntity
{
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; } = TimeOffType.AnnualLeave;
    /// <summary>Null means the whole working day is unavailable.</summary>
    public decimal? HoursPerDay { get; set; }
    /// <summary>Surfaced as "Details" on the time-off dialog.</summary>
    public string? Note { get; set; }
    /// <summary>V003 — see <see cref="Allocation.BookerId"/>.</summary>
    public Guid? BookerId { get; set; }
    public Resource? Booker { get; set; }
}

// Backs department / location / job_title / skill / activity_type reference tables. Discriminated
// by the table it maps to (see AppDbContext); shape matches OpenAPI ReferenceItem.
public class ReferenceItem : AuditableEntity
{
    public string Value { get; set; } = null!;
    public bool Active { get; set; } = true;
}
