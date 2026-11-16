using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    public class UserActivityLog
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "User ID")]
        public string? UserId { get; set; }

        [Display(Name = "User")]
        public string? UserName { get; set; }

        [Display(Name = "Action")]
        public string? Action { get; set; }          // e.g. "Login", "Logout", "DeleteSystemType"

        [Display(Name = "IP address")]
        public string? IpAddress { get; set; }

        [Display(Name = "User agent")]
        public string? UserAgent { get; set; }

        [Display(Name = "Details")]
        public string? Details { get; set; }         // optional extra context

        [Display(Name = "Timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Display(Name = "Success")]
        public bool Success { get; set; } = true;
    }
}