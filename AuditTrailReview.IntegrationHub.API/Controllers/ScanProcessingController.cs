using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.IntegrationHub.API.Security;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.IntegrationHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ApiKeyAuthenticationDefaults.PolicyScanWrite)]
public class ScanProcessingController : ControllerBase
{
    /// <summary>ScanUploadJob.FileName's column width.</summary>
    private const int MaxFileNameLength = 450;

    /// <summary>Outer upload ceiling -- mirrors ScanModel's OuterCeilingBytes; see its own remarks.</summary>
    private const long OuterCeilingBytes = 786_432_000;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<ScanProcessingController> _logger;

    public ScanProcessingController(
        ApplicationDbContext context,
        ILogger<ScanProcessingController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // This endpoint only validates and stores: every upload becomes a Tier.Worker ScanUploadJob row
    // (bytes persisted in FileContent) and AuditTrailReview.IntegrationHub.WorkerService's poll
    // loop (ScanUploadJobPoller/ScanUploadJobProcessor) does the actual processing, so no scan work
    // ever runs inside this process and an API restart can't lose a queued file. A Worker Service
    // must therefore be running wherever this API is deployed -- a job stays Queued until one is.
    // Callers get 202 Accepted and poll GetScanStatus below for the result (the Console client
    // does this itself).
    //
    // Cheap checks that would otherwise only surface as a Failed job later (unknown systemId,
    // unsupported extension) are done here so the caller still gets an immediate 400, using the
    // same messages as ScanProcessingService.ProcessFileAsync.
    //
    // The 786,432,000-byte limit mirrors AuditTrailReview.WebApp's Scan.cshtml.cs ScanModel.
    // Without this attribute, the endpoint would fall back to Kestrel's/IIS's own default
    // request-body cap (~30,000,000 bytes). Unlike ScanModel, these attributes work directly on
    // the action method here -- the Razor-Pages-only restriction noted in Scan.cshtml.cs doesn't
    // apply to MVC controllers.
    [HttpPost("scan")]
    [RequestSizeLimit(OuterCeilingBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = OuterCeilingBytes)]
    public async Task<IActionResult> ScanProcessingService(
        IFormFile file,
        [FromQuery] int systemId = 0,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File is required.");
        }

        if (file.FileName.Length > MaxFileNameLength)
        {
            return BadRequest($"File name is too long (maximum {MaxFileNameLength} characters).");
        }

        if (Engine.ScanProcessingService.GetUnsupportedFileTypeMessage(file.FileName) is { } unsupportedMessage)
        {
            return BadRequest(unsupportedMessage);
        }

        // Mirrors AuditTrailReview.WebApp's ScanModel check -- a file between this threshold and
        // the OuterCeilingBytes [RequestSizeLimit] hard backstop above would otherwise still be
        // accepted and queued as a Tier.Worker job that only fails or stalls much later in the
        // Worker Service, instead of getting a clear, actionable 400 here.
        if (Engine.ScanProcessingService.GetFileTooLargeMessage(file.FileName, file.Length) is { } tooLargeMessage)
        {
            return BadRequest(tooLargeMessage);
        }

        try
        {
            if (systemId != 0
                && !await _context.SystemATR.AnyAsync(s => s.Id == systemId, cancellationToken))
            {
                return BadRequest(Engine.ScanProcessingService.SystemNotFoundMessage(systemId));
            }

            return await QueueUploadAsync(file, systemId, cancellationToken);
        }
        catch (SqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Database connection is unavailable. Verify SQL Server/localdb is running and the DefaultConnection string is correct.");
        }
    }

    /// <summary>
    /// Status check for an upload queued by the POST action above -- there is no
    /// signed-in-user/bell-notification concept for an API-key-authenticated caller (see
    /// AuditTrailReview.Model.ScanUploadJob.UserId's remarks), so callers poll this instead.
    /// </summary>
    [HttpGet("scan/{jobId:int}")]
    public async Task<IActionResult> GetScanStatus(int jobId, CancellationToken cancellationToken)
    {
        var job = await _context.ScanUploadJob
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return NotFound();

        return Ok(new
        {
            jobId = job.Id,
            status = job.Status.ToString(),
            scanId = job.ScanId,
            fileName = job.FileName,
            systemId = job.SystemId,
            errorMessage = job.ErrorMessage
        });
    }

    /// <summary>
    /// Persists the upload as a Queued Tier.Worker ScanUploadJob for the Worker Service to pick up --
    /// see AuditTrailReview.Model.ScanUploadJob for the design. The bytes are read into one exactly
    /// sized buffer (no MemoryStream + ToArray() copy, which would double the memory held for a
    /// large file).
    /// </summary>
    private async Task<IActionResult> QueueUploadAsync(IFormFile file, int systemId, CancellationToken cancellationToken)
    {
        var fileContent = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(fileContent, cancellationToken);
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var atFileType = fileExtension == ".json" ? ATFileType.json
                        : fileExtension == ".xml"  ? ATFileType.xml
                        : ATFileType.csv;

        var job = new ScanUploadJob
        {
            SystemId    = systemId,
            FileName    = file.FileName,
            ATFileType  = atFileType,
            Tier        = ScanUploadTier.Worker,
            Status      = ScanUploadStatus.Queued,
            Source      = ScanUploadSource.Api,
            UserId      = null, // no signed-in-user concept for an API-key-authenticated call
            FileContent = fileContent
        };

        _context.ScanUploadJob.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upload '{FileName}' ({Size} bytes) queued as ScanUploadJob #{JobId} for the Worker Service.",
            file.FileName, file.Length, job.Id);

        return Accepted(new
        {
            jobId = job.Id,
            status = "Queued",
            statusUrl = Url.Action(nameof(GetScanStatus), new { jobId = job.Id })
        });
    }
}
