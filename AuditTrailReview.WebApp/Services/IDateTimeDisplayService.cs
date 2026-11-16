namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Converts a stored UTC DateTime into the admin-configured GeneralSettings.DisplayTimeZone
    /// and formats it with GeneralSettings.DateTimeFormat -- the single place every page/export
    /// in the app turns a UTC timestamp into display text. Supersedes the old
    /// AuditTrailReview.WebApp.Helpers.DisplayTimeZone, which only converted (never formatted)
    /// and only ever used EngineSettings.WorkdayTimeZone (a different, rule-evaluation-only
    /// setting -- see GeneralSettings.cs's remarks).
    /// </summary>
    public interface IDateTimeDisplayService
    {
        Task<string> FormatAsync(DateTime? utc);

        Task<string> FormatAsync(DateTime utc);

        /// <summary>
        /// Drops the cached GeneralSettings row so the very next FormatAsync call re-reads the
        /// database -- called by AdminPanel/Settings/EditGeneral after a save, so a changed
        /// format/time zone takes effect immediately rather than after the cache TTL expires.
        /// </summary>
        void InvalidateCache();
    }
}
