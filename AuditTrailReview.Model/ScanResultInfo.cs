using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Xml;

namespace AuditTrailReview.Model
{
    public class ScanResultInfo
    {
        [Key]
        public int Id { get; set; }

        public int ScanID { get; set; }

        [Display(Name = "UI")]
        public string? UniqueIdentifier { get; set; }

        public DateTime? Timestamp { get; set; }

        [Display(Name = "User ID")]
        public string? UserID { get; set; }

        [Display(Name = "Object type")]
        public string? ObjectType { get; set; }

        public string? Object { get; set; }

        public string? Field { get; set; }

        public string? Source { get; set; }

        public string? Operation { get; set; }

        [Display(Name = "Old value")]
        public string? OldValue { get; set; }

        [Display(Name = "New value")]
        public string? NewValue { get; set; }

        public string? Reason { get; set; }

        public string? Finding { get; set; }

        /// <summary>
        /// Mirrors <c>!string.IsNullOrEmpty(Finding)</c> -- set once by
        /// AuditTrailReview.Engine.ScanResultService when a row is created, not derived by a
        /// query. Backs a filtered index (see ATR 0.24's schema script) so counting/filtering
        /// "rows that have a finding" doesn't need a full scan of the <see cref="Finding"/>
        /// column, which SQL Server can't index directly since it's nvarchar(max).
        /// </summary>
        public bool HasFinding { get; set; }

        // --- Review fields ---
        [Display(Name = "Review status")]
        public FindingReviewStatus? ReviewStatus { get; set; }

        [Display(Name = "Review comment")]
        public string? ReviewComment { get; set; }

        [Display(Name = "Reviewed by")]
        public string? ReviewedBy { get; set; }

        [Display(Name = "Reviewed at")]
        public DateTime? ReviewedAt { get; set; }
    }
}

