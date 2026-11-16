using System;
using System.Collections.Generic;

namespace AuditTrailReview.Engine
{
    /// <summary>
    /// Heuristically determines whether a login looks like a shared/generic/system account
    /// rather than an individual's own login, for rule D1 (originally D3, then D2 after a
    /// fourth rule-management round, then D1 again after a sixth round swapped D1/D2 back).
    /// Deliberately independent of <see cref="LoginNameHeuristics"/>: that class detects a
    /// login that looks like a typed personal name (backed the ORIGINAL rule D1 -- a
    /// different, unrelated rule that was removed in the fourth round; the heuristic is now
    /// used only by Pages/Scan/Preview.cshtml.cs's own Excel export), while this one detects a
    /// login that looks like a known non-personal account -- not simply the logical negation of the other
    /// -- most real individual logins (e.g. "jsmith", "j.kowalski2") are neither a personal-name
    /// shape nor a recognized system-account pattern, and both heuristics can validly stay
    /// silent on the same login.
    /// </summary>
    public static class SystemAccountHeuristics
    {
        // Exact-match (case-insensitive) login values that are well-known generic/shared/
        // placeholder account names across virtually any system, regardless of the audited
        // system's own naming convention or locale. Deliberately overlaps with
        // LoginNameHeuristics.KnownSystemWords (both describe the same category of
        // non-personal placeholder word), but is kept as its own list here rather than shared
        // directly, since the two rules are free to evolve independently (e.g. a word worth
        // excluding from "looks like a name" isn't necessarily one worth flagging as "looks
        // like a shared account", and vice versa).
        private static readonly HashSet<string> KnownAccountNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "admin", "administrator", "system", "sa", "root", "service", "support",
                "operator", "guest", "anonymous", "unknown", "default", "test", "demo",
                "sample", "null", "none", "n/a", "na", "superuser", "poweruser", "help", "info"
            };

        // Case-insensitive prefixes that are a common naming convention for service/system/
        // batch/automation accounts (e.g. "svc-integration", "sys_batchjob01", "bot.reindex").
        // Matched via StartsWithKnownPrefix below, which requires the prefix to be the whole
        // login or be immediately followed by a separator/digit -- see that method for why.
        private static readonly string[] KnownAccountPrefixes =
            { "svc", "sys", "system", "batch", "auto", "bot", "api", "job", "cron", "daemon" };

        /// <summary>
        /// True when <paramref name="login"/> matches a known generic/shared/system account
        /// name or naming convention -- either the built-in lists above, or one of
        /// <paramref name="additionalPatterns"/> (typically <see
        /// cref="AuditTrailReview.Model.EngineSettings.AdditionalSystemAccountPatterns"/>).
        /// Each additional pattern is matched case-insensitively, either as an exact login
        /// match, or -- if the pattern ends with '*' -- as a prefix match against the whole
        /// remainder of the login (unlike the stricter separator/digit guard the built-in
        /// prefixes use, so an admin can cover a convention this heuristic's own built-in list
        /// doesn't anticipate, e.g. "testuser*" for an organization whose test accounts are
        /// never separator-delimited).
        /// </summary>
        public static bool IsLikelySystemAccount(string login, IEnumerable<string>? additionalPatterns = null)
        {
            if (string.IsNullOrWhiteSpace(login))
                return false;

            login = login.Trim();

            if (KnownAccountNames.Contains(login))
                return true;

            foreach (var prefix in KnownAccountPrefixes)
            {
                if (StartsWithKnownPrefix(login, prefix))
                    return true;
            }

            if (additionalPatterns != null)
            {
                foreach (var pattern in additionalPatterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                        continue;

                    var trimmed = pattern.Trim();

                    if (trimmed.EndsWith('*'))
                    {
                        var prefix = trimmed[..^1];
                        if (prefix.Length > 0 && login.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    else if (string.Equals(login, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="login"/> equals <paramref name="prefix"/> exactly, or
        /// starts with it followed immediately by a separator ('-', '_', '.') or a digit --
        /// the conventional way service-account names are built ("svc-x", "svc_x", "svc.x",
        /// "svc01"). Without this guard, a built-in prefix like "test" or "system" would also
        /// match ordinary words/names that merely happen to start with the same letters (e.g.
        /// "Testimony", "Systema"), which the exact-match list and the wildcard-based
        /// AdditionalSystemAccountPatterns escape hatch handle instead when that's genuinely
        /// what an organization needs to catch.
        /// </summary>
        private static bool StartsWithKnownPrefix(string login, string prefix)
        {
            if (!login.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (login.Length == prefix.Length)
                return true;

            var next = login[prefix.Length];
            return next is '-' or '_' or '.' || char.IsDigit(next);
        }
    }
}
