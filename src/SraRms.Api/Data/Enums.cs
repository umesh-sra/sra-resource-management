using NpgsqlTypes;

namespace SraRms.Api.Data;

// Enum CLR names are PascalCase; JSON serialisation uses camelCase (configured in
// Program.cs) which yields the exact OpenAPI tokens (e.g. OnHold -> "onHold").
// [PgName] pins each value to the PostgreSQL enum label defined in V001.

public enum ProjectStatus
{
    [PgName("planned")] Planned,
    [PgName("active")] Active,
    [PgName("onHold")] OnHold,
    [PgName("completed")] Completed,
    [PgName("cancelled")] Cancelled,
}

public enum ResourceStatus
{
    [PgName("active")] Active,
    [PgName("inactive")] Inactive,
    [PgName("onLeave")] OnLeave,
}

public enum EffortUnit
{
    [PgName("hoursPerWeek")] HoursPerWeek,
    [PgName("percent")] Percent,
}

// Named Weekday to avoid clashing with System.DayOfWeek.
public enum Weekday
{
    [PgName("monday")] Monday,
    [PgName("tuesday")] Tuesday,
    [PgName("wednesday")] Wednesday,
    [PgName("thursday")] Thursday,
    [PgName("friday")] Friday,
    [PgName("saturday")] Saturday,
    [PgName("sunday")] Sunday,
}
