using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Holds the original bytes of the file that produced a Scan, so it can be downloaded again
    /// from Pages/Scan/Details.cshtml. One row per Scan, written inside the same save/transaction
    /// that creates the Scan row (see ScanProcessingService.ProcessFileAsync and
    /// Pages/Scan/Scan.cshtml.cs's own synchronous-upload path), so a Scan never exists without
    /// its ScanFile or vice versa.
    ///
    /// Deliberately a separate table rather than a column on ScanInfo -- keeps the hot, frequently
    /// queried Scan table free of a potentially large column, same reasoning as ScanUploadJob's own
    /// FileContent. Unlike ScanUploadJob.FileContent (nulled out once
    /// consumed), this row is permanent: every scan's original file is kept indefinitely, by
    /// product decision, as an audit-integrity record of what was actually reviewed. Scans
    /// processed before this feature existed simply have no matching ScanFile row -- Details.cshtml
    /// degrades to plain text for those instead of a dead link.
    ///
    /// Read back via AuditTrailReview.Engine.ScanFileReader, never through normal EF entity
    /// tracking -- same reasoning as ScanUploadJobProcessor's remarks on FileContent: loading a
    /// large blob that way can peg the CPU for tens of minutes.
    /// </summary>
    public class ScanFile
    {
        [Key]
        public int Id { get; set; }

        /// <summary>References Scan.Id by convention only, same as ScanUploadJob.ScanId -- this
        /// codebase does not add DB-level FOREIGN KEY constraints for Scan's child tables.</summary>
        public int ScanId { get; set; }

        /// <summary>The original uploaded file, unmodified. Content type for download is derived
        /// from the owning Scan's ATFileType rather than stored here.</summary>
        public byte[]? Content { get; set; }

        public DateTime UploadedAtUtc { get; set; }
    }
}
