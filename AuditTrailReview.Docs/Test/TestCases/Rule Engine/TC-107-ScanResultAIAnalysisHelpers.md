# TC-107 – Scan Result Service: AI Analysis Support Helpers

## Objective
Verify the `ScanResultService` helper methods that support the AI-assisted analysis feature (FR-035–FR-037, ADR-0006, NFR-014): `ConvertScanResultsToJson` (two overloads -- the uncapped one serializes a scan's entire row set for the "full scan" tier, the capped one serializes only its rule-engine findings, evenly sampled, for the "findings-only" tier above `AiSettings.FullScanRowThreshold`), `BuildScanStatisticalSummaryJson` (a whole-scan statistical summary sent alongside the findings-only tier's sampled findings), `ShouldUseFindingsOnlyTier` (the tier-boundary decision), `ParseOpenAIResponse` (extracts insights/recommendations text from the OpenAI API's response), and `HumanizeAiResponseValue` (renders a readable fallback when the AI does not follow the prompt's "insights"/"recommendations" must-be-a-plain-string instruction and returns a JSON array/object instead). These are a distinct concern from the 19-rule engine itself, namely output formatting and third-party response parsing rather than rule evaluation, so they're documented separately from TC-101–TC-106.

## Preconditions
- No database, web server, or live OpenAI call involved: pure in-memory unit tests against static `ScanResultService` methods, using hand-built `ScanResultInfo` objects and literal JSON response strings.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the uncapped `ConvertScanResultsToJson(scanResults)` with an empty result list | Returns an empty JSON array (`[]`) |
| 2 | Call the uncapped `ConvertScanResultsToJson(scanResults)` with a mix of rows, some with an empty/whitespace `Finding` and some with a non-empty one | ALL rows appear in the output, flagged or not -- the uncapped overload is the "full scan" tier and does not filter |
| 3 | Call the capped `ConvertScanResultsToJson(scanResults, maxFindings, out sampled)` with a mix of rows, some with an empty/whitespace `Finding` and some with a non-empty one | Only rows with a non-empty `Finding` appear in the output -- rows without a finding are never sent in the findings-only tier |
| 4 | Call the capped overload with more matching findings than `maxFindings` | Output contains exactly `maxFindings` findings, `sampled` is `true`, and both the first and last findings (by original order) are present in the output -- proving it's an even sample across the whole set, not a prefix |
| 5 | Call the capped overload with exactly `maxFindings` matching findings (boundary case) | All findings are included, and `sampled` is `false` (not a false positive at the exact boundary) |
| 6 | Call `BuildScanStatisticalSummaryJson` with a mix of rows across several users, operations, object types, and timestamps (including some rows with no `Reason` and some with no `Timestamp`) | Output's `totalRows`/`totalFindings`/`missingReasonCount` are correct; `dateRangeStart`/`dateRangeEnd` reflect the min/max non-null `Timestamp`; `topUsersByRecordCount`/`topObjectTypesByRecordCount` are capped at 10 entries, ordered by count descending; `recordsByHourOfDay`/`recordsByDayOfWeek` sum to the number of rows with a non-null `Timestamp` |
| 7 | Call `BuildScanStatisticalSummaryJson` with an empty result list | Returns a well-formed summary with all counts zero and empty/zeroed collections -- no exception |
| 8 | Call `ShouldUseFindingsOnlyTier` with `totalRows` exactly equal to the threshold, then with `totalRows` one greater | Exactly at the threshold returns `false` (full-scan tier); one row over returns `true` (findings-only tier) |
| 9 | Call `ParseOpenAIResponse` with a null or empty string | Returns empty strings for both insights and recommendations, no exception |
| 10 | Call `ParseOpenAIResponse` with a well-formed OpenAI chat-completion response wrapping a JSON `{"insights":...,"recommendations":...}` payload in `choices[0].message.content` | Returned insights and recommendations match the embedded values exactly |
| 11 | Call `ParseOpenAIResponse` with a string that is not valid JSON at all | Returns a non-empty insights string indicating a parse error, rather than throwing |
| 12 | Call `ParseOpenAIResponse` where the embedded `"insights"` (or `"recommendations"`) value is a JSON array of `{"finding", "records": [...]}` groups -- the shape a model has been observed returning despite the prompt's plain-string instruction -- for both the recognized shape and an unrecognized one | Recognized shape: a readable "finding: record details" summary, not raw JSON. Unrecognized shape: compact (single-line) JSON, never an indented multi-line dump |

