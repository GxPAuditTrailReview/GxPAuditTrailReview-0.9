using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Xml;

namespace AuditTrailReview.Model
{
    public class ScanResultAIInfo
    {

        [Key]
        public int Id { get; set; }

        public int ScanID { get; set; }

        public string? Insight { get; set; }

        public string? Recommendation { get; set; }
    }
}
