# TC-106 – Rule Engine: Cross-Cutting Behavior

## Objective
Verify `ScanResultService.ApplyRulesAndGenerateResults`'s behavior that isn't specific to any single rule code: handling of null/empty input, all-rules-disabled, a null `EngineSettings`, scan-id propagation onto every result, row-count preservation, and correct combination of multiple simultaneous findings on one row.

## Preconditions
- No database or web server involved: pure in-memory unit tests against `ScanResultService.ApplyRulesAndGenerateResults`.
- Rows are built with the `Row(...)` helper; the fixed `ScanId` constant (42) used by the test class's `Apply(...)` helper is asserted onto every result.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `ApplyRulesAndGenerateResults` with a null row list | Returns an empty result list, no exception |
| 2 | Call it with an empty row list | Returns an empty result list |
| 3 | Call it with rows but all 19 rule flags disabled | Every result's `Finding` is null or empty |
| 4 | Call it with any rows | Every result's `ScanID` equals the scan id passed in |
| 5 | Call it with a null `EngineSettings` and rule B4 enabled | Completes without throwing, using rule defaults |
| 6 | Call it with 5 input rows | Returns exactly 5 results, one per input row |
| 7 | Apply multiple rules (A2, B5) to a single row that violates both | The single result's `Finding` contains `[A2]` and `[B5]` together |

## Assertions
- Null/empty input never throws and yields an empty result list.
- `ScanID` is propagated onto every generated `ScanResultInfo`.
- A null `EngineSettings` falls back to rule defaults rather than throwing.
- Multiple simultaneously-violated rules all appear in a single row's `Finding` text.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `ApplyRulesAndGenerateResults_NullRows_ReturnsEmptyList`, `ApplyRulesAndGenerateResults_EmptyRows_ReturnsEmptyList`, `ApplyRulesAndGenerateResults_AllRulesDisabled_NoFindings`, `ApplyRulesAndGenerateResults_ScanIdPropagatedToResults`, `ApplyRulesAndGenerateResults_NullEngineSettings_UsesDefaults_NoException`, `ApplyRulesAndGenerateResults_RowCountMatchesInputCount`, `MultipleRules_RowWithSeveralViolations_AllFindingsInSingleResult`
- Production code: `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`
- Requirement: FR-018

## Status
Active
