## 🚀 Release v0.8 – GxPAuditTrailReview

**Release Date:** 2026-10-20

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed for each system, and every scan can be saved for future reference.

Scan results can now be reviewed and assessed. Support for audit trails from major software vendors has also been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Adding help documentation
- Improvement of scanning 
- Proven possiblity to scan 300k records audity trail in about 9 minutes, limitation of scan file through interface 45MB on webbased application
- Scan mechanism in AuditTrailReview.IntegrationHub.WorkerService improved to handle large audit trail files with large amounts of records, and large amounts of queued files: files are now claimed and tracked in the database as they're picked up from FTP, so a service restart or crash mid-file no longer loses or duplicates a scan: the file is safely reprocessed once the service comes back up. Files that keep failing are retried a few times and then left alone instead of being retried forever.
- Worker Service now tidies up the FTP drop folder after itself: a successfully processed file is moved into a "Processed" subfolder, and a file that has failed repeatedly and given up is moved into a "Failed" subfolder for review; both folder names are configurable, and both are created automatically as needed. Keeps the drop folder showing only files still waiting to be picked up, and gives IT a safe place to clean up old files from. It also periodically double-checks for any file that should have been moved but wasn't (e.g. a one-off FTP hiccup) and retries the move automatically, so nothing gets permanently stuck.
- handeling paging whe  loading large amount of records, like 1 mln
- Tiered large-scan-upload processing: an upload too big to process inline is no longer rejected. It is now queued in the background -- either in-process for a moderate-sized file, or durably in `dbo.ScanUploadJob` for the Worker Service to pick up for a larger one -- using the same scanning pipeline either way. The web app shows a notification bell for when a background scan finishes, and the Integration Hub API exposes a `GET .../scan/{jobId}` status endpoint for the same purpose. Both recover cleanly from a mid-job restart.
- The Integration Hub API (`AuditTrailReview.IntegrationHub.API`) no longer processes uploads itself: every upload is validated immediately (rejecting a missing file, an unknown system, or an unsupported file type) and stored for the Worker Service to process, which returns a `202 Accepted` with a job id right away instead of leaving the caller's connection open for the whole scan. The bundled console client now polls for the result and reports the scan id or the error once the job finishes. A Worker Service must be running wherever the API is deployed.
- Successfully tested a 5,000,000-record (~714 MB) audit trail file end to end -- upload, background queue, Worker Service processing, and the completion notification -- completing in under 20 minutes, see `TestStrategy.md`.
- Added a Windows installer for the Integration Hub Worker Service (`installer/WorkerService/`), which registers it as a proper Windows Service and configures it to automatically restart itself after a crash, so a brief outage no longer leaves queued scans sitting unprocessed until someone notices.
- Added a docs chatbot ("Ask the docs") that answers questions about the application directly from its own documentation.
- Added self-contained SQLite and PostgreSQL Docker flavors of the web application (and, for PostgreSQL, the Integration Hub API and Worker Service too) so the app can be tried without a separate SQL Server instance -- not a replacement for the supported SQL-Server-backed deployment.
- Sped up the Dashboard's finding counts, which had become a slow, full-table scan on a database with millions of scan results.
-
---

### 🔧 Improvements

- Clos
- Lowered the web upload's synchronous-processing limit to 10 MB and its in-process background limit to 50 MB (anything larger now always goes to the Worker Service), based on real measured processing times -- a large file was previously able to tie up a web request for many minutes.
- Fixed the upload progress pop-up, which could show wildly wrong time estimates (hours) for a large file; it now shows a realistic estimate for a small file processed inline, and an upload-progress indicator (with an explanation that the scan continues in the background) for anything queued.
- Scan results now display timestamps converted to the configured workday time zone instead of raw UTC, matching the time zone the working-hours rule already evaluates against.
- `ScanUploadJob` failure messages now include the underlying database error instead of just Entity Framework's generic wrapper text, making a failed upload easier to diagnose from the admin UI.
- Raised the Integration Hub's outer upload ceiling to 1 GB (from 500 MB) to support very large audit trail exports.
-

---

### 🐞 Bug Fixes

- Fixed bugs identified during development and testing  
- Fixed a rare case where the Worker Service's post-processing move to `ProcessedFolder`/`FailedFolder` could fail with an FTP `550` rename error when a file with the same name already existed at the destination (e.g., left over from an earlier run) -- the move now explicitly checks for and clears any existing destination file first.  
- Raised the Integration Hub API's (`AuditTrailReview.IntegrationHub.API`) upload size limit to 50 MB to match the web application, and gave the bundled console client (`AuditTrailReview.Console`) a much longer request timeout -- external callers sending a large audit trail file through the API were previously capped well below what the shared scanning engine can actually handle, and the console client would give up on a real-sized file before the server finished.  
- Fixed a Worker Service upload that could stall for 30+ minutes and peg the CPU: a large stored file's bytes were being loaded through normal Entity Framework change tracking on every status check instead of being streamed, which is especially slow for a file in the hundreds of megabytes. This streaming fix now also applies to the PostgreSQL deployment flavor, not just SQL Server.
- Fixed a large FTP upload sometimes being scanned as a truncated file (e.g. a 200,000-row export processed as if it only had ~32,000 rows) when a poll cycle landed mid-upload -- the Worker Service now waits for a file's size to be stable across two consecutive polls before processing it.
- Fixed the Worker Service silently exiting with no error logged when its startup recovery failed (e.g. the database wasn't reachable yet) -- the failure is now logged and the service falls through to its normal polling loop instead of stopping.
- Fixed a Worker Service Docker build failure (the Dockerfile was excluded from its own build context by `.dockerignore`).
- Fixed the PostgreSQL and SQLite deployment flavors always reading security settings against a SQL Server connection regardless of the configured database provider, silently falling back to default settings on every startup.


---

### ⚠️ Known Issues

- Processing a very large audit trail file (multiple millions of records) is memory-intensive; see `Installation Manual.md`'s hardware sizing guidance. The SQLite and PostgreSQL Docker flavors are intended for trying the app and functional testing, not as a performance benchmark or a production deployment.

---

### 📦 Third-Party Licenses

This project is licensed under the Business Source License 1.1 (see `LICENSE` at the repo
root). The full list of third-party NuGet packages, bundled client-side libraries, runtime
components and their licenses for this release is recorded in the
[Software Bill of Materials for v0.8](SBOM/SBOM%20v0.8.md) (`ReleaseNotes/SBOM/SBOM v0.8.md`).
It also describes how to regenerate the inventory.
