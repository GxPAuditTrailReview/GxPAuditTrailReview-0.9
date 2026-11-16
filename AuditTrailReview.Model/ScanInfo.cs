using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace AuditTrailReview.Model
{
    public class ScanInfo
    {
        [Key]
        public int Id { get; set; }
        
        public int SystemID { get; set; }

        [Display(Name = "Scan datetime")]
        public DateTime? ScanDate { get; set; }

        [Display(Name = "File name")]
        public string? ATFileName { get; set; }

        [Display(Name = "File type")]
        public ATFileType ATFileType { get; set; }

        public bool ScanClosed { get; set; }

        /// <summary>ASP.NET Identity user id of the signed-in WebApp user who uploaded this
        /// scan (same convention as ScanUploadJob.UserId). NULL for scans ingested via FTP or
        /// the Integration Hub API -- neither has a signed-in-user concept.</summary>
        [MaxLength(450)]
        public string? UserId { get; set; }

        // Navigation property to SystemATRInfo
        [ForeignKey("SystemID")]
        public SystemATRInfo? System { get; set; }
    }

    public enum ATFileType
    {
        [Display(Name = "CSV")]
        csv = 0,
        
        [Display(Name = "XML")]
        xml = 1,
        
        [Display(Name = "JSON")]
        json = 2
    }
}
