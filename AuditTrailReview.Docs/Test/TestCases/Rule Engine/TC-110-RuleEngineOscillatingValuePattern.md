# TC-110 – Rule Engine: Oscillating Value Pattern (E2)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against FR-018's E2 rule: for each object/field (`ObjectType`+`Object`+`Field`), walked in actual chronological order within the scan, a value that repeats in strict two-value alternation -- A, B, A, B -- across at least four Create/Update events is flagged as a possible sign of an unstable process or unusual back-and-forth corrections. Unlike E1 (a burst of too many changes too fast), E2 doesn't care about timing at all -- it looks for a specific *shape* in the value history, however far apart in time the flips occur.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper, with `objectType`/`objectName`/`field` set to identify the object/field under test and `newValue` set to construct the value sequence under test.
- Only rule E2 is enabled; the other 18 rules stay disabled.
- The minimum shape this rule looks for is four consecutive Create/Update values in strict A-B-A-B alternation; three values (A, B, A) are not enough, and a longer run (A, B, A, B, A, B, ...) is flagged again at each point it continues the pattern, not just the first four values.
- Rows with no `Object` at all, or no `Timestamp`, are excluded from the sequence entirely (the same exclusion A2 applies). A `Delete` row resets the tracked value sequence for that object/field -- a value re-established after a deletion is not compared against values from before it.
- Values are compared trimmed and case-insensitively, consistent with A2/B1's own value-comparison style elsewhere in this engine.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule E2 to a Create (A), Update (B), Update (A), Update (B) sequence for one object/field | The fourth row (completing the second A/B repeat) carries an `[E2]` finding; the first three do not |
| 2 | Apply rule E2 to a six-value A, B, A, B, A, B sequence | Rows 4, 5, and 6 each carry an `[E2]` finding -- every row that continues the alternation once the minimum shape is established |
| 3 | Apply rule E2 to a four-value sequence with three distinct values (A, B, C, B) | No `[E2]` finding -- the first and third values don't match |
| 4 | Apply rule E2 to the same value repeated four times (A, A, A, A) | No `[E2]` finding -- there is only one state, not two alternating ones |
| 5 | Apply rule E2 to a three-value sequence (A, B, A) | No `[E2]` finding -- one short of the minimum four-value shape |
| 6 | Apply rule E2 to two different fields on the same object, one oscillating and one not | Only the oscillating field's row is flagged; the other field's rows are unaffected |
| 7 | Apply rule E2 to two different objects, one oscillating and one not | Only the oscillating object's row is flagged; the other object's rows are unaffected |
| 8 | Apply rule E2 to values differing only in case and surrounding whitespace across the sequence | The finding is still generated (compared trimmed, case-insensitively) |
| 9 | Apply rule E2 to a Create/Update/Delete/Create/Update sequence where the pre- and post-deletion values would otherwise form an A-B-A-B shape | No `[E2]` finding -- the deletion resets the tracked sequence, so only two values exist on either side of it |
| 10 | Apply rule E2 to rows with no `Object` populated | No `[E2]` finding is ever produced for these rows, even where the value sequence would otherwise qualify |
| 11 | Apply rule E2 with the rule disabled against data that would otherwise qualify | No `[E2]` finding is produced |
| 12 | Apply rule E2 to a qualifying oscillation and inspect the finding text | The finding message includes both alternating values |

## Assertions
- `ScanResultInfo.Finding` contains `[E2]` only for the row completing a strict two-value alternation of at least four Create/Update values for the same `ObjectType`+`Object`+`Field`, and for every subsequent row that continues that alternation.
- Rows with no `Object` or no `Timestamp` never participate in the tracked sequence.
- A `Delete` row resets the tracked value sequence for that object/field, so nothing after it is compared against values from before it.
- The match is case-insensitive and trimmed, and the finding message text names both alternating values.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `E2_ABAB_Pattern_FindingGenerated`, `E2_LongerOscillation_EachSubsequentRepeatAlsoFlagged`, `E2_NonAlternatingValueSequence_NoFinding`, `E2_SameValueRepeated_NoFinding`, `E2_OnlyThreeValueChanges_NoFinding`, `E2_DifferentFieldsOnSameObject_EvaluatedIndependently`, `E2_DifferentObjects_EvaluatedIndependently`, `E2_CaseInsensitiveAndTrimmedValueMatching_FindingGenerated`, `E2_DeleteResetsSequence_NotFlaggedAcrossDeletion`, `E2_RowsWithoutObject_ExcludedFromEvaluation`, `E2_Disabled_NoFinding`, `E2_FindingMessage_ContainsBothValues`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; `AuditTrailReview.Engine.ScanResultService.GetFieldHistoryKey`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rule E2)

## Status
Active
