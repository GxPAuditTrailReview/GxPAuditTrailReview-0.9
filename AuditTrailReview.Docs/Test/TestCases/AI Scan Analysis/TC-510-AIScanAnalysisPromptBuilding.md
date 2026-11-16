# TC-510 – AI Scan Analysis: Prompt Building

## Objective
Verify `AuditTrailPromptBuilder` (the single source of truth for the prompt sent to OpenAI for AI-assisted scan analysis, consumed by `OpenAIService.QueryJsonWithPromptAsync`) and its `DescribeTemplate()` method, which `Pages/AdminPanel/Settings/Index.cshtml.cs`'s AI Analysis tab uses to show administrators the exact prompt, read-only, without a real scan's data (formerly `Pages/AdminPanel/AIEnhancedReviewEngine.cshtml.cs`, merged into Settings -- see TC-703).

## Preconditions
- No database, web server, or live OpenAI call involved: pure unit tests against `AuditTrailPromptBuilder`'s fluent builder (`WithJson`, `WithUserPrompt`, `WithFindingsOnly`, `WithFindingsSampled`, `WithStatisticalSummary`, `Build`) and its static `DescribeTemplate()`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Build a prompt with `WithJson` only | The supplied JSON appears verbatim in the prompt |
| 2 | Build a prompt with no, a blank, and a populated optional user prompt (`WithUserPrompt`) | No/blank user prompt omits the "User prompt:" section entirely; a populated one includes the section header and the text |
| 3 | Build a prompt and inspect its compliance framing | Mentions "GxP" and "Part 11" |
| 4 | Build a prompt and inspect its response-shape instructions | Requests a JSON response containing `"insights"` and `"recommendations"` |
| 5 | Build a prompt and inspect its model-behavior instructions | Instructs the model not to simply restate rule-engine findings, not to infer unsupplied facts, to reference specific records, not to assert a definite regulatory violation, to respond with a raw JSON object only (no markdown fences), and that `insights`/`recommendations` must each be a plain string, never a nested array/object |
| 6 | Build a prompt without calling `WithFindingsOnly` (i.e. the default, "full scan" tier) | The intro paragraph describes the data as the complete audit trail, not a flagged subset |
| 7 | Build a prompt with `WithFindingsOnly(true)` | The intro paragraph describes the data as only the rule engine's flagged findings, not the full audit trail |
| 8 | Build a `WithFindingsOnly(true)` prompt without calling `WithFindingsSampled` (or with `sampled: false`) | No sampling notice appears in the prompt |
| 9 | Build a `WithFindingsOnly(true)` prompt with `WithFindingsSampled(true, cap)` | The prompt includes a note naming the cap and stating the findings are an evenly-spaced sample (not just the first ones), instructing the model not to assume full coverage |
| 10 | Build a `WithFindingsOnly(false)` prompt with `WithFindingsSampled(true, cap)` (should never happen in production, but must not render nonsensically) | No sampling notice appears -- the notice is gated on `WithFindingsOnly(true)` as well as sampling |
| 11 | Build a `WithFindingsOnly(true)` prompt with `WithStatisticalSummary(json)` | The prompt includes a section stating the statistics cover the audit trail's entire row set, followed by the supplied JSON |
| 12 | Build a `WithFindingsOnly(true)` prompt without calling `WithStatisticalSummary` (or with `null`) | No statistics section appears |
| 13 | Call `DescribeTemplate()` | Returns non-empty text that contains a placeholder phrase ("different for every scan") instead of real or empty-looking scan data, describes the complete-audit-trail (full-scan) wording by default, and includes no sampling notice or statistics section |
| 14 | Compare `DescribeTemplate()`'s output with `Build()` called using that same placeholder string as the JSON | The two strings are identical, so `DescribeTemplate` can never drift from what `OpenAIService` actually sends |

## Assertions
- Every documented instruction and compliance phrase from the production prompt template is present in `Build()`'s output, in both the full-scan and findings-only wording branches.
- The data-description sentence (as of 2026-10-27) is tier-aware: it describes the JSON as the complete audit trail by default, or as the rule engine's flagged findings only when `WithFindingsOnly(true)` was set -- see ADR-0006's amendment and TC-107.
- The sampling notice appears only when both `WithFindingsOnly(true)` and `WithFindingsSampled(true, ...)` were called; it names the configured cap and says the findings are an even sample, not a prefix.
- The statistical-summary section appears only when both `WithFindingsOnly(true)` and a non-null `WithStatisticalSummary(...)` were called.
- `DescribeTemplate()` is provably in sync with `Build()` by construction, not by a separately maintained copy of the text.

## Related Code
- Test class: `AuditTrailReview.UnitTest.AuditTrailPromptBuilderTests`
- Methods: `Build_IncludesTheSuppliedJson`, `Build_NoUserPrompt_DoesNotIncludeUserPromptSection`, `Build_WithUserPrompt_IncludesUserPromptSectionAndText`, `Build_BlankUserPrompt_DoesNotIncludeUserPromptSection`, `Build_MentionsGxPAndPart11`, `Build_RequestsJsonShapedResponseWithInsightsAndRecommendations`, `Build_InstructsTheModelNotToJustRestateRuleEngineFindings`, `Build_InstructsTheModelToAvoidInferringUnsuppliedFacts`, `Build_InstructsTheModelToReferenceSpecificRecords`, `Build_InstructsTheModelNotToAssertDefiniteViolations`, `Build_InstructsTheModelToRespondWithRawJsonOnly`, `Build_InstructsTheModelThatInsightsAndRecommendationsMustBePlainStrings`, `Build_FindingsOnlyFalse_DescribesCompleteAuditTrail`, `Build_FindingsOnlyTrue_DescribesFlaggedSubsetOnly`, `Build_FindingsOnlyTrueNotSampled_OmitsSamplingNotice`, `Build_FindingsOnlyTrueSampled_IncludesCapNumberAndSamplingNoticeText`, `Build_FindingsOnlyFalseSampled_OmitsSamplingNoticeRegardless`, `Build_StatisticalSummarySupplied_IncludesSummarySection`, `Build_StatisticalSummaryOmitted_NoSummarySection`, `DescribeTemplate_DoesNotThrow_AndReturnsNonEmptyText`, `DescribeTemplate_ContainsAPlaceholderInsteadOfRealScanData`, `DescribeTemplate_MatchesBuildWithTheSamePlaceholderAsJson`
- Production code: `AuditTrailReview.Engine.AI.AuditTrailPromptBuilder`, consumed by `AuditTrailReview.Engine.AI.OpenAIService.QueryJsonWithPromptAsync`
- Related: the AI analysis on/off toggle and this read-only preview are covered by TC-753; the resulting OpenAI response is parsed by `ScanResultService.ParseOpenAIResponse` (TC-107)

## Status
Active
