namespace AuditTrailReview.WebApp.Helpers
{
    /// <summary>
    /// Shared time-zone-ID enumeration used by every time zone dropdown in ConfigPanel (the
    /// [C3] working-hours zone on RuleBasedReviewEngine, and the display time zone on
    /// Settings/EditGeneral) -- extracted so both pages normalize the server's time zones to
    /// IANA IDs the same way, rather than duplicating the Windows-ID conversion logic.
    /// </summary>
    public static class TimeZoneHelper
    {
        /// <summary>
        /// Enumerates this server's known time zones as IANA IDs. TimeZoneInfo.GetSystemTimeZones()
        /// returns native Windows IDs (e.g. "Central European Standard Time") when this server runs
        /// on Windows, so each is converted to its IANA equivalent (e.g. "Europe/Warsaw") where one
        /// exists -- IANA is what a browser's Intl.DateTimeFormat().resolvedOptions().timeZone
        /// reports, and what gets stored in the database, so dropdown values must match that format
        /// regardless of the server's own OS.
        /// </summary>
        public static List<string> GetIanaTimeZoneIds()
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            {
                if (tz.HasIanaId)
                {
                    ids.Add(tz.Id);
                }
                else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var ianaId))
                {
                    ids.Add(ianaId);
                }
                // else: no known IANA equivalent for this Windows ID -- skip it rather than show
                // a value the browser-detection script could never match.
            }

            return ids.ToList();
        }
    }
}
