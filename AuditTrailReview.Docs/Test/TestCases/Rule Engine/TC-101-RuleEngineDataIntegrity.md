# TC-101 – Rule Engine: Data Integrity Rules (A1, A3)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against the two remaining Data Integrity rules defined in FR-018 (A1, A3): mandatory fields and no duplicate records. (Record-chain continuity, the third Data Integrity rule, has its own script: TC-109. Category A previously held five additional rules (unique identifiers, minimum reason length, minimum alphabetic reason content, sequence-gap detection, and boilerplate-reason detection), all removed in a deliberate scope reduction; see `Audit-Trail-Rule-Regulatory-Basis-Review.md`. The retired OQ scripts TC-108 and TC-110 covered two of those.)

## Preconditions
- No database or web server involved: these are pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Test rows are built with the test class's `Row(...)` helper, which returns a minimally valid `AuditTrailInfo` (operation `Update`, UTC timestamp, non-null old/new values, a valid reason); each test overrides only the field(s) relevant to the rule under test.
- Only the rule under test is enabled (`Apply(rows, r<N>: true)`); the other 18 of the 19 rules stay disabled so each test isolates a single rule's behavior.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule A1 to a row missing `UserID`, `Timestamp`, or `Operation` in turn | Each case produces an `[A1]` finding; a row with all mandatory fields present does not |
| 2 | Apply rule A3 to two rows with identical user/timestamp/operation/old/new values, and to two rows with distinct composite keys | The identical rows both carry an `[A3]` finding; the distinct rows do not |
| 3 | Apply rule A3 to two rows sharing identical user/timestamp/operation/old/new values but changing *different* objects (regression test for a user-reported bug: a legitimate bulk operation, e.g. a script setting many different pieces of equipment to the same new status in the same second, used to have every row misflagged as a "duplicate" of the others), to two rows sharing everything including the same object and field, and to two rows sharing everything but changing different fields on the same object | The different-object case produces no `[A3]` finding; the same-object-and-field case still does (a genuine duplicate); the different-field case produces no `[A3]` finding |

## Assertions
- `ScanResultInfo.Finding` contains the expected rule code (e.g. `[A1]`) when the rule condition is met, and does not contain it otherwise.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `A1_AllMandatoryFieldsPresent_NoFinding`, `A1_MissingUser_FindingGenerated`, `A1_MissingTimestamp_FindingGenerated`, `A1_MissingOperation_FindingGenerated`, `A3_DuplicateCompositeKey_FindingGenerated`, `A3_UniqueCompositeKeys_NoFinding`, `A3_SameUserTimestampValuesButDifferentObjects_NoFinding`, `A3_SameUserTimestampValuesAndSameObjectAndField_FindingGenerated`, `A3_SameEverythingButDifferentField_NoFinding`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rules A1, A3)

## Status
Active
