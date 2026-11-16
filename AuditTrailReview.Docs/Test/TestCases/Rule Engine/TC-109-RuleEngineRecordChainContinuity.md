# TC-109 – Rule Engine: Record-Chain Continuity (A2)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against FR-018's A2 rule: for each object/field (`ObjectType`+`Object`+`Field`), walked in actual chronological order within the scan, an Update row's `OldValue` must match the immediately preceding row's `NewValue` for that same object/field. A mismatch signals a gap in the audit trail itself -- a purged/deleted row, export truncation, or tampering -- which the purely single-row B1–B7 checks structurally cannot see.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper, with `objectType`/`objectName`/`field` set to identify the object/field under test and `oldValue`/`newValue` set to construct each row's link in the value chain.
- Only rule A2 is enabled; the other 18 rules stay disabled.
- Unlike A1 and A3 (per-row) and even C1 (per-record chronological order, keyed on `ObjectType`+`Object`), A2 groups by the finer-grained `ObjectType`+`Object`+`Field` (see `ScanResultService.GetFieldHistoryKey`) and evaluates rows in actual `Timestamp` order within each group, independent of the order rows appear in the source list.
- Rows with no `Object` at all, or no `Timestamp`, are excluded from the chain entirely: there's no reliable way to place them in a specific object's value history.
- The rule only checks `Update` rows; `Create` and `Delete` rows are never themselves flagged, though their `NewValue` (or, for `Delete`, the lack of one) still shapes the chain that later rows are checked against.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule A2 to a Create establishing a value followed by an Update whose `OldValue` matches it | Neither row carries an `[A2]` finding |
| 2 | Apply rule A2 to a Create followed by an Update whose `OldValue` does not match the Create's `NewValue` | Only the Update row carries an `[A2]` finding |
| 3 | Apply rule A2 to a single Update row with no preceding record for its object/field in the scan | No `[A2]` finding, regardless of its `OldValue` |
| 4 | Apply rule A2 to two different fields (`Status`, `Owner`) on the same object, one chain intact and one broken | Only the row breaking its own field's chain is flagged; the other field's rows are unaffected |
| 5 | Apply rule A2 to the same field on two different objects, one chain intact and one broken | Only the row breaking its own object's chain is flagged; the other object's rows are unaffected |
| 6 | Apply rule A2 to values differing only in case and surrounding whitespace across the chain | No `[A2]` finding (compared trimmed, case-insensitive) |
| 7 | Apply rule A2 to rows with no `Object` populated | No `[A2]` finding is ever produced for these rows, even with a data mismatch that would otherwise qualify |
| 8 | Apply rule A2 to a chain where the earliest row has no `Timestamp` | That row is excluded from the chain; the next row is evaluated as if it were first (no preceding baseline), not flagged |
| 9 | Apply rule A2 to a Create, then a Delete, then a later Update for the same object/field | The Delete ends the chain (clears the running baseline); the later Update is not compared against the pre-deletion value and is not flagged |
| 10 | Apply rule A2 to an Update with a missing `OldValue` | No `[A2]` finding (that gap is B2's concern, not double-flagged here) |
| 11 | Apply rule A2 with the rule disabled against data that would otherwise qualify | No `[A2]` finding is produced |
| 12 | Apply rule A2 to a qualifying mismatch and inspect the finding text | The finding message includes both the expected (previous) value and the actual (mismatched) `OldValue` |
| 13 | Apply rule A2 to two rows listed in the input list in the opposite order from their `Timestamp` order | The chain is still evaluated by actual chronological order, not list order; neither row is flagged |

## Assertions
- `ScanResultInfo.Finding` contains `[A2]` only for an `Update` row whose `OldValue` doesn't match the immediately preceding chronological record's `NewValue` for the same `ObjectType`+`Object`+`Field`, and never for `Create`/`Delete` rows themselves.
- Rows with no `Object` or no `Timestamp` never participate in the chain, either as the row being checked or as the baseline another row is checked against.
- A `Delete` row resets the running baseline, so nothing after it is compared against the pre-deletion value.
- The finding message text reflects the actual expected and actual values involved in the mismatch.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `A2_OldValueMatchesPreviousNewValue_NoFinding`, `A2_OldValueDoesNotMatchPreviousNewValue_FindingGeneratedOnMismatchedRowOnly`, `A2_FirstRecordInChain_NoFindingRegardlessOfOldValue`, `A2_DifferentFieldsOnSameObject_EvaluatedIndependently`, `A2_DifferentObjects_EvaluatedIndependently`, `A2_CaseInsensitiveAndTrimmedValueMatching_NoFinding`, `A2_RowsWithoutObject_ExcludedFromEvaluation`, `A2_RowWithoutTimestamp_ExcludedFromChain`, `A2_DeleteEndsChain_SubsequentRecordNotComparedAgainstPreDeletionValue`, `A2_MissingOldValueOnUpdate_NoFindingFromThisRule`, `A2_Disabled_NoFinding`, `A2_FindingMessage_ContainsExpectedAndActualValues`, `A2_RowsOutOfFileOrderButCorrectByTimestamp_EvaluatedInChronologicalOrder`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; `AuditTrailReview.Engine.ScanResultService.GetFieldHistoryKey`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rule A2)

## Status
Active
