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
    public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
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
    public ICollection<Allocation> Allocations { get; set; } = new List<Allocation>();
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
}

// Backs department / location / job_title / skill reference tables. Discriminated
// by the table it maps to (see AppDbContext); shape matches OpenAPI ReferenceItem.
public class ReferenceItem : AuditableEntity
{
    public string Value { get; set; } = null!;
    public bool Active { get; set; } = true;
}
