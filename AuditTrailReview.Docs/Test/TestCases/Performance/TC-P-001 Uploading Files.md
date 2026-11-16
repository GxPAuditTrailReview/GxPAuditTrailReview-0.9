


## Performance tests banchmark:

### Uploading scanning audit trails - local laptop  v.0.9

| File Size | Records | Processing Time	|
|-----------|---------|------------------|
| 2 KB      | 12      | 1s				 |
| 6 KB      | 37      | 1s              |
| 7 MB      | 50K     | 10s              |
| 14 MB     | 100K    | 30s              |
| 28 MB     | 200K    | 28s              |
| 42 MB     | 300K    | 59s              |
| 70 MB     | 500K    | 1 min 30s        |
| 150 MB    | 1M      | 4 min 30s        |
| 714 MB    | 5M	  | 20 min 47s       |



---

## Performance test script


1. WebApp upload, small (<10 MB): processed synchronously inline - the time of upload should be less than 31 seconds.

2. WebApp upload, medium (10 MB - 50 MB): processed asynchronously - the time of upload should be less than 2 minutes.

3. WebApp upload, large (> 50 MB) till 750 MB (786,432,000 bytes): processed asynchronously - the time of upload should be less than 25 minutes.


### Manual test steps

1. **Tier 1 - synchronous (<=10 MB).** Upload a small sample (e.g. the "6 KB / 37 records" file) via the WebApp Scan page. Start a stopwatch the moment "Run Scan" is clicked; stop it when the page redirects to Scan/Details.
   - Pass: redirect happens in under 31 seconds.

2. **Tier 2 - in-process async (10-50 MB).** Upload a file in this range (e.g. "42 MB / 300K records"). Confirm the page returns immediately with a "queued" message instead of blocking for the full processing time, then poll Scan/Index until the job's status goes Queued -> Processing -> Completed; measure the total elapsed time.
   - Pass: completes in under 2 minutes.

3. **Tier 3 - Worker Service (50 MB-750 MiB).** Upload a file in this range (e.g. the "714 MB / 5M records" file). Confirm the WebApp returns immediately and the Worker Service log shows it picking up and processing the job.
   - Pass: completes in under 25 minutes.

4. **Boundary check - over the hard ceiling.** Select a file just over 786,432,000 bytes (750 MiB).
   - Pass: the client-side warning and "file too large" modal (#ftpTooLargeModal) appear before any upload is attempted; no `ScanUploadJob` row is created.

5. **Boundary check - just under the ceiling.** Upload a file just under 750 MiB (e.g. ~749 MiB).
   - Pass: accepted and queued as `Tier.Worker`, same as step 3.

6. **Worker Service restart resilience (optional).** While a large `Tier.Worker` job is `Processing`, restart the Worker Service.
   - Pass: on startup the job is reset from `Processing` back to `Queued` (`RecoverStuckWorkerJobsAsync`) and completes on the next poll instead of being lost.

7. **Regression guard against total `ScanResult` table size.** With several large scans already ingested (so `dbo.ScanResult` holds millions of rows combined), upload and process a tiny file (<1 MB) again.
   - Pass: processing time stays close to the small-file baseline in the table above (~1s) - confirms the earlier flat ~25s small-file regression (missing `ScanResult.ScanID` index) has not resurfaced.


Performance Tests