using System.Text;
using SraRms.Api.Data;

namespace SraRms.Api.Services.Import;

/// <summary>Value translations between the Resource Guru vocabulary and the SRA-RMS model.</summary>
public static class ImportMaps
{
    /// <summary>Resource Guru's placeholder for bookings made against no client.</summary>
    public const string UnassignedClient = "No client assigned";

    /// <summary>Prefix marking a project code the importer had to invent.</summary>
    public const string SyntheticCodePrefix = "RG-";

    /// <summary>Fallback job title: <c>resource.primary_job_title</c> is NOT NULL.</summary>
    public const string UnspecifiedJobTitle = "Unspecified";

    /// <summary>
    /// Maps a Resource Guru downtime type to <see cref="TimeOffType"/>.
    /// "Holiday (personal)" is annual leave, not the model's <c>personal</c>
    /// (which is personal/carer's leave); parental leave and untyped downtime
    /// have no counterpart and fall to <c>other</c> rather than being guessed.
    /// </summary>
    public static TimeOffType ToTimeOffType(string? rgType) => rgType?.Trim().ToLowerInvariant() switch
    {
        "public holiday" => TimeOffType.PublicHoliday,
        "holiday (personal)" or "holiday" or "annual leave" => TimeOffType.AnnualLeave,
        "sick leave" or "sick" => TimeOffType.Sick,
        "personal leave" or "carer's leave" => TimeOffType.Personal,
        _ => TimeOffType.Other,
    };

    /// <summary>
    /// Maps Resource Guru's booking status. Its vocabulary is the same as
    /// SRA-RMS's (confirmed / tentative / waiting), so this is a straight
    /// translation; anything unrecognised is treated as firm rather than quietly
    /// demoted, because under-stating a commitment is the more dangerous error.
    /// </summary>
    public static BookingStatus ToBookingStatus(string? rgStatus) =>
        rgStatus?.Trim().ToLowerInvariant() switch
        {
            "tentative" => BookingStatus.Tentative,
            "waiting" or "waiting for approval" => BookingStatus.Waiting,
            _ => BookingStatus.Confirmed,
        };

    public static Weekday ToWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Weekday.Monday,
        DayOfWeek.Tuesday => Weekday.Tuesday,
        DayOfWeek.Wednesday => Weekday.Wednesday,
        DayOfWeek.Thursday => Weekday.Thursday,
        DayOfWeek.Friday => Weekday.Friday,
        DayOfWeek.Saturday => Weekday.Saturday,
        _ => Weekday.Sunday,
    };

    /// <summary>
    /// Project status inferred from the project window against the business date.
    /// Resource Guru's export carries no status column, and leaving everything
    /// "planned" would empty the dashboard's active-project figures.
    /// </summary>
    public static ProjectStatus StatusFor(DateOnly start, DateOnly end, DateOnly today) =>
        end < today ? ProjectStatus.Completed
        : start > today ? ProjectStatus.Planned
        : ProjectStatus.Active;

    /// <summary>
    /// An upper-case, hyphenated code fragment derived from a name, for projects
    /// the export left without a code.
    /// </summary>
    public static string Slug(string value, int maxLength)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasDash = true; // suppresses a leading hyphen
        foreach (var c in value.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim('-');
    }
}

/// <summary>
/// Collects non-fatal import problems, aggregated by code so that one bad column
/// across 800 rows reports as one warning with a count and a few examples rather
/// than 800 messages.
/// </summary>
public sealed class ImportIssueLog
{
    private const int MaxExamples = 5;

    private readonly Dictionary<string, Entry> _entries = [];

    private sealed class Entry
    {
        public required string Message { get; init; }
        public int Count { get; set; }
        public List<string> Examples { get; } = [];
    }

    public void Add(string code, string message, string? example = null)
    {
        if (!_entries.TryGetValue(code, out var entry))
            _entries[code] = entry = new Entry { Message = message };
        entry.Count++;
        if (example is not null && entry.Examples.Count < MaxExamples && !entry.Examples.Contains(example))
            entry.Examples.Add(example);
    }

    public IReadOnlyList<ImportIssueDto> ToList() =>
        _entries.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => new ImportIssueDto(e.Key, e.Value.Message, e.Value.Count, e.Value.Examples))
            .ToList();
}
