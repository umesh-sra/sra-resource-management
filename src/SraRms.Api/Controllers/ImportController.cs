using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SraRms.Api.Auth;
using SraRms.Api.Services.Import;

namespace SraRms.Api.Controllers;

/// <summary>
/// Bulk data migration (FR-IMP-1..6). Administrator-only: an import writes
/// across every table, so it carries the same authorization as a delete.
/// </summary>
[Route("v1/import")]
public class ImportController(ResourceGuruImporter importer, ILogger<ImportController> logger)
    : BaseApiController
{
    /// <summary>Largest upload accepted; the nine-month SRA export is about 17 MB.</summary>
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    // POST /import/resource-guru
    [HttpPost("resource-guru")]
    [Authorize(Policy = Policies.Admin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<ImportReportDto>> ResourceGuru(
        IFormFile file, [FromQuery] bool dryRun, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequestProblem("Attach the Resource Guru export as the 'file' part of the form.");

        var name = Path.GetFileName(file.FileName ?? "upload");
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequestProblem($"'{name}' is neither a .zip nor a .csv.");

        // ZipArchive needs to seek the central directory, and IFormFile's stream
        // is only guaranteed seekable while the request is buffered — so copy to
        // a temp file that deletes itself when the handle closes.
        await using var buffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"sra-rms-import-{Guid.NewGuid():N}.tmp"),
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Options = FileOptions.DeleteOnClose | FileOptions.Asynchronous,
            });
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        try
        {
            using var archive = ResourceGuruArchive.Open(buffer, name);
            var report = await importer.RunAsync(archive, dryRun, ct);
            logger.LogInformation(
                "Resource Guru import {Mode} from {File}: {Counts}",
                dryRun ? "previewed" : "committed", name,
                string.Join(", ", report.Entities.Select(e => $"{e.Entity} +{e.Created}")));
            return Ok(report);
        }
        catch (InvalidDataException ex)
        {
            // A malformed or unrecognised upload is the caller's problem, not a 500.
            return BadRequestProblem(ex.Message);
        }
    }
}
