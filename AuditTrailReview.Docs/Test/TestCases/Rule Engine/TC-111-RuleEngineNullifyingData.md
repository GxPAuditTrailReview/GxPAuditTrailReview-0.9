# TC-111 – Rule Engine: Nullifying Data (E3)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against FR-018's E3 rule: an `Update` row that changes a genuine (non-blank, non-zero) `OldValue` to blank/NULL or the literal value `"0"` is flagged as a possible sign of an earlier data-entry error or a misconfiguration being silently reset. Unlike B2 (which flags an Update missing either value outright, as a data-completeness defect), E3 is a risk-based anomaly signal: it only fires when a real, meaningful value existed beforehand and was wiped out, and it treats a change to the literal `"0"` the same as a change to blank, since both represent a genuine value being replaced with "nothing" for practical purposes.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper, with `oldValue`/`newValue` set to construct the value transition under test.
- Only rule E3 is enabled; the other 18 rules stay disabled.
- The rule only ever looks at `Update` rows; `Create`, `Delete`, `Approve`, and `Review` rows are never flagged by it, regardless of their old/new values.
- "Genuine" means `OldValue` is non-blank (after trimming) and not itself the literal `"0"`: an Update whose old value was already blank or zero cannot nullify anything, since there was nothing meaningful to lose.
- "Nullified" means `NewValue` is blank (after trimming) or exactly `"0"`; no other numeric-zero spelling (`"0.0"`, `"0.00"`, `"-0"`, etc.) is currently recognized.
- Values are compared trimmed; `Operation` matching is case-insensitive, consistent with the rest of this engine.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule E3 to an Update from `"45.5"` to blank | The row carries an `[E3]` finding |
| 2 | Apply rule E3 to an Update from `"45.5"` to `"0"` | The row carries an `[E3]` finding |
| 3 | Apply rule E3 to an Update from `"0"` to blank | No `[E3]` finding: the old value wasn't genuine to begin with |
| 4 | Apply rule E3 to an Update from blank to `"0"` | No `[E3]` finding: the old value wasn't genuine to begin with |
| 5 | Apply rule E3 to an ordinary Update between two non-zero values (`"45.5"` → `"46.0"`) | No `[E3]` finding |
| 6 | Apply rule E3 to an Update from `"0"` to a real value (`"45.5"`) | No `[E3]` finding, since restoring a value is the opposite of nullifying it |
| 7 | Apply rule E3 to a Create establishing an initial value of `"0"` | No `[E3]` finding, since the rule only evaluates `Update` rows |
| 8 | Apply rule E3 to a Delete that correctly retains its old value (`"45.5"` → none) | No `[E3]` finding: a Delete is not an Update, and retaining the old value is B7's concern, not E3's |
| 9 | Apply rule E3 to an Update with mixed case (`"UPDATE"`) and values with surrounding whitespace (`" 45.5 "` → `"   "`) | The finding is still generated (compared trimmed, operation matched case-insensitively) |
| 10 | Apply rule E3 to an Update with a `null` `OldValue` and a blank `NewValue` | No `[E3]` finding: there is no genuine prior value to nullify |
| 11 | Apply rule E3 with the rule disabled against data that would otherwise qualify | No `[E3]` finding is produced |
| 12 | Apply rule E3 to a qualifying nullification and inspect the finding text | The finding message includes the original (now-nullified) value |

## Assertions
- `ScanResultInfo.Finding` contains `[E3]` only for an `Update` row whose trimmed `OldValue` is non-blank and not `"0"`, and whose trimmed `NewValue` is blank or exactly `"0"`.
- `Create`, `Delete`, `Approve`, and `Review` rows are never flagged by this rule regardless of their values.
- An Update whose `OldValue` was already blank or `"0"` is never flagged, since nothing genuine is being lost.
- The finding message text includes the original value that was nullified.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `E3_UpdateFromValueToBlank_FindingGenerated`, `E3_UpdateFromValueToZero_FindingGenerated`, `E3_UpdateFromZeroToBlank_NoFinding`, `E3_UpdateFromBlankToZero_NoFinding`, `E3_UpdateBetweenTwoNonZeroValues_NoFinding`, `E3_UpdateFromZeroToNonZero_NoFinding`, `E3_CreateWithZeroValue_NoFinding`, `E3_DeleteRetainingOldValue_NoFinding`, `E3_CaseAndWhitespaceInsensitive_FindingGenerated`, `E3_NullOldValue_NoFinding`, `E3_Disabled_NoFinding`, `E3_FindingMessage_ContainsOldValue`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rule E3)

## Status
Active
