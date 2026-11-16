using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace AuditTrailReview.Model
{
    public class RuleCategory
    {
        public int Id { get; set; }

        public string? Code { get; set; }   // A, B, C...
        public string? Name { get; set; }

        public ICollection<Rule>? Rules { get; set; }
    }

}
