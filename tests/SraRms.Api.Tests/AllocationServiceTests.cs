using SraRms.Api.Data;
using SraRms.Api.Services;

namespace SraRms.Api.Tests;

public class AllocationServiceTests
{
    [Theory]
    [InlineData(20, EffortUnit.HoursPerWeek, 38, 20)]   // hours are taken as-is
    [InlineData(50, EffortUnit.Percent, 38, 19)]        // 50% of 38h = 19h
    [InlineData(100, EffortUnit.Percent, 40, 40)]
    public void WeeklyHours_converts_effort(decimal effort, EffortUnit unit, decimal availability, decimal expected)
    {
        Assert.Equal(expected, AllocationService.WeeklyHours(effort, unit, availability));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-15", "2026-02-15", true)]  // partial overlap
    [InlineData("2026-01-01", "2026-01-31", "2026-02-01", "2026-02-28", false)] // disjoint
    [InlineData("2026-01-01", "2026-01-31", "2026-01-31", "2026-02-15", true)]  // touching boundary
    [InlineData("2026-03-01", "2026-03-31", "2026-01-01", "2026-12-31", true)]  // fully contained
    public void Overlaps_detects_date_range_intersection(string s1, string e1, string s2, string e2, bool expected)
    {
        Assert.Equal(expected, AllocationService.Overlaps(D(s1), D(e1), D(s2), D(e2)));
    }

    private static DateOnly D(string s) => DateOnly.Parse(s);
}
