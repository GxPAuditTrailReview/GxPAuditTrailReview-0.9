using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Persisted claim/completion record for one file discovered on the FTP server (see
    /// AuditTrailReview.IntegrationHub.WorkerService.FTP.FtpFileCheckerService). Replaces the old
    /// "does a Scan row with this ATFileName exist" dedup check, which could not tell a fully
    /// processed file apart from one whose Scan row was saved but whose ScanResult rows never made
    /// it in before a crash -- that file would then be silently treated as "done" forever, with
    /// zero results (see ScanProcessingService.ProcessFileAsync's transaction around both saves,
    /// which now prevents that half-saved state from happening at all).
    ///
    /// One row per (SystemId, FileName). FtpFileCheckerService claims a file -- writes this row as
    /// Processing, saved immediately -- BEFORE downloading or processing it, then flips it to
    /// Completed or Failed once ProcessFileAsync returns. A row still Processing when the Worker
    /// Service starts up can only mean the previous run crashed mid-file: this pipeline is
    /// single-instance and strictly sequential (Worker.cs's polling loop fully awaits every
    /// discovered file before it can start another poll), so nothing else could have left a row in
    /// that state. FtpFileCheckerService.RecoverStuckClaimsAsync resets those rows to Pending on
    /// startup so the interrupted file is retried cleanly, without counting against RetryCount --
    /// the interruption wasn't the file's fault.
    /// </summary>
    public class IngestedFileInfo
    {
        [Key]
        public int Id { get; set; }

        public int SystemId { get; set; }

        // 450 chars, not unbounded: FileName is the second half of a unique (SystemId, FileName)
        // index in the database (see the ATR 0.21 schema script) -- SQL Server does not allow a
        // MAX/LOB column as an index key at all, and 450 unicode chars (900 bytes) is the
        // standard safe bound for an nvarchar index key column.
        [Required]
        [MaxLength(450)]
        public string FileName { get; set; } = string.Empty;

        public IngestedFileStatus Status { get; set; }

        /// <summary>Set once ProcessFileAsync returns successfully for this file.</summary>
        public int? ScanId { get; set; }

        /// <summary>When this file was most recently claimed (Status set to Processing).</summary>
        public DateTime ClaimedAt { get; set; }

        /// <summary>When this file most recently reached Completed or Failed. Null while Pending/Processing.</summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>
        /// How many times this file has been claimed after a genuine failure (an exception from
        /// ProcessFileAsync -- NOT a crash/restart interruption, which resets to Pending without
        /// incrementing this). FtpFileCheckerService stops retrying once this reaches
        /// MaxRetryAttempts, so a genuinely broken file doesn't get re-downloaded and re-attempted
        /// forever on every poll.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>Exception message from the most recent failed attempt, if any.</summary>
        public string? ErrorMessage { get; set; }
    }

    public enum IngestedFileStatus
    {
        /// <summary>Discovered (or recovered after a crash) but not currently being worked on.</summary>
        Pending,
        Processing,
        Completed,
        Failed
    }
}