## Assertions
- The uncapped `ConvertScanResultsToJson(scanResults)` serializes the scan's complete row set verbatim, with no filtering -- it is the "full scan" tier's payload builder (AiSettings.FullScanRowThreshold not exceeded) and intentionally does not look at `Finding` at all.
- The capped `ConvertScanResultsToJson(scanResults, maxFindings, out sampled)` filters to rows where `Finding` is non-empty, then -- only when there are more matches than `maxFindings` -- selects an even sample across the whole ordered set of matches (first, last, and spread in between), never just a prefix. `sampled` correctly distinguishes "more findings existed than the cap" from "findings count exactly equalled the cap" (no off-by-one).
- Both overloads produce compact JSON (`WriteIndented: false`), not indented.
- `BuildScanStatisticalSummaryJson`'s output size does not depend on the scan's row count (top-10 lists, fixed 24/7-element histograms) even though its computation reads every row in one pass.
- `ShouldUseFindingsOnlyTier`'s boundary is inclusive of the threshold itself (`totalRows == threshold` stays in the full-scan tier).
- `ParseOpenAIResponse` never throws on empty or malformed input; malformed input surfaces as an error indication in the insights value rather than an unhandled exception.
- A well-formed response's nested `insights`/`recommendations` JSON is extracted correctly from the OpenAI wrapper structure.
- When `"insights"`/`"recommendations"` is itself a JSON array/object instead of a string, the returned text is human-readable (never `JToken.ToString()`'s default indented JSON dump) -- a recognized `{"finding","records"}` shape renders a one-line summary per item; any other shape falls back to compact (not indented) JSON.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanResultServiceTests`
- Methods: `ConvertScanResultsToJson_EmptyList_ReturnsEmptyJsonArray`, `ConvertScanResultsToJson_Uncapped_IncludesAllRowsRegardlessOfFinding`, `ConvertScanResultsToJson_Capped_FiltersOutRowsWithoutFindings`, `ConvertScanResultsToJson_FewerFindingsThanCap_IncludesAllAndReportsNotSampled`, `ConvertScanResultsToJson_FindingsCountEqualsCap_ReportsNotSampled`, `ConvertScanResultsToJson_MoreFindingsThanCap_SamplesEvenlyAndReportsSampled`, `BuildScanStatisticalSummaryJson_*`, `ShouldUseFindingsOnlyTier_*`, `ParseOpenAIResponse_NullOrEmpty_ReturnsEmptyStrings`, `ParseOpenAIResponse_ValidResponse_ExtractsInsightsAndRecommendations`, `ParseOpenAIResponse_InvalidJson_ReturnsErrorInInsights`, `ParseOpenAIResponse_InsightsAsFindingGroupArray_RendersReadableSummaryNotRawJson`, `ParseOpenAIResponse_RecommendationsAsFindingGroupArray_RendersReadableSummaryNotRawJson`, `ParseOpenAIResponse_InsightsAsUnrecognizedJsonShape_FallsBackToCompactJsonNotIndented`
- Production code: `AuditTrailReview.Engine.ScanResultService.ConvertScanResultsToJson`, `AuditTrailReview.Engine.ScanResultService.BuildScanStatisticalSummaryJson`, `AuditTrailReview.Engine.ScanResultService.ShouldUseFindingsOnlyTier`, `AuditTrailReview.Engine.ScanResultService.ParseOpenAIResponse`, `AuditTrailReview.Engine.ScanResultService.HumanizeAiResponseValue`, `AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync`, `AuditTrailReview.Model.AiSettings.FullScanRowThreshold`, `AuditTrailReview.Model.AiSettings.MaxFindingsPerAnalysis`
- Requirement: FR-035, FR-036, FR-037; ADR-0006; NFR-014

## Status
Active
