using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AuditTrailReview.Model
{
    public class SystemATRInfo
    {

        [Key]
        public int Id { get; set; }


        [Display(Name = "System ID")]
        [Required(ErrorMessage = "System ID is required")]
        [MinLength(3, ErrorMessage = "System ID must be at least {1} characters long")]
        public string? SystemPrefixID { get; set; }

        [Display(Name = "Name")]
        [Required(ErrorMessage = "System name is required")]
        [MinLength(5, ErrorMessage = "System name must be at least {1} characters long")]
        public string? SystemName { get; set; }

        [Display(Name = "System type")]
        public int? SystemTypeID { get; set; }
    }
}
