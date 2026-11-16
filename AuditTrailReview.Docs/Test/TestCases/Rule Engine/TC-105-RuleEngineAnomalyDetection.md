# TC-105 – Rule Engine: Anomaly Detection (E1)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) a mass-change anomaly per FR-018's E1 rule: `EngineSettings.MassChangeThresholdCount` or more records *by the same user* within a `EngineSettings.MassChangeWindowSeconds`-wide sliding window are all flagged, using the configured thresholds rather than a fixed 10-records/1-second burst, and without counting different users' unrelated changes toward the same burst.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper, with timestamps spaced to land inside or outside the window under test.
- Only rule E1 is enabled; the other 18 rules stay disabled.
- `EngineSettings()` with no explicit `MassChangeThresholdCount`/`MassChangeWindowSeconds` defaults to 10/1.0 (the same values this rule used to hard-code), so tests that omit `settings` exercise that default, and tests that construct a custom `EngineSettings` exercise the configurability itself.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule E1 (default settings) to 10 or more rows whose timestamps all fall within the same 1-second window | Every row in the window carries an `[E1]` finding |
| 2 | Apply rule E1 (default settings) to 9 rows within the same 1-second window | No `[E1]` finding is produced (below the 10-record threshold) |
| 3 | Apply rule E1 (default settings) to rows spread across multiple, non-overlapping seconds | No `[E1]` finding is produced |
| 4 | Construct a bare `EngineSettings()` and inspect `MassChangeThresholdCount`/`MassChangeWindowSeconds` | Both equal the old hard-coded values (10 / 1.0) |
| 5 | Apply rule E1 with a configured threshold lower than 10 to a burst that wouldn't have qualified under the old default | An `[E1]` finding is produced |
| 6 | Apply rule E1 with a configured threshold higher than 10 to a burst that would have qualified under the old default | No `[E1]` finding is produced |
| 7 | Apply rule E1 with a configured window wider than 1 second to records spread across a longer span | An `[E1]` finding is produced |
| 8 | Apply rule E1 with a configured window narrower than 1 second to a burst that would have qualified under the old default | No `[E1]` finding is produced |
| 9 | Apply rule E1 with a custom threshold/window and inspect the finding text | The finding message reflects the configured threshold and window, not the old hard-coded 10/1 |
| 10 | Apply rule E1 to a burst whose source `TimestampRaw` has no fractional-seconds component at all (e.g. `"8/25/2026 11:00:00 AM"`) -- regression test for a user-reported concern that the finding always printed a fabricated ".000" -- and, separately, to a burst whose source `TimestampRaw` genuinely does declare fractional seconds (e.g. `"2026-08-25T11:00:00.123Z"`) | The no-sub-second-source case's finding text shows the timestamp without any milliseconds component; the genuinely-sub-second case still shows it |
| 11 | Apply rule E1 to 10 modifications within the same 1-second window made by 10 different users (regression test for a user-reported fix: E1 used to count modifications toward a burst regardless of who made them) | No `[E1]` finding is produced -- no single user reaches the threshold |
| 12 | Apply rule E1 to a genuine 10-modification burst from one user, interleaved with a different user's two ordinary, non-bursting changes in the same overall time span | Only the bursting user's rows carry an `[E1]` finding; the other user's rows do not |
| 13 | Apply rule E1 to a qualifying burst and inspect the finding text | The finding names the bursting user (e.g. "same user (\'burstUser\')") |

## Assertions
- `ScanResultInfo.Finding` contains `[E1]` for every row inside a qualifying `MassChangeThresholdCount`-or-more-record, `MassChangeWindowSeconds`-wide window, and for no row otherwise.
- The finding message text reflects the actual configured threshold and window values.
- The finding message's timestamp includes a milliseconds component only when the source data actually declared one; it never fabricates ".000" for a source that only ever had whole-second precision.
- A burst is scoped per user: `MassChangeThresholdCount`-or-more modifications only count toward the same burst when they share the same `UserID`; different users' modifications in the same window never combine to trigger a finding for either of them individually.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `E1_TenOrMoreRecordsWithinOneSecond_FindingGenerated`, `E1_NineRecordsWithinOneSecond_NoFinding`, `E1_RecordsSpreadOverMultipleSeconds_NoFinding`, `E1_DefaultSettings_MatchesOldHardcodedTenPerSecond`, `E1_CustomLowerThreshold_FindingGeneratedBelowOldDefault`, `E1_CustomHigherThreshold_NoFindingForBurstThatWouldHaveMatchedOldDefault`, `E1_CustomWiderWindow_FindingGeneratedAcrossLongerSpan`, `E1_CustomNarrowerWindow_NoFindingForBurstThatWouldHaveMatchedOldDefault`, `E1_FindingMessage_ReflectsConfiguredThresholdAndWindow`, `E1_FindingMessage_SourceHadNoSubSecondPrecision_TimestampShownWithoutMilliseconds`, `E1_FindingMessage_SourceHadSubSecondPrecision_TimestampShownWithMilliseconds`, `E1_TenModificationsFromTenDifferentUsers_NoFinding`, `E1_OneUserBurstsAmongAnotherUsersUnrelatedChanges_OnlyBurstingUsersRowsFlagged`, `E1_FindingMessage_NamesTheBurstingUser`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`, `HasSubSecondPrecision`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`); configuration in `AuditTrailReview.Model.EngineSettings` (`MassChangeThresholdCount`, `MassChangeWindowSeconds`), editable via `ConfigPanel/RuleBasedReviewEngine`
- Requirement: FR-018 (rule E1)

## Status
Active
