# TC-112 – Rule Engine: Login Name and System Account Heuristics

## Objective
Verify the two string-heuristic helpers that back the rule engine's user-validation and AI-preview behavior: `LoginNameHeuristics.LooksLikePersonalName` (does a login look like a real person's name, used by the Preview page's "looks like a personal name" finding) and `SystemAccountHeuristics.IsLikelySystemAccount` (does a login look like a shared/system/service account, used by rule D1; see TC-104). Both are pure string heuristics with no database dependency.

## Preconditions
- No database or web server involved: pure unit tests against the two static helper classes.
- `SystemAccountHeuristics.IsLikelySystemAccount` is tested with its built-in exact-name and prefix lists, and with caller-supplied "additional patterns" (exact and wildcard `*`-suffixed).
- `LoginNameHeuristics.LooksLikePersonalName` is a structural heuristic (Title-Case shape, not a fixed name dictionary); see the class's own XML doc for the design rationale.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `LooksLikePersonalName` with null, empty, or whitespace-only input | Returns `false` |
| 2 | Call it with a single Title-Case name-shaped word (e.g. `Adrian`, `Łukasz`, `Jo`) | Returns `true`; the same word all-lowercase, all-uppercase, or mixed-case returns `false` |
| 3 | Call it with a single Title-Case word that is a known system/role placeholder (e.g. `Admin`, `System`, `Guest`) | Returns `false` even though the word is otherwise name-shaped |
| 4 | Call it with technical/formatted logins (`svc_batch`, `jdoe`, `jan.kowalski`, `DOMAIN\jsmith`, etc.) | Returns `false` |
| 5 | Call it with length-boundary inputs (1 char, 2 chars, 30 chars, 31 chars) | `false` below the minimum and above the 30-character maximum; `true` within range |
| 6 | Call it with 2–4 space-separated Title-Case words, and with 5 space-separated words | 2–4 words returns `true`; 5 words, a word containing digits, or trailing/leading whitespace that collapses it to a single lowercase word all return `false` |
| 7 | Call it with hyphenated/apostrophe names (`Anna-Maria`, `O'Brien`) and a mid-word-capital name (`McDonald`) | Hyphen/apostrophe names return `true`; `McDonald` is a documented gap and returns `false` |
| 8 | Call it with Unicode Title-Case and lowercase diacritic names (`Łukasz` / `łukasz`) | Title-Case returns `true`; lowercase returns `false`, since the check is Unicode-letter-aware, not a fixed ASCII list |
| 9 | Call `IsLikelySystemAccount` with null, empty, or whitespace-only input | Returns `false` |
| 10 | Call it with built-in exact-match names (`admin`, `sa`, `root`, `service`, …) in any casing | Returns `true` |
| 11 | Call it with built-in prefixes followed by a separator or digit (`svc-integration`, `sys-batchjob`, `svc01`, bare `svc`) | Returns `true` |
| 12 | Call it with a word that merely starts with a prefix's letters but no separator/digit (`systema`, `automobile`, `jobless`) | Returns `false`, since the separator/digit guard prevents false positives on ordinary words |
| 13 | Call it with ordinary individual logins (`jsmith`, `j.kowalski`, `Adam`) | Returns `false` |
| 14 | Call it with caller-supplied additional patterns: an exact match, an exact match in different casing, a wildcard (`contractor*`) match with and without a following separator, a bare `*`, and a blank entry in the list | Exact/wildcard matches return `true`; non-matches, a bare `*` (stripped to an empty prefix and explicitly guarded against), and blank entries all return `false` without throwing |
| 15 | Call it with `null` or an empty additional-patterns collection | Falls back to the built-in lists only |
| 16 | Call it with surrounding whitespace on the login | Trimmed before evaluation; a whitespace-padded match still returns `true` |

## Assertions
- `LooksLikePersonalName` and `IsLikelySystemAccount` return the documented boolean for every input class above, with no exceptions thrown for null/empty/malformed input.
- The system-account prefix guard and the personal-name known-word exclusion list are enforced exactly as documented, including the accepted gaps (`McDonald`, all-lowercase single words).

## Related Code
- Test classes: `AuditTrailReview.UnitTest.LoginNameHeuristicsTests`, `AuditTrailReview.UnitTest.SystemAccountHeuristicsTests`
- Methods: all `LooksLikePersonalName_*` methods in `LoginNameHeuristicsTests`; all `IsLikelySystemAccount_*` methods in `SystemAccountHeuristicsTests`
- Production code: `AuditTrailReview.Engine.LoginNameHeuristics.LooksLikePersonalName`, `AuditTrailReview.Engine.SystemAccountHeuristics.IsLikelySystemAccount`
- Related: rule D1 (TC-104) uses `SystemAccountHeuristics`; `Pages/Scan/Preview.cshtml.cs`'s export finding uses `LoginNameHeuristics` (see TC-782)

## Status
Active
