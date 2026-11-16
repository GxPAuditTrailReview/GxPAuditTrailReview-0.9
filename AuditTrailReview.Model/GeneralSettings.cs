using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Singleton configuration row (always Id = 1) controlling how every UTC date/time stored in
    /// the system is displayed. Independent of EngineSettings.WorkdayTimeZone, which only
    /// controls the [C3] working-hours rule's evaluation -- DisplayTimeZone here is purely a
    /// presentation preference and has no effect on rule evaluation. See
    /// AuditTrailReview.WebApp.Services.IDateTimeDisplayService, the single place both fields are
    /// read for display.
    /// </summary>
    public class GeneralSettings
    {
        [Key]
        public int Id { get; set; }

        // One of DateTimeFormatOption's fixed .NET custom format strings (see that class) --
        // validated against that list on save (AdminPanel/Settings/EditGeneral), never free
        // text, so a stray value here can never produce a FormatException at display time.
        public string DateTimeFormat { get; set; } = "dd-MMM-yy HH:mm";

        // IANA time zone ID (e.g. "America/New_York") that every displayed date/time is
        // converted into. Null/empty (the default) or an ID that no longer resolves on this
        // server falls back to UTC rather than failing a page render -- same convention as
        // EngineSettings.WorkdayTimeZone.
        public string? DisplayTimeZone { get; set; }
    }
}
