using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AuditTrailReview.Model
{
    public class SystemTypeInfo
    {

        [Key]
        [Display(Name = "System type ID")]
        public int Id { get; set; }

        [Display(Name = "System type")]
        public string? SystemType { get; set; }
    }
}
