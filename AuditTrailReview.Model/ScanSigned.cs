using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Xml;

namespace AuditTrailReview.Model
{
    public class ScanSignedInfo
    {
        [Key]
        public int Id { get; set; }

        public int ScanID { get; set; }

        public string? UserID { get; set; }

        [Display(Name = "User")]
        public string? UserName { get; set; }

        public DateTime? Timestamp { get; set; }

        public string? Role { get; set; }

        public string? Reason { get; set; }

    }
}

