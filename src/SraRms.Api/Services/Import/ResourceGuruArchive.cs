using System.IO.Compression;
using System.Text;

namespace SraRms.Api.Services.Import;

/// <summary>The logical sheets of a Resource Guru report export.</summary>
public enum RgSheet
{
    /// <summary>The person master list — one row per resource.</summary>
    Resources,

    /// <summary>One row per resource, per project, per day booked.</summary>
    Bookings,

    /// <summary>Leave and public holidays: one row per resource per day.</summary>
    Downtime,

    /// <summary>Bookable hours: one row per resource per day.</summary>
    Availability,

    /// <summary>Per-project daily scheduled totals; carries the project and client ids.</summary>
    ScheduledVsActuals,

    /// <summary>Derived utilisation percentages — not imported.</summary>
    Utilization,

    /// <summary>Timesheet actuals — not imported (SRA-RMS does not model actuals).</summary>
    Timesheets,
}

/// <summary>
/// A Resource Guru export opened for reading. Accepts either the report .zip as
/// downloaded from Resource Guru, or a single extracted .csv, and resolves the
/// sheets by the filename fragments Resource Guru uses ("... Bookings Data
/// 1 Jan - 30 Sep 2026.csv"), so the date range in the name does not matter.
/// </summary>
public sealed class ResourceGuruArchive : IDisposable
{
    /// <summary>Refuse an archive whose entries would decompress beyond this (zip-bomb guard).</summary>
    private const long MaxUncompressedBytes = 400L * 1024 * 1024;

    // Ordered longest-fragment-first so "Scheduled Vs Actuals" is not shadowed.
    private static readonly (string Fragment, RgSheet Sheet)[] Fragments =
    [
        ("Scheduled Vs Actuals", RgSheet.ScheduledVsActuals),
        ("Availability Data", RgSheet.Availability),
        ("Timesheets Data", RgSheet.Timesheets),
        ("Utilization Data", RgSheet.Utilization),
        ("Utilisation Data", RgSheet.Utilization),
        ("Resource Data", RgSheet.Resources),
        ("Bookings Data", RgSheet.Bookings),
        ("Downtime Data", RgSheet.Downtime),
    ];

    private readonly ZipArchive? _zip;
    private readonly Dictionary<RgSheet, ZipArchiveEntry> _entries = new();
    private readonly Stream? _single;
    private readonly RgSheet? _singleSheet;

    /// <summary>Names of the files the archive contained, in archive order.</summary>
    public IReadOnlyList<string> FileNames { get; }

    /// <summary>The sheets that were recognised and can be read.</summary>
    public IReadOnlyCollection<RgSheet> Sheets =>
        _singleSheet is { } s ? [s] : _entries.Keys;

    private ResourceGuruArchive(ZipArchive zip)
    {
        _zip = zip;
        var names = new List<string>();
        foreach (var entry in zip.Entries)
        {
            names.Add(entry.FullName);
            if (Classify(entry.FullName) is { } sheet) _entries.TryAdd(sheet, entry);
        }
        FileNames = names;
    }

    private ResourceGuruArchive(Stream csv, string fileName, RgSheet sheet)
    {
        _single = csv;
        _singleSheet = sheet;
        FileNames = [fileName];
    }

    /// <summary>
    /// Opens an uploaded export. <paramref name="content"/> must be seekable and
    /// is not disposed by this instance's owner until the archive is disposed.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The upload is neither a readable zip nor a recognisable single sheet.
    /// </exception>
    public static ResourceGuruArchive Open(Stream content, string fileName)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var sheet = Classify(fileName)
                        ?? throw new InvalidDataException(
                            $"'{fileName}' is not a recognised Resource Guru sheet. Expected a filename "
                            + "containing \"Resource Data\", \"Bookings Data\", \"Downtime Data\", "
                            + "\"Availability Data\" or \"Scheduled Vs Actuals\".");
            return new ResourceGuruArchive(content, fileName, sheet);
        }

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                "The upload is not a readable .zip archive. Upload the Resource Guru report .zip "
                + "as downloaded, or a single exported .csv.");
        }

        var total = zip.Entries.Sum(e => Math.Max(0, e.Length));
        if (total > MaxUncompressedBytes)
        {
            zip.Dispose();
            throw new InvalidDataException(
                $"The archive would expand to {total / 1024 / 1024} MB, beyond the "
                + $"{MaxUncompressedBytes / 1024 / 1024} MB import limit.");
        }

        var archive = new ResourceGuruArchive(zip);
        if (archive._entries.Count == 0)
        {
            archive.Dispose();
            throw new InvalidDataException(
                "The archive contains no recognised Resource Guru sheets. Expected files named "
                + "\"... Resource Data ....csv\", \"... Bookings Data ....csv\" and so on.");
        }
        return archive;
    }

    /// <summary>
    /// Opens a recognised sheet for reading, or returns null when the export did
    /// not include it. Each call returns a fresh reader.
    /// </summary>
    public CsvTable? Open(RgSheet sheet)
    {
        Stream stream;
        if (_singleSheet is { } only)
        {
            if (only != sheet) return null;
            _single!.Position = 0;
            stream = _single;
        }
        else if (_entries.TryGetValue(sheet, out var entry))
        {
            stream = entry.Open();
        }
        else
        {
            return null;
        }

        // detectEncodingFromByteOrderMarks strips the UTF-8 BOM Resource Guru writes.
        var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024, leaveOpen: stream == _single);
        return CsvTable.Open(reader);
    }

    /// <summary>The sheet a filename denotes, or null when it is not one we read.</summary>
    private static RgSheet? Classify(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var (fragment, sheet) in Fragments)
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return sheet;
        return null;
    }

    public void Dispose() => _zip?.Dispose();
}
