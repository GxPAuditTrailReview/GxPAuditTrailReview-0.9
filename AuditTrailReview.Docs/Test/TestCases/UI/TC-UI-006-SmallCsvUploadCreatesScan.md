# TC-UI-006 – Small CSV Upload Produces a Reviewable Scan

## Objective
Verify the core ingest-to-review path end to end through the browser: uploading a small CSV on the manual upload page creates a persisted scan and lands the user on its Details page, using only the WebApp itself — no Integration Hub API, Worker Service, or FTP involved, since a file this size (well under the 10 MB Tier-1 threshold) is processed synchronously in-request (`ScanModel.OnPostAsync`).

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`, backed by its LocalDB database.
- `TestConfig.LoginEmail` / `TestConfig.LoginPassword` are set to a valid account.
- The bundled sample file `wwwroot/samples/sample-audit-trail.csv` (~5 KB) is available from the running WebApp's own static files, or a local copy of it is used as the upload input.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in via `/Identity/Account/Login` | Redirected to `/Dashboard` |
| 2 | Navigate to `/Scan/Scan` | The upload form (`#scanForm`) is displayed |
| 3 | Set the file input (`input[name='UploadedFile']`) to `sample-audit-trail.csv` | File is attached |
| 4 | Submit the form | Request completes without a 5xx error |
| 5 | Check the resulting URL | Redirected to `/Scan/Details` with a new scan `id` in the query string |
| 6 | Read the Details page's file name field | Shows `sample-audit-trail.csv` (the uploaded file name) |
| 7 | Navigate to `/Scan/Index` | The new scan's row (matching its file name/id) is present somewhere in the scan list table |

## Assertions
- The upload does not return a 5xx error and results in a redirect to `/Scan/Details?id=<newId>`. The redirect is awaited for up to 2 minutes, since the scan is processed in-request and can be slow on a small remote server; on a timeout the test fails with the current URL and the page text (validation or error message), not a bare timeout.
- The Details page's `ATFileName` field equals the uploaded file's name.
- The new scan appears in `/Scan/Index`'s results table.

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `SmallCsvUploadProducesReviewableScan`
- Config: `TestConfig.BaseUrl`, `LoginEmail`, `LoginPassword`
- Production code exercised: `AuditTrailReview.WebApp.Pages.Scan.ScanModel.OnPostAsync` (see `TC-782-ScanUploadAndPreview` for its unit-level coverage)

## Page Elements Used
| Element | Selector |
|---------|----------|
| Upload form | `#scanForm` |
| File input | `input[name='UploadedFile']` |
| Details file name field | the `ATFileName` display field on `/Scan/Details` |
| Scan list table | the scans table on `/Scan/Index` |

## Notes
- Uploading this file also queues a fire-and-forget background AI-analysis work item (`EngineSettings.AiAnalysisEnabled`, seeded `true`); the HTTP response itself does not wait on it, so this test does not depend on AI availability. If a fully AI-network-free run is required, disable AI analysis first via `/AdminPanel/Settings/Index?ActiveTab=ai`.
- Use one of the small bundled samples only (`sample-audit-trail.csv`/`.json`/`.xml`) — the `sample-audit-trail-performance*.csv` files are much larger and not appropriate for a smoke test.

## Status
Active
