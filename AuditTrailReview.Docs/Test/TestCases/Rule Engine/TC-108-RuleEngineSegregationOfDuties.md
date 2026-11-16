# TC-108 – Rule Engine: Segregation of Duties (B8)

## Objective
Verify that `ScanResultService.ApplyRulesAndGenerateResults` correctly flags (or does not flag) audit trail rows against FR-018's B8 rule: for each object (`ObjectType`+`Object`), the `UserID` on any `Approve`/`Review` row must not also appear on a `Create`/`Update` row for that same object anywhere in the scan. Unlike B1–B7, which each check a single row in isolation, B8 is a cross-row, per-object check: the same style of grouping A2 uses, but keyed on object identity (`ScanResultService.GetRecordKey`) rather than object+field, and checking "any prior creator/modifier", not chronological chain adjacency.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper, with `objectType`/`objectName` set to identify the object under test, `operation` set to exactly `"Create"`, `"Update"`, `"Approve"`, or `"Review"` (case-insensitive, exact-literal matching, with no fuzzy synonyms such as "Approved" or "Sign-off"), and `userId` set to construct same-user or different-user scenarios.
- Only rule B8 is enabled; the other 18 rules stay disabled.
- The check is scan-wide, not adjacency-based: an `Approve`/`Review` row is flagged against **any** earlier or later `Create`/`Update` row by the same user on the same object in the scan, regardless of how many other rows or how much time separates them.
- Rows with no `Object` at all are excluded entirely: there's no object identity to group them by.
- Rows with a null/empty `UserID` can never trigger or be flagged by this rule, since there is no user identity to compare.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply rule B8 to a Create by user `alice` followed by an Approve by `alice` on the same object | The Approve row carries a `[B8]` finding |
| 2 | Apply rule B8 to an Update by user `alice` followed by a Review by `alice` on the same object | The Review row carries a `[B8]` finding |
| 3 | Apply rule B8 to a Create by `alice` and an Approve by `bob` on the same object | No `[B8]` finding on either row |
| 4 | Apply rule B8 to an Approve row on an object with no prior Create/Update by anyone in the scan | No `[B8]` finding |
| 5 | Apply rule B8 to two different objects, one where the same user created and approved, one where different users did | Only the row on the conflicting object is flagged; the other object's rows are unaffected |
| 6 | Apply rule B8 to user IDs and operation values differing only in case (e.g. `"Alice"` vs `"ALICE"`, `"approve"` vs `"Approve"`) | The finding is still generated (compared case-insensitively) |
| 7 | Apply rule B8 to a Create by `alice`, several unrelated rows in between, then an Approve by `alice` on the same object much later in the scan | The Approve row is still flagged, since the check is scan-wide, not limited to adjacent rows |
| 8 | Apply rule B8 to a Create by `alice` followed by a non-Approve/Review operation (e.g. a second Update) also by `alice` on the same object | No `[B8]` finding: only Approve/Review rows are checked against prior creators/modifiers |
| 9 | Apply rule B8 to rows with no `Object` populated | No `[B8]` finding is ever produced for these rows, even where the user and operation would otherwise qualify |
| 10 | Apply rule B8 to an Approve row with a null/empty `UserID` | No `[B8]` finding |
| 11 | Apply rule B8 with the rule disabled against data that would otherwise qualify | No `[B8]` finding is produced |
| 12 | Apply rule B8 to a qualifying conflict and inspect the finding text | The finding message includes the conflicting user's `UserID` |

## Assertions
- `ScanResultInfo.Finding` contains `[B8]` only for an `Approve`/`Review` row whose `UserID` matches the `UserID` of some `Create`/`Update` row on the same `ObjectType`+`Object` elsewhere in the scan, and never for the `Create`/`Update` rows themselves.
- Rows with no `Object`, or with a null/empty `UserID`, never participate in the check, either as the row being evaluated or as part of the creator/modifier set another row is compared against.
- The match is scan-wide (any position in the input, not just immediately adjacent rows) and case-insensitive on both `Operation` and `UserID`.
- The finding message text identifies the conflicting `UserID`.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `B8_SameUserCreatedAndApproved_FindingGenerated`, `B8_SameUserModifiedAndReviewed_FindingGenerated`, `B8_DifferentUsers_NoFinding`, `B8_ApprovalWithNoPriorCreateOrUpdate_NoFinding`, `B8_DifferentObjects_EvaluatedIndependently`, `B8_CaseInsensitiveOperationAndUserMatching_FindingGenerated`, `B8_ApprovalNotImmediatelyAdjacent_StillFlagged`, `B8_NonApprovalOperationsByOtherwiseConflictingUser_NotFlagged`, `B8_RowsWithoutObject_ExcludedFromEvaluation`, `B8_NullUser_NoFinding`, `B8_Disabled_NoFinding`, `B8_FindingMessage_ContainsUserId`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`; `AuditTrailReview.Engine.ScanResultService.GetRecordKey`; rules enumerated in `AuditTrailReview.Model.Rule.cs` (`RulesEnum`)
- Requirement: FR-018 (rule B8)

## Status
Active
