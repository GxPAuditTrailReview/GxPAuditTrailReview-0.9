# TC-102 – Rule Engine: Action Consistency Rules (B1–B7)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against the seven Action Consistency rules defined in FR-018 (B1–B7), which check that Create/Update/Delete rows carry the value and reason data appropriate to that operation.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper and overridden per test (notably `operation`, `oldValue`, `newValue`, `reason`).
- Only the rule under test is enabled; the other 18 rules stay disabled.
- Operation matching for B1–B7 is case-insensitive and depends on the mapped `Operation` value equaling exactly `"Create"`, `"Update"`, or `"Delete"` (FR-018 acceptance criteria).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule B1 to an `Update` row where old value equals new value, and to one where they differ | Same-value case produces a `[B1]` finding; differing-value case does not |
| 2 | Apply rule B1 to a `Create` row with identical old/new values | No `[B1]` finding, since B1 only applies to `Update` rows |
| 3 | Apply rule B2 to an `Update` row missing the old value, one missing the new value, and one with both present | Missing-value cases each produce a `[B2]` finding; the complete row does not |
| 4 | Apply rule B3 to a `Create` row missing a new value, and to one with a new value | Missing case produces a `[B3]` finding; the complete case does not |
| 5 | Apply rule B4 to a `Create` row that carries an old value, to one with a null old value, and to one using a `(null)`/null-placeholder old value | The populated-old-value case produces a `[B4]` finding; the null and placeholder cases do not |
| 6 | Apply rule B5 to an `Update` row with no reason and one with a reason | Missing-reason case produces a `[B5]` finding; the reasoned case does not |
| 7 | Apply rule B6 to a `Delete` row with no reason and one with a reason | Missing-reason case produces a `[B6]` finding; the reasoned case does not |
| 8 | Apply rule B7 to a `Delete` row missing the old value and one retaining it | Missing case produces a `[B7]` finding; the retained-value case does not |

## Assertions
- `ScanResultInfo.Finding` contains the expected rule code (e.g. `[B3]`) exactly when the operation/value/reason combination violates that rule, matching FR-018's per-operation matrix.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `B1_UpdateWithSameBeforeAfter_FindingGenerated`, `B1_UpdateWithDifferentBeforeAfter_NoFinding`, `B1_CreateOperationIgnored`, `B1_UpdateWithEmptyValues_NoFinding`, `B2_UpdateMissingOldValue_FindingGenerated`, `B2_UpdateMissingNewValue_FindingGenerated`, `B2_UpdateWithBothValues_NoFinding`, `B3_CreateMissingNewValue_FindingGenerated`, `B3_CreateWithNewValue_NoFinding`, `B4_CreateWithOldValue_FindingGenerated`, `B4_CreateWithNullOldValue_NoFinding`, `B4_CreateWithNullPlaceholder_NoFinding`, `B5_UpdateMissingReason_FindingGenerated`, `B5_UpdateWithReason_NoFinding`, `B6_DeleteMissingReason_FindingGenerated`, `B6_DeleteWithReason_NoFinding`, `B7_DeleteMissingOldValue_FindingGenerated`, `B7_DeleteWithOldValue_NoFinding`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rules B1–B7)

## Status
Active
