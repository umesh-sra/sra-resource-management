using Microsoft.EntityFrameworkCore;

namespace SraRms.Api.Data;

public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor? _http;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? http = null)
        : base(options)
    {
        _http = http;
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Allocation> Allocations => Set<Allocation>();

    // Reference pick-lists share one CLR type mapped to four tables.
    public DbSet<ReferenceItem> Departments => Set<ReferenceItem>(ReferenceCollections.Departments);
    public DbSet<ReferenceItem> Locations => Set<ReferenceItem>(ReferenceCollections.Locations);
    public DbSet<ReferenceItem> JobTitles => Set<ReferenceItem>(ReferenceCollections.JobTitles);
    public DbSet<ReferenceItem> Skills => Set<ReferenceItem>(ReferenceCollections.Skills);

    protected override void OnModelCreating(ModelBuilder b)
    {
        // CLR<->PostgreSQL enum mapping is configured via MapEnum in Program.cs.

        b.Entity<Client>(e =>
        {
            e.ToTable("client");
            e.HasMany(c => c.Projects).WithOne(p => p.Client)
                .HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("project");
            e.Property(p => p.Budget).HasPrecision(14, 2);
            e.Property(p => p.Remaining).HasPrecision(14, 2);
            e.HasMany(p => p.Allocations).WithOne(a => a.Project)
                .HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Resource>(e =>
        {
            e.ToTable("resource");
            e.Property(r => r.AvailabilityHoursPerWeek).HasPrecision(5, 2);
            e.HasMany(r => r.Allocations).WithOne(a => a.Resource)
                .HasForeignKey(a => a.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Allocation>(e =>
        {
            e.ToTable("allocation");
            e.Property(a => a.Effort).HasPrecision(7, 2);
        });

        // Shared-type reference entities -> one table each.
        foreach (var (name, table) in new[]
                 {
                     (ReferenceCollections.Departments, "department"),
                     (ReferenceCollections.Locations, "location"),
                     (ReferenceCollections.JobTitles, "job_title"),
                     (ReferenceCollections.Skills, "skill"),
                 })
        {
            b.SharedTypeEntity<ReferenceItem>(name, e => e.ToTable(table));
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampAudit();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    private void StampAudit()
    {
        var now = DateTimeOffset.UtcNow;
        var user = _http?.HttpContext?.User?.Identity?.Name
                   ?? _http?.HttpContext?.User?.FindFirst("preferred_username")?.Value;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = user;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = user;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = user;
            }
        }
    }
}

public static class ReferenceCollections
{
    public const string Departments = "Department";
    public const string Locations = "Location";
    public const string JobTitles = "JobTitle";
    public const string Skills = "Skill";

    // Maps the /reference/{collection} path token to a shared-type entity name.
    public static string? FromPath(string collection) => collection switch
    {
        "departments" => Departments,
        "locations" => Locations,
        "jobTitles" => JobTitles,
        "skills" => Skills,
        _ => null,
    };
}
