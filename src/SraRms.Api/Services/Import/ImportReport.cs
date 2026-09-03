namespace SraRms.Api.Services.Import;

/// <summary>
/// The outcome of one import run (OpenAPI <c>ImportReport</c>). A dry run
/// produces the same shape as a committed run, so the SPA can preview an
/// import and then commit it without re-interpreting the response.
/// </summary>
public record ImportReportDto
{
    /// <summary>True when the transaction was rolled back and nothing was written.</summary>
    public bool DryRun { get; init; }

    /// <summary>Files found in the upload, whether or not they were read.</summary>
    public IReadOnlyList<string> SourceFiles { get; init; } = [];

    /// <summary>Sheets that were recognised and parsed, e.g. "bookings".</summary>
    public IReadOnlyList<string> SheetsRead { get; init; } = [];

    /// <summary>Rows read per sheet, before grouping.</summary>
    public IReadOnlyList<ImportSourceCountDto> SourceRows { get; init; } = [];

    /// <summary>What the run did, per target entity.</summary>
    public IReadOnlyList<ImportEntityCountDto> Entities { get; init; } = [];

    /// <summary>Non-fatal issues, aggregated by kind so 800 rows do not yield 800 messages.</summary>
    public IReadOnlyList<ImportIssueDto> Warnings { get; init; } = [];

    /// <summary>Source columns SRA-RMS has no home for, so the loss is explicit.</summary>
    public IReadOnlyList<ImportUnmappedFieldDto> UnmappedFields { get; init; } = [];

    public int DurationMs { get; init; }
}

/// <summary>Rows parsed from one source sheet.</summary>
public record ImportSourceCountDto(string Sheet, int Rows);

/// <summary>
/// Per-entity outcome. <paramref name="Skipped"/> counts records that already
/// existed (matched on their natural key) and were left untouched.
/// </summary>
public record ImportEntityCountDto(string Entity, int Created, int Updated, int Skipped);

/// <summary>An aggregated warning: <paramref name="Count"/> source rows or records were affected.</summary>
public record ImportIssueDto(string Code, string Message, int Count, IReadOnlyList<string> Examples);

/// <summary>A source column that is deliberately not imported.</summary>
public record ImportUnmappedFieldDto(string Sheet, string Field, string Reason, int NonEmptyRows);
