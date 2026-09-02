namespace SraRms.Api.Services;

/// <summary>
/// Resolves "today" in the organisation's business time zone.
///
/// Dashboard horizons, current-allocation windows and utilisation are *business
/// dates*, not instants: for an Adelaide user the day must roll over at local
/// midnight, not when UTC does. Deriving them from <see cref="DateTime.UtcNow"/>
/// made the API disagree with the SPA (which uses the browser's local date) for
/// the first 9.5-10 hours of every Australian day — a project starting "today"
/// would vanish from the dashboard horizon while the agenda still showed it.
///
/// The zone comes from <c>App:TimeZone</c>; SRS §2.5 allows a single configurable
/// time zone for the first release. Per-resource time zones (V002
/// <c>resource.time_zone</c>) describe where a person works and are deliberately
/// not used here: the dashboard is an organisation-level view and must show the
/// same figures to everyone looking at it.
/// </summary>
/// <remarks>
/// Instants — audit <c>created_at</c>/<c>updated_at</c> — stay UTC. Only values
/// that are genuinely calendar dates belong here.
/// </remarks>
public sealed class BusinessClock(TimeProvider time, TimeZoneInfo zone)
{
    /// <summary>The configured business time zone.</summary>
    public TimeZoneInfo TimeZone { get; } = zone;

    /// <summary>The current instant, expressed in the business time zone.</summary>
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone);

    /// <summary>Today's date in the business time zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>
    /// Builds a clock from configuration, throwing on an unknown zone id rather
    /// than silently falling back to UTC — a silent fallback would reintroduce
    /// exactly the off-by-a-day bug this type exists to prevent. Program.cs calls
    /// this during startup so the throw happens before the host runs.
    /// </summary>
    public static BusinessClock FromConfiguration(TimeProvider time, string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try
        {
            return new BusinessClock(time, TimeZoneInfo.FindSystemTimeZoneById(id));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"App:TimeZone '{id}' is not a time zone this host knows. Use an IANA name such as "
                + "'Australia/Adelaide' (supported on Windows and Linux via ICU), or 'UTC'.", ex);
        }
    }
}
