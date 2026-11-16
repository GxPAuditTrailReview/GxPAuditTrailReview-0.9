# TC-812 – Application Services: MCP Server Scan and Audit-Trail Query Tools

## Objective
Verify the read-only MCP tools in `AuditTrailReview.McpServer.Tools.AuditTrailTools` added on top of `list_systems`/`list_scans` (ADR-0010): `get_scan_summary`, `get_scan_findings`, `query_scan_records` and `query_app_audit_trail`. They must return correct, filtered data, report bad input as an `error` object instead of throwing, and cap result size.

## Preconditions
- Tool methods are called directly (static methods taking an EF Core InMemory `ApplicationDbContext`); no MCP transport or HTTP involved.
- Seed: one `SystemATR`, one scan with three records (two with findings, one reviewed as `Deviation`), a record in another scan, and two `dbo.AuditTrail` entries (one for the scan).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `get_scan_summary` for the seeded scan | `totalRecords` = 3, `findingCount` = 2 |
| 2 | `get_scan_summary` for an unknown scan id | JSON object with an `error` property |
| 3 | `get_scan_findings` with `reviewStatus = "Deviation"` | Only the one matching finding |
| 4 | `get_scan_findings` with `reviewStatus = "bogus"` | `error` object listing valid values |
| 5 | `query_scan_records` with `userId` and `findingsOnly` | Only that user's finding records in the scan (2) |
| 6 | `query_scan_records` with `from` set and `take = 100000` | Date filter applied (2 rows); oversized `take` is capped, no exception |
| 7 | `query_app_audit_trail` with `scanId` | Only audit-trail entries for that scan (1) |
| 8 | `list_systems` with two systems seeded out of order | Both returned, ordered by id |
| 9 | `list_scans` with `systemId` and `take = 1` | Only the newest scan of that system |
| 10 | Run every tool against SQLite (a real relational provider) | All queries translate to SQL and return the expected data |

## Assertions
- Results are scoped to the requested scan and honour every supplied filter.
- Unknown scan or invalid enum input yields an `error` object, not an exception.
- `take` is capped at 200 (the cap itself is only exercised with an oversized value, not 200+ rows).

## Related Code
- Test class: `AuditTrailReview.UnitTest.McpServer.AuditTrailToolsTests`
- Methods: `GetScanSummary_ExistingScan_CountsRecordsAndFindings`, `GetScanSummary_UnknownScan_ReturnsErrorObject`, `GetScanFindings_ReviewStatusFilter_ReturnsOnlyMatching`, `GetScanFindings_InvalidReviewStatus_ReturnsErrorObject`, `QueryScanRecords_UserAndFindingsOnly_FiltersWithinScan`, `QueryScanRecords_TakeAboveCapAndDateRange_FiltersAndDoesNotThrow`, `QueryAppAuditTrail_ScanIdFilter_ReturnsOnlyThatScansEntries`, `ListSystems_ReturnsSeededSystemsOrderedById`, `ListScans_SystemFilterAndTake_ReturnsMostRecentFirstWithinFilter`, `AllTools_OnRelationalProvider_QueriesTranslateAndReturnData`
- Requirement: URS-035 / FR-043 (see Validation Package)
- Production code: `AuditTrailReview.McpServer.Tools.AuditTrailTools`

## Notes
- Step 10 runs on SQLite, which proves the LINQ translates to SQL (GroupBy on a nullable enum, enum `ToString()`, date ranges) but is not SQL Server or PostgreSQL; run the tools once against each real provider.
- Not covered: the objectType/operation/user filters on `query_app_audit_trail` individually, and tool discovery through a real MCP client.

## Status
Active
