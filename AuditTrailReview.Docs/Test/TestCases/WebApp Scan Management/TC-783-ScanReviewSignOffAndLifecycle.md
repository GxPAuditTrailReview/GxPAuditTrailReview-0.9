# TC-783 – WebApp Scan Management: Scan Review, Sign-Off, and Lifecycle

## Objective
Verify `DetailsModel` (`Pages/Scan/Details.cshtml.cs`), the scan detail/review page: loading a scan's results, AI analysis status, and signature history; recording a reviewer's disposition on a finding; reassigning a scan's system; exporting a scan to Excel; downloading the scan's originally uploaded file; and the close/sign and reopen lifecycle actions, including the gate that blocks closing a scan with unreviewed findings. Also verifies `ScanFileReader` (`AuditTrailReview.Engine`), the helper `DetailsModel` uses to check for and stream back that stored file.

## Preconditions
- `DetailsModel` is constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.InitializePageContext` wires up a signed-in user (and, for sign/reopen, a user id matching a seeded `ApplicationUser`, since those handlers resolve the acting user via `UserManager`).
- `ScanFileReaderTests` uses its own InMemory `ApplicationDbContext`, which only exercises `ScanFileReader`'s EF Core fallback read path. `ScanFileReaderProviderSqlTests` builds a context against the SQL Server/PostgreSQL providers (no connection is ever opened) purely to check the generated SQL text; see `ScanUploadJobProcessorTests.cs`'s own remarks (TC-402) on why the raw-ADO.NET streaming paths aren't reachable from a unit test.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnGetAsync` with a null id, and an unknown id | Returns `NotFound` |
| 2 | `OnGetAsync` for a scan with a system, and for one without | The system is populated, or stays `null`, respectively |
| 3 | `OnGetAsync` | Populates scan results and AI results scoped to this scan only, the systems list ordered by name, and scan signatures ordered by timestamp |
| 4 | `OnGetAsync` with no `EngineSettings` row, and with an existing row | Uses default settings, or the stored settings, respectively |
| 5 | `OnGetAiAnalysisAsync` with no AI results yet, and with results present | Reports "not ready", or "ready" with the result items, respectively |
| 6 | `OnPostReviewAsync` with an unknown scan-result id | Returns `NotFound` |
| 7 | `OnPostReviewAsync` with a valid review | Updates the review fields and redirects; logs an audit "Update" entry carrying the scan's Id in `ScanID` |
| 8 | `OnPostAssignSystemAsync` with an unknown scan id, and with an existing scan | `NotFound` for the first; updates `SystemId` and logs an audit entry (carrying the scan's Id in `ScanID`) for the second |
| 9 | `OnGetExportExcelAsync` with a null id, and an unknown id | Returns `NotFound` |
| 10 | `OnGetExportExcelAsync` for an existing scan | Returns an Excel file containing both worksheets |
| 11 | `OnPostSignAndCloseAsync` with an unknown scan id | Returns `NotFound` |
| 12 | `OnPostSignAndCloseAsync` with unreviewed findings present | Sets an error and does not close the scan |
| 13 | `OnPostSignAndCloseAsync` with no unreviewed findings | Closes the scan, adds a "Signed" entry, and logs an audit entry carrying the scan's Id in `ScanID` |
| 14 | `OnPostReopenAsync` with an unknown scan id, and with an existing closed scan | `NotFound` for the first; reopens the scan, adds a "Signed" entry for the reopen, and logs an audit entry (carrying the scan's Id in `ScanID`) for the second |
| 15 | `HasUnreviewedFindings` for a scan with no findings, with an unreviewed finding, with all findings reviewed, and with rows that have no finding text at all | `false`, `true`, `false`, and rows without finding text are ignored (never counted as unreviewed), respectively |
| 16 | `AiAnalysisMayBePending` for a recently created scan, for an old scan, and for a scan with no scan date | `true` for recent, `false` for old or dateless |
| 17 | `ShouldShowLiveUsernameTooShortFinding` when the username is no longer too short, when the persisted finding already includes `[D2]`, when the persisted finding has other findings but not `D2`, and when there's no persisted finding at all | `false`, `false`, `true`, `true` respectively, as a "live" re-check of rule D2 against the finding as currently persisted |
| 18 | `OnGetAsync` for a scan with a stored `ScanFile`, and for one without | `HasOriginalFile` is `true`, or `false`, respectively |
| 19 | `OnGetDownloadFileAsync` with a null id, with an unknown id, and for a scan with no stored `ScanFile` | Returns `NotFound` in all three cases (the last covers a scan processed before this feature existed) |
| 20 | `OnGetDownloadFileAsync` for a scan with a stored `ScanFile` | Returns a `FileStreamResult` of the original bytes, with `text/csv` content type and `audit.csv` as the download name; logs an audit "Create" entry attributed to the acting user |
| 21 | `ScanFileReader.HasFileAsync` for a scan with a `ScanFile` row, and for one without | `true`, or `false`, respectively |
| 22 | `ScanFileReader.OpenContentStreamAsync` for a scan with a `ScanFile` row, and for one without | Returns a stream of the exact stored bytes, or throws `InvalidOperationException`, respectively |
| 23 | `ScanFileReader.BuildContentSelectSql` against a PostgreSQL context, and against a SQL Server context | Produces `SELECT "Content" FROM "ScanFile" WHERE "ScanId" = @ScanId` (double-quoted, case-preserving), or the equivalent `[Content]`/`[ScanFile]`/`[ScanId]` bracket-quoted statement, respectively |

### Signature re-authentication (login + password in the signing pop-up)
| 27 | `OnPostSignAndCloseAsync` / `OnPostReopenAsync` with the signed-in user's login and a wrong password | Scan state unchanged, no `ScanSigned` row, no Scan audit entry; `TempData` re-opens the matching pop-up (`close` / `reopen`) with an error; `AccessFailedCount` goes to 1; a failed `SignatureFailed` `UserActivityLog` row is written |
| 28 | `OnPostSignAndCloseAsync` with no password | Scan stays open, nothing signed |
| 29 | `OnPostSignAndCloseAsync` / `OnPostReopenAsync` with a login that is not the signed-in user (correct password) | Nothing signed; pop-up re-opened; `AccessFailedCount` unchanged (no password was checked); `SignatureFailed` logged |
| 30 | `OnPostSignAndCloseAsync` with the login in a different letter case | Signs (logins are compared normalized) |
| 31 | A wrong password followed by the correct one | Second attempt signs; `AccessFailedCount` reset to 0; `SignatureAuthenticated` logged as success |
| 32 | Five wrong passwords in a row | 5th attempt deactivates the account (`LockoutEnd = DateTimeOffset.MaxValue`, via `FailedLoginLockoutService`), signs out `IdentityConstants.ApplicationScheme`, logs `AccountLockedOut`, redirects to `/Account/Lockout` (area Identity); nothing signed |
| 33 | Account already locked out, correct password | Redirects to `/Account/Lockout`; nothing signed |

### Closed-scan guard and bulk review
| 34 | `OnPostReviewAsync` for a finding on a closed (signed) scan | The finding is not changed, nothing is audited, an error is set, and the handler redirects back to the scan |
| 35 | `OnGetBulkSummaryAsync` for a scan with sole-rule, multi-rule, already reviewed and finding-less rows (plus a row of another scan) | `unreviewed` counts only this scan's unreviewed rows with a finding; each rule reports its unreviewed count and how many have it as the only finding; rules with no unreviewed findings are left out |
| 36 | `OnGetBulkPreviewAsync`, rule scope, "only finding" on and off | On: counts rows where the rule is the only finding and reports the multi-finding rows left out as `excluded`; off: counts every unreviewed row with the rule |
| 37 | `OnGetBulkPreviewAsync`, search scope with a blank term | Returns an error (a search-scope bulk review needs a search term) |
| 38 | `OnPostBulkReviewAsync`, rule scope with "only finding" | Sets status, comment, `ReviewedBy`, `ReviewedAt` on the unreviewed sole-rule rows only; multi-finding rows, already reviewed rows (decision and comment kept), other rules and other scans are unchanged; redirects to the Potential findings tab with a success message |
| 39 | `OnPostBulkReviewAsync`, rule scope including multi-finding rows | Every unreviewed row with the rule is updated; rows without it are not |
| 40 | `OnPostBulkReviewAsync`, search scope | Only unreviewed rows with a finding that match the term are updated; non-matching rows and rows without a finding are not |
| 41 | `OnPostBulkReviewAsync`, valid action on 3 rows | Each row gets its own `ReviewStatus` and `ReviewComment` Update entries, plus one "Bulk review (Scan #id)" Create entry with `RowCount = 3`; every entry carries `ScanID`, the acting user, a "Bulk finding review (...)" reason and a `UniqueIdentifier` |
| 42 | `OnPostBulkReviewAsync` on a closed scan, with a blank comment, with no decision, with a confirmed count that differs from the live count, and with an invalid rule code | Nothing is changed or audited; an error is set and no success message |
| 43 | `OnPostBulkReviewAsync` for an unknown scan id | Returns `NotFound` |
| 44 | `OnGetAsync` for a scan with unreviewed, reviewed and finding-less rows (plus another scan's row) | `UnreviewedFindingsCount` (the Bulk review sub-tab's badge) counts only this scan's unreviewed rows with a finding; `HasUnreviewedFindings` is `true` |
| 45 | Redirect after a bulk review, and after a single review | The bulk redirect carries `FindingsView = bulk` (back to the Bulk review sub-tab, step 38); the single review redirect carries no `FindingsView`, so it stays on the findings list |

### Audit trail tab
| 24 | `OnGetAsync` for this scan's own "Audit trail" tab, with an entry for this scan and one for a different scan | Only the entry whose `ScanID` matches the viewed scan is included; the other scan's entry is excluded |
| 25 | `OnGetAsync` with `AuditTrailSearchTerm` set | Filters to matching entries |
| 26 | `OnGetAsync` with more audit entries than one page size, requesting page 2 | Pagination honors `ResultsPageSize` and returns the correct `AuditTrailTotalPages`/remaining entries |

## Assertions
- `OnPostSignAndCloseAsync` never closes a scan while any finding lacks a recorded review decision.
- Reopen/sign-and-close each append a `ScanSignature` entry and log an audit-trail entry attributed to the acting user.
- AI analysis status and "may still be pending" are both derived correctly from the presence of AI results and the scan's age.
- `OnGetDownloadFileAsync` degrades to `NotFound` rather than throwing for a scan with no stored file, and logs an audit entry on every successful download.
- `ScanFileReader.BuildContentSelectSql` quotes identifiers appropriately per provider, so the generated SQL survives PostgreSQL's case-folding and matches EF Core's own mixed-case table/column names.
- Sign-and-close and reopen only happen after the signed-in user re-enters a correct password (21 CFR 11.200(a)); a wrong password counts toward lockout exactly like a failed login, and the scan is never left half-changed by a failed attempt.
- No finding of a closed (signed) scan can be reviewed, singly or in bulk, even by a hand-made POST.
- A bulk review only ever changes this scan's unreviewed findings, never overwrites an existing decision, requires a decision and a comment, applies only when the live count equals the count the reviewer confirmed, and leaves each changed row individually traceable in the audit trail.
- Every review/assign/sign-close/reopen audit entry carries the acted-on scan's Id in `ScanID`, and the Details page's "Audit trail" tab is scoped strictly to entries whose `ScanID` matches the scan being viewed, with search and paging.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Scan.DetailsModelTests`, `AuditTrailReview.UnitTest.Engine.ScanFileReaderTests`, `AuditTrailReview.UnitTest.Engine.ScanFileReaderProviderSqlTests`
- Methods: all methods in `WebApp/Scan/DetailsModelTests.cs`, `Engine/ScanFileReaderTests.cs`, `Engine/ScanFileReaderProviderSqlTests.cs` -- including `OnPostReviewAsync_ValidReview_AuditEntryRecordsScanId` and the Audit trail tab methods `OnGetAsync_AuditTrailEntriesForThisScan_Included`, `OnGetAsync_AuditTrailEntriesFromOtherScan_NotIncluded`, `OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailPaging_ReturnsRequestedPage`, and the signature re-authentication methods `OnPostSignAndCloseAsync_WrongPassword_DoesNotCloseAndReopensSignModal`, `OnPostSignAndCloseAsync_EmptyPassword_DoesNotClose`, `OnPostSignAndCloseAsync_LoginDoesNotMatchCurrentUser_DoesNotSignOrCountTowardLockout`, `OnPostSignAndCloseAsync_LoginDiffersOnlyInCase_Signs`, `OnPostSignAndCloseAsync_CorrectPassword_ResetsAccessFailedCountAndLogsSuccess`, `OnPostSignAndCloseAsync_FifthWrongPassword_DeactivatesSignsOutAndRedirectsToLockout`, `OnPostSignAndCloseAsync_AlreadyLockedOut_CorrectPasswordStillRejected`, `OnPostReopenAsync_WrongPassword_LeavesScanClosedAndReopensSignModal`, `OnPostReopenAsync_LoginDoesNotMatchCurrentUser_LeavesScanClosed`, the closed-scan guard `OnPostReviewAsync_ClosedScan_DoesNotChangeFindingAndSetsError`, and the bulk review methods `OnGetBulkSummaryAsync_MixedFindings_CountsUnreviewedPerRuleAndSoleFindings`, `OnGetBulkPreviewAsync_RuleScopeOnlySole_ReturnsCountAndExcluded`, `OnGetBulkPreviewAsync_RuleScopeIncludingMultiFindingRows_CountsAllRowsWithRule`, `OnGetBulkPreviewAsync_SearchScopeWithoutTerm_ReturnsError`, `OnPostBulkReviewAsync_RuleScopeOnlySole_UpdatesOnlyUnreviewedSoleFindingsOfThisScan`, `OnPostBulkReviewAsync_RuleScopeIncludingMultiFindingRows_UpdatesThemToo`, `OnPostBulkReviewAsync_SearchScope_UpdatesOnlyMatchingUnreviewedFindings`, `OnPostBulkReviewAsync_ValidBulkReview_LogsPerRowAuditEntriesAndOneSummary`, `OnPostBulkReviewAsync_ClosedScan_ChangesNothing`, `OnPostBulkReviewAsync_MissingComment_ChangesNothing`, `OnPostBulkReviewAsync_MissingStatus_ChangesNothing`, `OnPostBulkReviewAsync_ExpectedCountDiffersFromLiveCount_ChangesNothing`, `OnPostBulkReviewAsync_InvalidRuleCode_ChangesNothing`, `OnPostBulkReviewAsync_UnknownScan_ReturnsNotFound`, `OnGetAsync_MixedFindings_SetsUnreviewedFindingsCount`, `OnPostReviewAsync_ValidReview_RedirectDoesNotSwitchToBulkSubTab`
- Production code: `AuditTrailReview.WebApp.Pages.Scan.DetailsModel` (incl. `ReauthenticateAsync`, `BuildBulkSelection`), `AuditTrailReview.Engine.Services.AuditTrailService.LogUpdatesAsync` (covered directly by TC-801), `AuditTrailReview.WebApp.Services.FailedLoginLockoutService`, `AuditTrailReview.Engine.ScanFileReader`
- Requirement: FR-021, FR-022
- Related: rule D2 is covered directly by TC-104; audit logging by `AuditTrailService` is covered by TC-801 (the `ScanID` column written there); the provider-SQL quoting technique mirrors `ScanUploadJobProcessor.BuildFileContentSelectSql`, covered by TC-402

## Notes
- The unit tests use framework-default `IdentityOptions` (lockout after 5 failures), not the app's configured `SecuritySettings`; the threshold the live app uses is whatever Admin Panel → Settings holds.
- The signing pop-up itself (opening, read-only login, re-opening after an error) is markup/JS on `Details.cshtml` and is not covered by these unit tests; it is checked manually by FT-19/FT-21, and the Selenium page object `ScanDetailsPage.SignAndClose(password)` / `Reopen(password)` drives it (that project is disabled in CI).
- Sign-out on lockout is checked against a recording `IAuthenticationService` stand-in, not a real cookie handler.
- Bulk review on InMemory: the explicit transaction is ignored there (`TransactionIgnoredWarning` suppressed), so rollback of a half-done bulk action is not unit-tested; the SQL translation of the rule-code `LIKE` filters (`[` in the pattern) is only exercised against a live SQL Server. The 10,000-row limit (`MaxBulkReviewRows`) is not unit-tested.
- The Potential findings / Bulk review sub-tabs (switching, the "Open Bulk review" shortcut, the badge refresh) and the bulk pop-up (summary loading, preview, enabling the Apply button) are markup/JS on `Details.cshtml` and are not covered by these unit tests.

## Status
Active
