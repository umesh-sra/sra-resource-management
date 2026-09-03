using System.IO.Compression;
using System.Text;

namespace SraRms.Api.Tests;

/// <summary>
/// Builds a synthetic Resource Guru report .zip for the import tests.
///
/// The header lines are copied verbatim from a real export, and rows are written
/// by column name into that full layout — so the tests fail if the importer
/// stops finding a column where Resource Guru actually puts it, not just if the
/// mapping logic breaks.
/// </summary>
public static class ImportFixtureBuilder
{
    public const string ResourceHeader =
        "Id,Name,Email,Type,Rate,Resource Field: Department,Resource Field: Job Role,"
        + "Resource Field: Job Title,Resource Field: Location,Resource Field: Manager,"
        + "Resource Field: Primary Skills,Resource Field: Secondary Skills,"
        + "Resource Field: Security Clearances,Resource Field: Security NPC (date obtained),"
        + "Resource Field: Staff Certifications,Phone";

    public const string BookingHeader =
        "Date,Hours,Days,Billable Rate Total,Booker,Resource,Email,Resource Type,Billable,"
        + "Approval Status,Booking Status,Project,Project Code,Activity Type,Client,"
        + "Project with Client,Details,Year,Quarter,Month,Month Name,Week Number,Phone,Rate,"
        + "Resource Field: Department,Resource Field: Job Role,Resource Field: Job Title,"
        + "Resource Field: Location,Resource Field: Manager,Resource Field: Primary Skills,"
        + "Resource Field: Secondary Skills,Resource Field: Security Clearances,"
        + "Resource Field: Security NPC (date obtained),Resource Field: Staff Certifications";

    public const string DowntimeHeader =
        "Date,Type,Hours,Days,Resource,Email,Resource Type,Details,Year,Quarter,Month,Month Name,"
        + "Week Number,Resource Field: Department,Resource Field: Job Role,Resource Field: Job Title,"
        + "Resource Field: Location,Resource Field: Manager,Resource Field: Primary Skills,"
        + "Resource Field: Secondary Skills,Resource Field: Security Clearances,"
        + "Resource Field: Security NPC (date obtained),Resource Field: Staff Certifications";

    public const string AvailabilityHeader =
        "Date,Available Hours,Available Days,Unscheduled Hours,Unscheduled Days,Overtime Hours,"
        + "Overtime Days,Utilization Hours,Utilization Days,Resource,Email,Resource Type,Year,"
        + "Quarter,Month,Month Name,Week Number,Resource Field: Department,Resource Field: Job Role,"
        + "Resource Field: Job Title,Resource Field: Location,Resource Field: Manager,"
        + "Resource Field: Primary Skills,Resource Field: Secondary Skills,"
        + "Resource Field: Security Clearances,Resource Field: Security NPC (date obtained),"
        + "Resource Field: Staff Certifications";

    public const string ScheduledVsActualsHeader =
        "Date,Project ID,Project,Project Code,Client ID,Client,Activity Type,Total Scheduled Hours,"
        + "Billable Scheduled Hours,Billable Rate Scheduled Total,Non Billable Scheduled Hours,"
        + "Total Actual Hours,Billable Actual Hours,Billable Rate Actual Total,"
        + "Non Billable Actual Hours,Total Actual Confirmed Hours,Billable Actual Confirmed Hours,"
        + "Billable Rate Actual Confirmed Total,Non Billable Actual Confirmed Hours,Year,Quarter,"
        + "Month,Month Name,Week Number";

    /// <summary>Renders a sheet: the real header line plus rows addressed by column name.</summary>
    public static string Sheet(string header, IEnumerable<Dictionary<string, string>> rows)
    {
        var columns = header.Split(',');
        var sb = new StringBuilder().Append(header).Append('\n');
        foreach (var row in rows)
        {
            foreach (var unknown in row.Keys.Where(k => !columns.Contains(k)))
                throw new ArgumentException($"'{unknown}' is not a column of this sheet.", nameof(rows));
            sb.AppendJoin(',', columns.Select(c => Quote(row.GetValueOrDefault(c, "")))).Append('\n');
        }
        return sb.ToString();
    }

    private static string Quote(string value) =>
        value.AsSpan().IndexOfAny(',', '"', '\n') >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    /// <summary>Zips the named sheets under the filenames Resource Guru uses.</summary>
    public static byte[] Zip(IEnumerable<(string Name, string Content)> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content);
                stream.Write(bytes);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>A row, written as pairs so the tests read as data rather than as commas.</summary>
    public static Dictionary<string, string> Row(params (string Column, string Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value);

    /// <summary>Every weekday from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return d;
    }
}
