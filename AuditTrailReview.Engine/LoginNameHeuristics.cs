using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AuditTrailReview.Engine
{
    public static class LoginNameHeuristics
    {
        // A short, curated set of common IT/ops role and placeholder words that would otherwise
        // pass the structural "single Title-Case word" check below (e.g. "Admin", "Guest",
        // "System" are all single alphabetic Title-Case words). Unlike the personal-name
        // dictionary this class used to rely on, this list only needs to name categories of
        // role/placeholder accounts -- not the unbounded set of real personal names in every
        // locale -- so it stays small, is near-universal across organizations regardless of the
        // audited system's own language (these are common English IT terms), and doesn't need
        // the ongoing maintenance a name dictionary would.
        private static readonly HashSet<string> KnownSystemWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "admin", "administrator", "system", "root", "sa", "superuser", "poweruser",
                "guest", "anonymous", "unknown", "default", "test", "demo", "sample",
                "service", "support", "operator", "help", "info", "null", "none", "user"
            };

        // One name-shaped "word": Unicode letters only (\p{L} -- works for any script/locale,
        // not just ASCII+Polish), with an optional internal hyphen or apostrophe joining further
        // letter runs -- covers compound names ("Anna-Maria") and names like "O'Brien" without
        // allowing a leading/trailing/doubled separator. Digits, dots, underscores, "@", and "\"
        // -- all common in real login IDs ("jsmith2", "first.last", "svc_batch",
        // "DOMAIN\jsmith") -- are deliberately excluded by omission from the character class,
        // since their presence is itself a strong "this is a formatted login, not a typed name"
        // signal.
        private static readonly Regex NameShapedWordRegex = new(
            @"^\p{L}+(?:['-]\p{L}+)*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Heuristically determines whether <paramref name="login"/> looks like a typed personal
        /// name rather than a system login, using structural shape alone -- no personal-name
        /// dictionary. Two independent signals, either of which is sufficient:
        ///
        /// 1. Multiple space-separated name-shaped words (e.g. "John Smith", "Anna Kowalska") --
        ///    a login essentially never contains a space, but someone typing a person's name
        ///    freeform does. (The old dictionary-only version explicitly rejected anything
        ///    containing a space, so it could never catch this case at all.)
        ///
        /// 2. A single name-shaped word in Title Case (first letter uppercase, the rest
        ///    lowercase within each hyphen/apostrophe-separated segment -- e.g. "Adrian",
        ///    "Anna-Maria", "O'Brien") that isn't a known system/role word (<see
        ///    cref="KnownSystemWords"/>). Title Case is the discriminator: real login
        ///    conventions are essentially never cased this way (they're typically all-lowercase,
        ///    ALL-CAPS, or contain digits/separators), so a bare word capitalized like a proper
        ///    noun is a meaningful signal on its own -- unlike an all-lowercase word, which is
        ///    structurally indistinguishable from any other short lowercase login/word without a
        ///    dictionary.
        ///
        /// Trade-off vs. the old dictionary lookup, by design: a lowercase-typed name (e.g.
        /// "adam") is no longer caught, in exchange for working on any name in any Latin/
        /// Cyrillic/Greek-script locale the old fixed 21-word PL/EN list never covered, and for
        /// catching full "Firstname Lastname" logins the old approach couldn't. Caseless scripts
        /// (CJK, Thai, Arabic, Hebrew, ...) have no upper/lower distinction at all, so a single
        /// bare word in one of those scripts won't trigger signal 2 either -- signal 1
        /// (space-separated words) is what still catches those when written with a space.
        /// </summary>
        public static bool LooksLikePersonalName(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return false;

            login = login.Trim();

            if (login.Contains(' '))
            {
                var words = login.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return words.Length is >= 2 and <= 4 && words.All(IsNameShapedWord);
            }

            return login.Length is >= 2 and <= 30
                && IsNameShapedWord(login)
                && IsTitleCase(login)
                && !KnownSystemWords.Contains(login);
        }

        private static bool IsNameShapedWord(string word) => NameShapedWordRegex.IsMatch(word);

        /// <summary>
        /// True when every hyphen/apostrophe-separated segment of <paramref name="word"/> starts
        /// with an uppercase letter and has every subsequent letter lowercase -- e.g. "Adrian",
        /// "Anna-Maria", and "O'Brien" all pass; "adam", "ADAM", "AdAm", and "McDonald" do not
        /// (the "Mc"/"Mac" mid-word-capital naming convention is a known, accepted gap here --
        /// out of scope for this level of heuristic).
        /// </summary>
        private static bool IsTitleCase(string word)
        {
            foreach (var segment in word.Split('-', '\''))
            {
                if (segment.Length == 0)
                    return false; // leading/trailing/doubled separator -- NameShapedWordRegex
                                  // already excludes this reaching here, but guard for safety.

                if (!char.IsUpper(segment[0]))
                    return false;

                for (int i = 1; i < segment.Length; i++)
                {
                    if (!char.IsLower(segment[i]))
                        return false;
                }
            }

            return true;
        }
    }
}
