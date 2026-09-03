using Microsoft.EntityFrameworkCore;

namespace SraRms.Api.Data;

public static class DbSetup
{
    /// <summary>
    /// Configures EF Core for PostgreSQL with the CLR<->enum mappings and
    /// snake_case naming used across the app. Shared by Program.cs and the
    /// integration test host so both stay in sync.
    /// </summary>
    public static DbContextOptionsBuilder ConfigureNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npg =>
        {
            npg.MapEnum<ProjectStatus>("project_status");
            npg.MapEnum<ResourceStatus>("resource_status");
            npg.MapEnum<EffortUnit>("effort_unit");
            npg.MapEnum<Weekday>("day_of_week");
            npg.MapEnum<BookableStatus>("bookable_status");
            npg.MapEnum<TimeOffType>("time_off_type");
            npg.MapEnum<MilestoneStatus>("milestone_status");
            npg.MapEnum<ProjectBudgetType>("project_budget_type");
            npg.MapEnum<BookingStatus>("booking_status");
        }).UseSnakeCaseNamingConvention();
}
