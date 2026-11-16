using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Singleton configuration row (always Id = 1) that drives configurable rule thresholds.
    ///
    /// Six fields that only ever backed already-retired rules -- ReasonMinLength (old A4),
    /// ReasonMinAlphabeticChars (old A5), AdditionalPersonalNames (old D1), and
    /// BoilerplateReasonMinRowCount/BoilerplateReasonThresholdPercent/TimeGapThresholdDays (old
    /// A8/A7 and C4, kept after those removals per Program.cs's Round 2/3/4 seeding history) --
    /// were fully removed in a sixth rule-management round, at the user's explicit instruction,
    /// rather than continuing to be "retained for backward compatibility": nothing reads them,
    /// and the Engine Settings tab cards and their save-validation were deleted with them.
    /// Five of the six matching database columns were dropped by
    /// AuditTrailReview.Database/Database schema/ATR 0.19 (see that script's header for the
    /// column-level detail, including two of the three columns never having been captured in a
    /// schema script at all); ReasonMinAlphabeticChars itself survived in the live schema past
    /// that round and was dropped separately by
    /// AuditTrailReview.Database/Database schema/ATR 0.20 20261026 - EngineSettings Drop
    /// ReasonMinAlphabeticChars.sql. See Audit-Trail-Rule-Regulatory-Basis-Review.md and
    /// Program.cs's seeding comments for the fuller history.
    /// </summary>
    public class EngineSettings
    {
        [Key]
        public int Id { get; set; }

        // [C3] Normal working day start hour (0-23, inclusive), expressed in WorkdayTimeZone
        //      below -- not in the source data's own declared time zone, and not necessarily
        //      this server's time zone either. Timestamps are normalized to UTC and then
        //      converted into WorkdayTimeZone before this value is compared.
        public int WorkdayStartHour { get; set; } = 7;

        // [C3] Normal working day end hour (1-24, exclusive upper bound), expressed in
        //      WorkdayTimeZone below. Set to 24 together with start = 0 for a full 24-hour
        //      working day.
        public int WorkdayEndHour { get; set; } = 18;

        // [C3] When true, weekends (in WorkdayTimeZone below) are treated as normal working
        //      days (weekend records are not flagged).
        public bool AllowWeekends { get; set; } = false;

        // [C3] IANA time zone ID (e.g. "Europe/Warsaw") that WorkdayStartHour/WorkdayEndHour and
        //      the weekend check above are evaluated in. Timestamps are still normalized to UTC
        //      internally regardless of this setting -- this only controls which "wall clock"
        //      WorkdayStartHour/WorkdayEndHour are read against. Null/empty (the default, so an
        //      existing installation's behavior is unchanged until an admin opts in) or an ID
        //      that no longer resolves on this server falls back to UTC rather than failing a
        //      scan -- see ScanResultService's Rule 17 (C3).
        public string? WorkdayTimeZone { get; set; }

        // [D2] Minimum number of characters required in the Username field. (Was D2
        //      originally, D1 after a fourth rule-management round, D2 again after a sixth
        //      round swapped D1/D2 back.)
        public int UsernameMinLength { get; set; } = 3;

        // [D1] Comma-separated or newline-separated list of additional login patterns to flag
        //      as a shared/generic/system account, on top of
        //      SystemAccountHeuristics.IsLikelySystemAccount's built-in name/prefix lists.
        //      Case-insensitive. A pattern ending in '*' matches as a prefix against the whole
        //      login (e.g. "contractor*" matches "contractor_jdoe"); anything else matches only
        //      an exact login. Useful for organization-specific account-naming conventions the
        //      built-in heuristic can't anticipate.
        public string? AdditionalSystemAccountPatterns { get; set; }

        // [E1] Number of records within MassChangeWindowSeconds that constitutes a "mass
        //      change" burst (the record count is inclusive -- a burst of exactly this many
        //      records is flagged, not just anything above it).
        public int MassChangeThresholdCount { get; set; } = 10;

        // [E1] Sliding time window, in seconds, that MassChangeThresholdCount is
        //      evaluated over: MassChangeThresholdCount or more records whose timestamps all
        //      fall within this many seconds of each other are flagged.
        public double MassChangeWindowSeconds { get; set; } = 1.0;

        // Global opt-out for the AI-assisted analysis step queued after a scan's rule-based
        // results are saved (see ADR-0006, NFR-014, FR-035/FR-037). Checked by every ingestion
        // path -- Pages/Scan/Scan.cshtml.cs's own queuing block for the manual-upload path, and
        // ScanProcessingService.ProcessFileAsync (shared by the Worker Service and the API) --
        // so a scan gets the same AI analysis behavior regardless of how it was ingested.
        // Defaults to true so an existing installation's behavior is unchanged until an admin
        // explicitly opts out via AdminPanel/Settings' AI Analysis tab.
        public bool AiAnalysisEnabled { get; set; } = true;
    }
}
