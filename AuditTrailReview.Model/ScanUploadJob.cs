using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Tracks one large scan upload that couldn't run synchronously inside its own HTTP request --
    /// modeled directly on IngestedFileInfo's proven claim-before-work shape (see that class's own
    /// remarks), extended with what this feature specifically needs. Created by
    /// AuditTrailReview.WebApp.Pages.Scan.ScanModel the moment an upload exceeds the synchronous
    /// size threshold (currently 10,000,000 bytes -- see Scan.cshtml.cs), and by
    /// AuditTrailReview.IntegrationHub.API's ScanProcessingController for EVERY upload (always
    /// Tier.Worker, Source = Api): the API only validates and stores, the Worker Service processes.
    ///
    /// Two tiers, both ending up as rows in this one table:
    ///  - Tier.Immediate (WebApp only): queued on the SAME process's IBackgroundTaskQueue (the exact
    ///    mechanism already used for AI analysis) -- for uploads up to 50,000,000 bytes. Moves
    ///    Queued -> Processing almost instantly.
    ///  - Tier.Worker: FileContent is durably persisted here and AuditTrailReview.IntegrationHub.
    ///    WorkerService polls for Queued rows (AuditTrailReview.Engine.ScanUploadJobProcessor) --
    ///    for uploads up to the outer ceiling (786,432,000 bytes / 750 MiB). Deliberately does NOT route
    ///    through the existing external-FTP ingestion path (FtpFileCheckerService): that path ties
    ///    every file to one fixed Ftp:SystemId per Worker Service instance, but a web/API upload
    ///    carries its own arbitrary SystemId, so a DB-backed queue is used instead -- no new FTP
    ///    client code, no shared FTP credentials, and no fight with that single-system design.
    ///
    /// Both tiers ultimately call the exact same, unchanged AuditTrailReview.Engine.
    /// ScanProcessingService.ProcessFileAsync the FTP path already uses -- this table only decides
    /// WHERE and WHEN that call happens, never how the file itself is scanned.
    /// </summary>
    public class ScanUploadJob
    {
        [Key]
        public int Id { get; set; }

        public int SystemId { get; set; }

        [Required]
        [MaxLength(450)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>Needed up front, before processing -- the final ScanInfo (and its own
        /// ATFileType) isn't created until ProcessFileAsync actually runs.</summary>
        public ATFileType ATFileType { get; set; }

        public ScanUploadTier Tier { get; set; }

        public ScanUploadStatus Status { get; set; }

        public ScanUploadSource Source { get; set; }

        /// <summary>ASP.NET Identity user id (populated via UserManager.GetUserId(User), same
        /// pattern ScanSignedInfo.UserID already uses), for WebApp uploads only. Always null for
        /// Source = Api -- there's no signed-in-user concept over an API-key-authenticated call,
        /// so the bell/notification story (Pages/Notifications) is WebApp-only; API callers poll
        /// GET /api/ScanProcessing/scan/{jobId} instead.</summary>
        [MaxLength(450)]
        public string? UserId { get; set; }

        /// <summary>The uploaded bytes. Nulled out once ProcessFileAsync has consumed them,
        /// success or failure, so this table never accumulates old blobs long-term -- only small
        /// metadata rows persist. Populated immediately for Tier.Worker (needs to survive until the
        /// Worker Service's next poll); for Tier.Immediate it's typically cleared within moments of
        /// being written, since the in-process queue picks it up almost immediately.</summary>
        public byte[]? FileContent { get; set; }

        /// <summary>Set once ProcessFileAsync returns successfully for this job.</summary>
        public int? ScanId { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>Mirrors IngestedFileInfo.RetryCount/MaxRetryAttempts -- a genuinely broken file
        /// (bad encoding, unsupported content) shouldn't be retried forever by the Worker Service's
        /// poll. Not used for Tier.Immediate, which never retries (the bytes only ever live in
        /// memory for one attempt -- see ScanUploadJobProcessor's remarks).</summary>
        public int RetryCount { get; set; }

        public DateTime? ClaimedAtUtc { get; set; }

        public DateTime? FinishedAtUtc { get; set; }

        /// <summary>WebApp-only: when the uploader dismissed/saw the bell notification for this
        /// job's completion (Pages/Notifications). Always null for Source = Api rows.</summary>
        public DateTime? AcknowledgedAtUtc { get; set; }
    }

    /// <summary>Which mechanism processes a ScanUploadJob row -- see ScanUploadJob's own remarks.</summary>
    public enum ScanUploadTier
    {
        Immediate,
        Worker
    }

    /// <summary>Same Queued/Processing/Completed/Failed shape as IngestedFileStatus, minus that
    /// enum's Pending state -- a ScanUploadJob is always created already Queued, there's no
    /// separate "discovered but not yet claimed" state to distinguish.</summary>
    public enum ScanUploadStatus
    {
        Queued,
        Processing,
        Completed,
        Failed
    }

    public enum ScanUploadSource
    {
        WebApp,
        Api
    }
}
