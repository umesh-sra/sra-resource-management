using SraRms.Api.Services;

namespace SraRms.Api.Tests;

/// <summary>
/// Pins the UTC/local business-date fix. These are pure unit tests — no fixture,
/// no container — because the defect was arithmetic, not persistence.
/// </summary>
public class BusinessClockTests
{
    /// <summary>A clock frozen at a chosen instant.</summary>
    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static BusinessClock Adelaide(DateTimeOffset utcNow) =>
        BusinessClock.FromConfiguration(new FrozenTime(utcNow), "Australia/Adelaide");

    [Fact]
    public void Today_is_the_local_date_when_utc_is_still_on_the_previous_day()
    {
        // 2026-09-02 00:30 in Adelaide (UTC+9:30) is 2026-09-01 15:00 UTC.
        // The old DateTime.UtcNow code answered 1 Sept here; the business date is 2 Sept.
        var clock = Adelaide(new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 2), clock.Today);
    }

    [Fact]
    public void Today_does_not_run_ahead_of_the_local_date()
    {
        // 2026-09-02 23:00 Adelaide is 2026-09-02 13:30 UTC — both agree on 2 Sept.
        var clock = Adelaide(new DateTimeOffset(2026, 9, 2, 13, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 2), clock.Today);
    }

    [Fact]
    public void Utc_zone_still_reports_the_utc_date()
    {
        var clock = BusinessClock.FromConfiguration(
            new FrozenTime(new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero)), "UTC");

        Assert.Equal(new DateOnly(2026, 9, 1), clock.Today);
    }

    [Fact]
    public void Missing_zone_configuration_defaults_to_utc()
    {
        var at = new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);

        foreach (var id in new string?[] { null, "", "   " })
        {
            var clock = BusinessClock.FromConfiguration(new FrozenTime(at), id);
            Assert.Equal(TimeZoneInfo.Utc.Id, clock.TimeZone.Id);
        }
    }

    [Fact]
    public void Unknown_zone_throws_rather_than_silently_serving_utc_dates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BusinessClock.FromConfiguration(
            new FrozenTime(DateTimeOffset.UnixEpoch), "Australia/Nowhere"));

        Assert.Contains("Australia/Nowhere", ex.Message);
    }

    [Fact]
    public void Daylight_saving_transition_is_handled_by_the_zone_rules()
    {
        // Adelaide moves to UTC+10:30 in October. 2026-10-05 00:30 local is
        // 2026-10-04 14:00 UTC — so the offset used must be the DST one, not the
        // fixed +9:30, or the date comes back a day early.
        var clock = Adelaide(new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 5), clock.Today);
    }
}
