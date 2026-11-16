# 0003. Shared rule engine library reused by all ingestion paths

Status: Accepted
Date: 2026-09-01 (reconstructed from the v0.6 codebase)

## Context
Audit trail files must be evaluated against the same 19 rules (categories A–E: Data Integrity, Action Consistency, Temporal Checks, User Validation, Anomaly Detection) no matter how they were ingested: a reviewer shouldn't have to know or care whether a scan came from a manual upload, an FTP drop, or an API call in order to trust that it was evaluated consistently.

## Decision
The rule evaluation logic lives in one place, `AuditTrailReview.Engine.ScanResultService.ApplyRulesAndGenerateResults`, and is called identically by:
- the manual web-upload handler (`Pages/Scan/Scan.cshtml.cs`),
- the shared `ScanProcessingService.ProcessFileAsync` (used by both the Worker Service and the Integration Hub API).

Both call paths load the same rule-enablement flags and `EngineSettings` from the database at the start of each scan, so behavior is driven entirely by stored configuration, not by which code path invoked it.

## Consequences

**Positive:**
- A rule fix or a threshold change in `EngineSettings` behaves identically regardless of ingestion path: there is exactly one rule engine to validate, not three.
- New rules only need to be added in one place (`ScanResultService`, `Rule.cs`'s `RulesEnum`, and the seed data in `Program.cs`) to apply everywhere.
- Dataset-wide checks (record-chain continuity for A2, duplicate composite records for A3, chronological order for C1, mass-change bursts for E1) are pre-computed once per scan before per-row evaluation, keeping the whole evaluation close to linear in the number of rows.

**Negative / known gaps:**
- **Previously divergent, now symmetric:** the manual web-upload handler used to additionally invoke AI analysis (`OpenAIService`) *after* calling into the shared engine, while the other two ingestion paths did not. A later product decision closed that gap (see ADR-0006): `ScanProcessingService.ProcessFileAsync`, shared by the Worker Service and the API, now queues the same AI-analysis step `Pages/Scan/Scan.cshtml.cs` already runs for manual uploads, gated by the same `EngineSettings.AiAnalysisEnabled` flag. All three ingestion paths are symmetric today.
- **File parsing/mapping upstream of the shared rule engine is *not* shared** (see ADR-0001's Consequences), so while the 19 rules themselves are guaranteed consistent, the row data they're evaluated against could theoretically differ slightly between the manual-upload parser and the `ScanProcessingService` parser if the two implementations drift.
- Rule configuration changes are not versioned or audited. See `3 Functional & Configuration Specification.md` NFR-009 for the gap where enabling/disabling a rule or changing a threshold is not itself recorded in the internal audit trail log.

## Alternatives considered
- **Duplicating a lightweight rule check per ingestion path** (e.g. a cut-down rule set for the API to avoid a dependency on the full Engine project). Rejected, since this is exactly the kind of drift the current design avoids, and the Engine project has no heavyweight dependencies that would justify skipping it.
- **A rules-as-data / external rules engine** (e.g. a business-rules-engine product, or externally hosted rule definitions). Rejected as unnecessary complexity for 19 rules that are stable in shape and only need enable/disable plus a handful of numeric/boolean thresholds.
