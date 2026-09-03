using System.Globalization;
using System.Text;

namespace SraRms.Api.Services.Import;

/// <summary>
/// Single-pass RFC 4180 CSV reader with header-name field access.
///
/// Hand-rolled rather than taking a CSV library dependency: the Resource Guru
/// exports need only quoted fields (embedded commas and newlines both occur in
/// the Details and Skills columns) plus a case-insensitive header lookup, and
/// the migration endpoint is the only caller. Rows stream, so the 24 MB
/// Utilization sheet never has to be held in memory.
/// </summary>
public sealed class CsvTable
{
    private readonly TextReader _reader;
    private readonly Dictionary<string, int> _columns;

    private CsvTable(TextReader reader, IReadOnlyList<string> header)
    {
        _reader = reader;
        _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++) _columns.TryAdd(header[i].Trim(), i);
    }

    /// <summary>Reads the header row and returns a reader positioned on the first data row.</summary>
    /// <exception cref="InvalidDataException">The stream held no header row.</exception>
    public static CsvTable Open(TextReader reader)
    {
        var header = ReadRecord(reader)
                     ?? throw new InvalidDataException("The CSV file is empty — no header row.");
        return new CsvTable(reader, header);
    }

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    /// <summary>Enumerates the data rows. Single pass: the sequence cannot be re-enumerated.</summary>
    public IEnumerable<CsvRow> Rows()
    {
        while (ReadRecord(_reader) is { } fields)
        {
            // A trailing newline produces one empty field; that is not a row.
            if (fields.Count == 1 && fields[0].Length == 0) continue;
            yield return new CsvRow(_columns, fields);
        }
    }

    /// <summary>
    /// Reads one record, honouring quoted fields. Returns null at end of stream.
    /// CRLF and LF both terminate a record; a bare CR does not (no Resource Guru
    /// export uses classic-Mac line endings).
    /// </summary>
    private static List<string>? ReadRecord(TextReader r)
    {
        if (r.Peek() < 0) return null;

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        while (true)
        {
            var next = r.Read();
            if (next < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            var c = (char)next;

            if (quoted)
            {
                if (c != '"') { field.Append(c); continue; }
                // "" inside a quoted field is a literal quote.
                if (r.Peek() == '"') { r.Read(); field.Append('"'); }
                else quoted = false;
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case ',': fields.Add(field.ToString()); field.Clear(); break;
                case '\r': break; // swallowed; the following \n ends the record
                case '\n': fields.Add(field.ToString()); return fields;
                default: field.Append(c); break;
            }
        }
    }
}

/// <summary>One CSV data row, addressed by header name.</summary>
public readonly struct CsvRow
{
    private readonly Dictionary<string, int> _columns;
    private readonly List<string> _fields;

    internal CsvRow(Dictionary<string, int> columns, List<string> fields)
    {
        _columns = columns;
        _fields = fields;
    }

    /// <summary>The raw field, or "" when the column is absent or the row is short.</summary>
    public string this[string column] =>
        _columns.TryGetValue(column, out var i) && i < _fields.Count ? _fields[i] : "";

    /// <summary>The trimmed field, or null when blank.</summary>
    public string? Text(string column)
    {
        var v = this[column].Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>The field as a decimal; 0 when blank or unparseable.</summary>
    public decimal Number(string column) =>
        decimal.TryParse(this[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    /// <summary>An ISO (yyyy-MM-dd) date field, or null when blank or unparseable.</summary>
    public DateOnly? IsoDate(string column) =>
        DateOnly.TryParseExact(this[column].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? d
            : null;

    /// <summary>
    /// A day-first date field (Resource Guru writes the person-profile dates as
    /// d/M/yyyy, unlike the ISO dates in the fact sheets), or null.
    /// </summary>
    public DateOnly? DayFirstDate(string column)
    {
        var v = this[column].Trim();
        string[] formats = ["d/M/yyyy", "d/M/yy", "yyyy-MM-dd"];
        return DateOnly.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    /// <summary>
    /// A comma-separated multi-value field (skills, clearances, certifications)
    /// split into trimmed, de-duplicated, order-preserving values.
    /// </summary>
    public List<string> CsvList(string column)
    {
        var raw = this[column];
        if (raw.Trim().Length == 0) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var v = part.Trim();
            if (v.Length > 0 && seen.Add(v)) list.Add(v);
        }
        return list;
    }
}
