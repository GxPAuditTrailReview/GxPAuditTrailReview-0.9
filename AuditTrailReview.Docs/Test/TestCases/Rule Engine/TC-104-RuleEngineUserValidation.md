# TC-104 – Rule Engine: User Validation Rules (D1–D2)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against the two User Validation rules defined in FR-018 (D1–D2): usernames must not match a known shared/generic/system account name or naming convention, and usernames must meet a minimum length.

A third User Validation rule (username must not look like a real personal name) existed as the original D1 and was removed at the user's instruction; see `Audit-Trail-Rule-Regulatory-Basis-Review.md` in the Reviews folder. The two surviving rules were renumbered at that point to stay contiguous (the minimum-username-length rule became D1, and the shared/generic/system-account rule became D2), then swapped again in a later, purely cosmetic rule-management round at the user's instruction: the shared/generic/system-account rule is now D1, and the minimum-username-length rule is now D2. No rule was added or removed by that swap, and no underlying id changed. The underlying `LoginNameHeuristics.LooksLikePersonalName` heuristic that powered the originally-removed rule is not dead code -- it is still called directly, independent of the rule engine, by the preview page's own Excel export -- so it and its tests remain in `LoginNameHeuristicsTests`, but it no longer backs any rule engine finding.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper and overridden per test, mainly the `UserID` field.
- Only the rule under test is enabled; the other 18 rules stay disabled.
- D1's shared/generic/system account detection is structural (`SystemAccountHeuristics.IsLikelySystemAccount`): a built-in exact-match name list (`"admin"`, `"test"`, `"guest"`, …) and a built-in prefix list with a separator/digit guard (`"svc-x"`, `"sys01"`, but not `"systema"`), plus any configured additions (`EngineSettings.AdditionalSystemAccountPatterns`, where a trailing `*` matches as a prefix with no separator/digit guard). D1 is independent of the personal-name heuristic (formerly the original rule D1, a different, unrelated rule) -- see `SystemAccountHeuristics`' XML doc for why a login can validly trigger neither, either, or (in principle) both.
- D2's minimum-length check is governed by `EngineSettings.UsernameMinLength`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule D1 to a row whose `UserID` matches a built-in exact-match account name (e.g. `"admin"`) or a built-in prefix followed by a separator/digit (e.g. `"svc-integration"`, `"sys01"`) | A `[D1]` finding is produced |
| 2 | Apply rule D1 to a row whose `UserID` merely starts with a known prefix's letters without a separator/digit (e.g. `"systema"`), or is an ordinary individual login (e.g. `"jsmith"`) | No `[D1]` finding |
| 3 | Apply rule D1 to a row whose `UserID` matches an additional configured exact or wildcard (`*`-suffixed) pattern | A `[D1]` finding is produced |
| 4 | Apply rule D1 to a row with a null `UserID`, and with the rule disabled against an otherwise-flaggable account name | No `[D1]` finding in either case |
| 5 | Apply rule D1 to a row whose `UserID` is a Title-Case personal name that the (removed) personal-name heuristic would have flagged (e.g. `"Adam"`) | No `[D1]` finding (D1's account-pattern check is independent of the personal-name heuristic) |
| 6 | Apply rule D2 to a `UserID` shorter than `UsernameMinLength`, one exactly at the minimum, and a null `UserID` | Too-short case produces a `[D2]` finding; the at-minimum and null cases do not |

## Assertions
- `ScanResultInfo.Finding` contains `[D1]` or `[D2]` exactly when the username violates the corresponding rule.
- A disabled rule never produces its finding regardless of the underlying data.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `D1_KnownAccountName_FindingGenerated`, `D1_KnownPrefixWithSeparator_FindingGenerated`, `D1_KnownPrefixFollowedByDigit_FindingGenerated`, `D1_PrefixWithoutSeparatorOrDigit_NoFinding`, `D1_OrdinaryIndividualLogin_NoFinding`, `D1_AdditionalExactPattern_FindingGenerated`, `D1_TestPrefixedLoginWithoutWildcardConfig_NoFinding`, `D1_AdditionalWildcardPattern_FindingGenerated`, `D1_NullUser_NoFinding`, `D1_Disabled_KnownAccountName_NoFinding`, `D1_PersonalNameShapedLoginNotFlaggedByD1`, `D2_UsernameTooShort_FindingGenerated`, `D2_UsernameAtMinLength_NoFinding`, `D2_NullUsername_NoFinding`; also `AuditTrailReview.UnitTest.LoginNameHeuristicsTests` for the retained (rule-independent) personal-name heuristic and `AuditTrailReview.UnitTest.SystemAccountHeuristicsTests` for D1's
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; `AuditTrailReview.Engine.LoginNameHeuristics`; `AuditTrailReview.Engine.SystemAccountHeuristics`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rules D1–D2)

## Status
Active
