
## Performance tests banchmark:

### Scan Details - local laptop  v.0.9

Loading scan datails can contain million recods so the performance shoule be carefully watched

| Records | Response Time |
|---------|----------------|
| 36      | 1s            |
| 50K     | 1s            |
| 100K    | 1s            |
| 200K    | 1s            |
| 300K    | 2s            |
| 500K    | 2s            |
| 1M      | 7s            |
| 5M      | 22s           |

---

## Performance test script

### Manual test steps

1. **Baseline - small scan.** From System/Details, open a scan with ~36-50K records. Start a stopwatch on click; stop when the Details page is fully rendered (results tab populated).
   - Pass: under ~2 seconds.

2. **Large scan.** Open the largest ingested scan (e.g. 5M+ records), same timing method.
   - Pass: response time stays close to the small-scan baseline (per the table above, ~22s is the current worst case) - load time should not scale with the scan's own record count, since every query filters by a single `ScanID`.

3. **Pagination.** On the large scan, page through the Scan Results tab (`ResultsPageSize = 8`) to a middle page and the last page.
   - Pass: every page loads in roughly the same time as page 1 (confirms `Skip`/`Take` plus the `ScanID` index keep each page cheap rather than an increasingly expensive scan as pages grow).

4. **Potential Findings tab.** Switch to the Potential Findings tab on the large scan.
   - Pass: similar load time to the main Results tab (same `ScanID`-filtered query pattern).

5. **AI Analysis tab / poll.** Open the AI Analysis tab while analysis is pending and observe the `OnGetAiAnalysisAsync` poll.
   - Pass: each poll response returns quickly (sub-second) and adds no noticeable load even while a large scan is open.

6. **Excel export.** Export the large scan via `OnGetExportExcelAsync`.
   - Pass: export completes in a reasonable time and the file has the expected row count. This one streams every `ScanResult` row, so it is expected to scale with record count, unlike the paginated view - document the actual time observed rather than treating growth here as a regression.

7. **Sign & close / reopen.** Sign & close, then reopen, the large scan.
   - Pass: both actions complete without noticeable delay (they only touch Scan-level/signature rows, not `ScanResult`).

8. **Regression guard.** Immediately after ingesting a new very large scan, re-run step 1 (small scan baseline).
   - Pass: small-scan response time is unaffected by how much total data now exists in `dbo.ScanResult` - confirms `IX_ScanResult_ScanID`/`IX_ScanResultAI_ScanID` keep lookups independent of total table size.