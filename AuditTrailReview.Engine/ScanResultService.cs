                using AuditTrailReview.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuditTrailReview.Engine
{
    public static class ScanResultService
    {
        /// <summary>
        /// Applies rules to mapped audit trail rows and generates scan results with findings.
        /// </summary>
        /// <param name="mappedRows">The mapped audit trail information</param>
        /// <param name="scanId">The scan ID to associate with results</param>
        /// <param name="IsRule3Enabled">Whether to check if OldValue and NewValue differ</param>
        /// <param name="IsRule4Enabled">Whether to check timestamps are in chronological order</param>
        /// <param name="IsRule5Enabled">Whether to check that all mandatory fields are present and non-empty</param>
        /// <param name="IsRule11Enabled">Whether to check that create operations do not have OldValue</param>
        /// <param name="IsRule13Enabled">Whether to check that update operations have a reason for update</param>
        /// <param name="IsRule14Enabled">Whether to check that delete operations have a reason for deletion</param>
        /// <param name="IsRule15Enabled">Whether to check that delete operations retain the old value (OldValue cannot be empty)</param>
        /// <param name="IsRule22Enabled">Whether to check if username matches a known shared/generic/system account pattern (D1)</param>
        /// <param name="IsRule24Enabled">Whether to check that an Update's OldValue matches the immediately preceding record's NewValue for the same object/field (A2, record-chain continuity)</param>
        /// <param name="IsRule27Enabled">Whether to check that the user who approved/reviewed an object is not the same user who created or modified it (B8, segregation of duties)</param>
        /// <param name="IsRule28Enabled">Whether to check for an oscillating A/B/A/B value pattern on the same object/field (E2, oscillating value pattern detection)</param>
        /// <param name="IsRule29Enabled">Whether to check for an Update that nullifies a genuine value to blank/NULL or the literal "0" (E3, nullifying data)</param>
        /// <returns>A list of ScanResultInfo objects with findings applied</returns>
        public static List<ScanResultInfo> ApplyRulesAndGenerateResults(
            List<AuditTrailInfo> mappedRows,
            int scanId,
            bool IsRule3Enabled,
            bool IsRule4Enabled,
            bool IsRule5Enabled,
            bool IsRule7Enabled,
            bool IsRule9Enabled,
            bool IsRule10Enabled,
            bool IsRule11Enabled,
            bool IsRule13Enabled,
            bool IsRule14Enabled,
            bool IsRule15Enabled,
            bool IsRule16Enabled,
            bool IsRule17Enabled,
            bool IsRule20Enabled,
            bool IsRule21Enabled,
            bool IsRule22Enabled,
            bool IsRule24Enabled,
            bool IsRule27Enabled,
            bool IsRule28Enabled,
            bool IsRule29Enabled,
            EngineSettings? settings = null
            )
        {
            if (mappedRows == null || !mappedRows.Any())
            {
                return new List<ScanResultInfo>();
            }

            settings ??= new EngineSettings();

            // Entries here can be wildcard prefix patterns (e.g. "contractor*"), which
            // SystemAccountHeuristics.IsLikelySystemAccount matches structurally rather than
            // via exact-value lookup.
            var additionalSystemAccountPatterns = (settings.AdditionalSystemAccountPatterns ?? string.Empty)
                .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Resolve Rule 17 (C3)'s configured time zone once per scan, rather than per row.
            // ResolveWorkdayTimeZone falls back to UTC for a null/empty/unrecognized
            // WorkdayTimeZone -- see that method and EngineSettings.WorkdayTimeZone for why
            // that's a safe default rather than a scan-time failure.
            var workdayTimeZone = ResolveWorkdayTimeZone(settings.WorkdayTimeZone);

            // Get duplicate composite keys if Rule 7 is enabled. The key includes the record
            // identity (ObjectType+Object+Field, via GetFieldHistoryKey) as well as
            // UserID/Timestamp/OldValue/NewValue/Operation -- a user-reported bug found this key
            // originally left the record identity out entirely, so a legitimate bulk operation
            // (e.g. a script setting 10 different pieces of equipment to the same new status in
            // the same second) had every one of its rows misflagged as "duplicates" of each
            // other, even though each row changed a genuinely different record. A duplicate now
            // means the same field on the same record was logged as changing the same way, by
            // the same user, at the same instant, more than once -- e.g. an import that ingested
            // the same audit trail line twice -- not merely "different records that happened to
            // change identically at once" (which is what rule E1's mass-change burst detection
            // is for instead).
            HashSet<string> duplicateRecordKeys = new(StringComparer.OrdinalIgnoreCase);
            if (IsRule7Enabled)
            {
                duplicateRecordKeys = mappedRows
                    .Where(r => r.UserID != null || r.Timestamp.HasValue || r.OldValue != null || r.NewValue != null)
                    .GroupBy(r => $"{GetFieldHistoryKey(r)}|{r.UserID}|{r.Timestamp:O}|{r.OldValue}|{r.NewValue}|{r.Operation}", StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Get mass change indexes if Rule 21 is enabled (MassChangeThresholdCount+
            // records within MassChangeWindowSeconds -- both configurable via EngineSettings,
            // rather than the fixed 10-records/1-second burst this used to hard-code).
            // Grouped by UserID first (a user-reported fix): a "mass change" is meant to catch
            // one user/script making an abnormally fast run of edits, not an incidental
            // coincidence of several different users each making one unrelated change in the
            // same window -- the latter says nothing about any single actor's behavior. Rows
            // with no UserID at all share one "no user" bucket, same as this rule's own
            // pre-existing tolerance for missing UserID elsewhere.
            HashSet<int> massChangeIndexes = new();
            if (IsRule21Enabled)
            {
                var timedRowsByUser = mappedRows
                    .Select((r, i) => (r.Timestamp, Index: i, UserID: r.UserID ?? string.Empty))
                    .Where(x => x.Timestamp.HasValue)
                    .GroupBy(x => x.UserID, StringComparer.OrdinalIgnoreCase);

                foreach (var userGroup in timedRowsByUser)
                {
                    var timedRows = userGroup.OrderBy(x => x.Timestamp!.Value).ToList();

                    int left = 0;
                    for (int right = 0; right < timedRows.Count; right++)
                    {
                        while ((timedRows[right].Timestamp!.Value - timedRows[left].Timestamp!.Value).TotalSeconds > settings.MassChangeWindowSeconds)
                            left++;

                        if (right - left + 1 >= settings.MassChangeThresholdCount)
                        {
                            for (int k = left; k <= right; k++)
                                massChangeIndexes.Add(timedRows[k].Index);
                        }
                    }
                }
            }

            // Get record-chain-continuity breaks if Rule 24 (A2) is enabled: for each
            // object/field (ObjectType+Object+Field -- see GetFieldHistoryKey, deliberately
            // finer-grained than GetRecordKey since two different fields on the same object
            // carry independent value chains), walk that field's rows in actual chronological
            // order within this scan and check that each Update's OldValue matches the
            // immediately preceding row's NewValue. A mismatch signals a gap in the audit
            // trail itself -- a purged/deleted row, export truncation, or tampering -- which
            // is exactly what B1-B7's purely single-row checks structurally cannot see, since
            // they only ever look within one row, never across a field's history. Rows with no
            // Object at all are skipped entirely (there's no reliable way to know they share a
            // "same object" identity), and rows with no Timestamp are skipped too (they can't
            // be reliably placed in the chronological chain).
            var chainContinuityBreaks = new Dictionary<int, (string ExpectedValue, string ActualValue)>();
            if (IsRule24Enabled)
            {
                var keyedRows = mappedRows
                    .Select((r, i) => (Row: r, Index: i))
                    .Where(x => !string.IsNullOrEmpty(x.Row.Object) && x.Row.Timestamp.HasValue);

                foreach (var group in keyedRows.GroupBy(x => GetFieldHistoryKey(x.Row), StringComparer.OrdinalIgnoreCase))
                {
                    var ordered = group.OrderBy(x => x.Row.Timestamp!.Value).ToList();
                    string? runningValue = null;

                    foreach (var item in ordered)
                    {
                        var row = item.Row;
                        bool isUpdate = string.Equals(row.Operation, "Update", StringComparison.OrdinalIgnoreCase);

                        if (isUpdate && !string.IsNullOrEmpty(row.OldValue) && !string.IsNullOrEmpty(runningValue) &&
                            !string.Equals(row.OldValue.Trim(), runningValue.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            chainContinuityBreaks[item.Index] = (runningValue, row.OldValue);
                        }

                        if (!string.IsNullOrEmpty(row.NewValue))
                            runningValue = row.NewValue;
                        else if (string.Equals(row.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
                            runningValue = null; // the chain ends here -- nothing "after" a deletion to compare against
                    }
                }
            }

            // Get segregation-of-duties violations if Rule 27 (B8) is enabled: for each
            // object (ObjectType+Object -- see GetRecordKey), collect the set of users who
            // performed a Create or Update on it, then flag any Approve/Review row on that same
            // object performed by one of those same users. Recognizes exactly "Create"/"Update"
            // as the create/modify step and exactly "Approve"/"Review" as the approval step
            // (case-insensitive literal matching) -- the same exact-literal-matching style
            // B1-B7 already use for Create/Update/Delete, with no fuzzy synonym list
            // ("Approved"/"Reviewed"/"Sign" etc. are not recognized). Rows with no Object or no
            // UserID are skipped -- there's no reliable "same object" or "same person" identity
            // to check. The conflicting Create/Update does not need to be the *immediately
            // preceding* row -- any Create/Update by that user on that object, anywhere in this
            // scan, is enough to flag a later Approve/Review by the same person, since
            // segregation of duties is a standing requirement, not a one-time adjacency check.
            var segregationOfDutiesViolations = new HashSet<int>();
            if (IsRule27Enabled)
            {
                var creatorsOrModifiersByObject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in mappedRows)
                {
                    if (string.IsNullOrEmpty(row.Object) || string.IsNullOrEmpty(row.UserID))
                        continue;

                    if (string.Equals(row.Operation, "Create", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(row.Operation, "Update", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = GetRecordKey(row);
                        if (!creatorsOrModifiersByObject.TryGetValue(key, out var users))
                        {
                            users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            creatorsOrModifiersByObject[key] = users;
                        }
                        users.Add(row.UserID);
                    }
                }

                for (int i = 0; i < mappedRows.Count; i++)
                {
                    var row = mappedRows[i];
                    if (string.IsNullOrEmpty(row.Object) || string.IsNullOrEmpty(row.UserID))
                        continue;

                    bool isApprovalStep = string.Equals(row.Operation, "Approve", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(row.Operation, "Review", StringComparison.OrdinalIgnoreCase);
                    if (!isApprovalStep)
                        continue;

                    var key = GetRecordKey(row);
                    if (creatorsOrModifiersByObject.TryGetValue(key, out var users) && users.Contains(row.UserID))
                    {
                        segregationOfDutiesViolations.Add(i);
                    }
                }
            }

            // Get oscillating-value-pattern detections if Rule 28 (E2) is enabled: for each
            // object/field (ObjectType+Object+Field -- see GetFieldHistoryKey, the same key
            // A2 uses), walk that field's Create/Update rows in actual chronological order
            // within this scan and look for a value sequence that repeats exactly two values
            // in strict alternation -- A, B, A, B -- which may indicate an unstable process or
            // an unusual pattern of back-and-forth corrections. This is distinct from A2's
            // concern (a broken chain -- OldValue not matching the prior NewValue) and from
            // E1's concern (too many changes too fast): E2 flags a value that keeps reverting,
            // regardless of how much time separates the flips. Rows with no Object at all, or
            // no Timestamp, are excluded (same as A2). A Delete resets the tracked value
            // sequence for that object/field, since there is no continuous history across a
            // deletion to call "oscillating" -- a value re-established after a deletion starts
            // a fresh sequence. Values are compared trimmed and case-insensitively, consistent
            // with A2/B1's own value-comparison style elsewhere in this engine. Once a 4-value
            // alternating window (A, B, A, B) is found, the row completing it is flagged; a
            // longer run (A, B, A, B, A, B, ...) flags each subsequent row that continues the
            // pattern too, not just the first four values.
            var oscillatingValuePatterns = new Dictionary<int, (string ValueA, string ValueB)>();
            if (IsRule28Enabled)
            {
                var keyedRowsForOscillation = mappedRows
                    .Select((r, i) => (Row: r, Index: i))
                    .Where(x => !string.IsNullOrEmpty(x.Row.Object) && x.Row.Timestamp.HasValue);

                foreach (var group in keyedRowsForOscillation.GroupBy(x => GetFieldHistoryKey(x.Row), StringComparer.OrdinalIgnoreCase))
                {
                    var ordered = group.OrderBy(x => x.Row.Timestamp!.Value).ToList();
                    var valueSequence = new List<(int Index, string Value)>();

                    void FlushValueSequence()
                    {
                        for (int i = 0; i + 3 < valueSequence.Count; i++)
                        {
                            var v0 = valueSequence[i].Value;
                            var v1 = valueSequence[i + 1].Value;
                            var v2 = valueSequence[i + 2].Value;
                            var v3 = valueSequence[i + 3].Value;

                            if (string.Equals(v0, v2, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(v1, v3, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(v0, v1, StringComparison.OrdinalIgnoreCase))
                            {
                                oscillatingValuePatterns[valueSequence[i + 3].Index] = (v0, v1);
                            }
                        }
                    }

                    foreach (var item in ordered)
                    {
                        var row = item.Row;

                        if (string.Equals(row.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
                        {
                            FlushValueSequence();
                            valueSequence.Clear();
                            continue;
                        }

                        bool isCreateOrUpdate = string.Equals(row.Operation, "Create", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(row.Operation, "Update", StringComparison.OrdinalIgnoreCase);

                        if (isCreateOrUpdate && !string.IsNullOrEmpty(row.NewValue))
                        {
                            valueSequence.Add((item.Index, row.NewValue.Trim()));
                        }
                    }

                    FlushValueSequence();
                }
            }

            var scanResults = new List<ScanResultInfo>();
            int rowIndex = 0;

            // Rule 4 (C1) precomputation: chronological order is checked *within each record*
            // rather than across the whole file -- a file that interleaves multiple records'
            // events would otherwise trigger false positives on every record boundary. A
            // "record" is identified by ObjectType+Object (see GetRecordKey); rows where both
            // are empty (e.g. a source system that doesn't populate them) all fall into one
            // group, reproducing the previous whole-file behavior for that data automatically.
            //
            // The expected direction (ascending/descending) is auto-detected independently for
            // each record from its own rows' predominant order, the same way it previously was
            // for the whole file.
            Dictionary<string, bool> recordIsDescendingOrder = new(StringComparer.OrdinalIgnoreCase);
            if (IsRule4Enabled)
            {
                foreach (var group in mappedRows.GroupBy(GetRecordKey, StringComparer.OrdinalIgnoreCase))
                {
                    var groupRows = group.ToList();
                    bool isDescendingOrder = true; // Default to descending
                    for (int i = 0; i < Math.Min(10, groupRows.Count - 1); i++)
                    {
                        if (groupRows[i].Timestamp.HasValue && groupRows[i + 1].Timestamp.HasValue)
                        {
                            if (groupRows[i].Timestamp.Value < groupRows[i + 1].Timestamp.Value)
                            {
                                isDescendingOrder = false;
                                break;
                            }
                            else if (groupRows[i].Timestamp.Value > groupRows[i + 1].Timestamp.Value)
                            {
                                isDescendingOrder = true;
                                break;
                            }
                        }
                    }
                    recordIsDescendingOrder[group.Key] = isDescendingOrder;
                }
            }

            var recordPreviousTimestamp = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            foreach (var mappedRow in mappedRows)
            {
                var findings = new List<string>();

                // Rule 3: Update must modify the value (before ≠ after) -  Check if OldValue and NewValue are the same (modify must change the value)
                if (IsRule3Enabled && 
                    string.Equals(mappedRow.Operation, "Update", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(mappedRow.OldValue) && 
                    !string.IsNullOrEmpty(mappedRow.NewValue) &&
                    string.Equals(mappedRow.OldValue, mappedRow.NewValue, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"[B1] Update operation did not change the value — before and after are identical: '{mappedRow.OldValue}'");
                }

                // Rule 4: Check if timestamps are in logical chronological order *within this
                // record* (grouped by ObjectType+Object -- see the precomputation above), not
                // across the whole file. The expected direction (ascending/descending) was
                // auto-detected per record above.
                if (IsRule4Enabled && mappedRow.Timestamp.HasValue)
                {
                    var recordKey = GetRecordKey(mappedRow);
                    bool isDescendingOrder = !recordIsDescendingOrder.TryGetValue(recordKey, out var descending) || descending;

                    if (recordPreviousTimestamp.TryGetValue(recordKey, out var previousTimestampForRecord))
                    {
                        bool outOfOrder = isDescendingOrder
                            ? mappedRow.Timestamp.Value > previousTimestampForRecord   // newest-first: each next must be ≤ previous
                            : mappedRow.Timestamp.Value < previousTimestampForRecord;  // oldest-first: each next must be ≥ previous

                        if (outOfOrder)
                        {
                            var direction = isDescendingOrder
                                ? "descending (newest first)"
                                : "ascending (oldest first)";
                            findings.Add($"[C1] Timestamp out of chronological order (detected order: {direction}) — record: {mappedRow.Timestamp.Value:yyyy-MM-dd HH:mm:ss}, previous: {previousTimestampForRecord:yyyy-MM-dd HH:mm:ss}");
                        }
                    }

                    recordPreviousTimestamp[recordKey] = mappedRow.Timestamp.Value;
                }

                // Rule 5: Mandatory fields: user, timestamp, action must exist and must not be empty
                if (IsRule5Enabled)
                {
                    var missingFields = new List<string>();

                    if (string.IsNullOrWhiteSpace(mappedRow.Operation))
                        missingFields.Add(nameof(mappedRow.Operation));

                    if (!mappedRow.Timestamp.HasValue)
                        missingFields.Add(nameof(mappedRow.Timestamp));

                    if (string.IsNullOrWhiteSpace(mappedRow.UserID))
                        missingFields.Add(nameof(mappedRow.UserID));

                    if (missingFields.Count > 0)
                        findings.Add($"[A1] Missing mandatory fields: {string.Join(", ", missingFields)}");
                }

                // Rule 7: Check for the same field on the same record being logged as changing
                // the same way, by the same user, at the same instant, more than once (see the
                // precomputation above for why the record identity -- ObjectType+Object+Field --
                // has to be part of the key, not just UserName/Timestamp/OldValue/NewValue/Operation).
                if (IsRule7Enabled) 
                {
                    var compositeKey = $"{GetFieldHistoryKey(mappedRow)}|{mappedRow.UserID}|{mappedRow.Timestamp:O}|{mappedRow.OldValue}|{mappedRow.NewValue}|{mappedRow.Operation}";
                    if (duplicateRecordKeys.Contains(compositeKey))
                    {
                        findings.Add($"[A3] Duplicate record detected — same record ({mappedRow.ObjectType ?? "(no object type)"} '{mappedRow.Object ?? "(no object id)"}', field '{mappedRow.Field ?? "(no field)"}'), UserName: '{mappedRow.UserID}', DataTime: '{mappedRow.Timestamp:yyyy-MM-dd HH:mm:ss}', Operation: '{mappedRow.Operation}', OldValue: '{mappedRow.OldValue}', NewValue: '{mappedRow.NewValue}'");
                    }
                }

                // Rule 9: Update must have both OldValue and NewValue present
                if (IsRule9Enabled &&
                    string.Equals(mappedRow.Operation, "Update", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(mappedRow.OldValue) || string.IsNullOrEmpty(mappedRow.NewValue)))
                {
                    var missingValueFields = new List<string>();
                    if (string.IsNullOrEmpty(mappedRow.OldValue))
                        missingValueFields.Add(nameof(mappedRow.OldValue));
                    if (string.IsNullOrEmpty(mappedRow.NewValue))
                        missingValueFields.Add(nameof(mappedRow.NewValue));
                    findings.Add($"[B2] Update operation is missing required value fields: {string.Join(", ", missingValueFields)}");
                }


                // Rule 10: Create operation must have NewValue present
                if (IsRule10Enabled &&
                    string.Equals(mappedRow.Operation, "Create", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(mappedRow.NewValue))
                {
                    findings.Add($"[B3] Create operation is missing the new value (NewValue is empty)");
                }

                // Rule 11: Create operation must not have OldValue
                // Values like "(null)" or "null" are treated as empty — some systems use these as placeholders
                if (IsRule11Enabled &&
                    string.Equals(mappedRow.Operation, "Create", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(mappedRow.OldValue) &&
                    !mappedRow.OldValue.Trim().Equals("null", StringComparison.OrdinalIgnoreCase) &&
                    !mappedRow.OldValue.Trim().Equals("(null)", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"[B4] Create operation must not have an old value — OldValue: '{mappedRow.OldValue}'");
                }

                // Rule 13: Update operation must have a reason for update
                if (IsRule13Enabled &&
                    string.Equals(mappedRow.Operation, "Update", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(mappedRow.Reason))
                {
                    findings.Add($"[B5] Update operation is missing a reason for update (Reason is empty)");
                }

                // Rule 14: Delete operation must have a reason for deletion
                if (IsRule14Enabled &&
                    string.Equals(mappedRow.Operation, "Delete", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(mappedRow.Reason))
                {
                    findings.Add($"[B6] Delete operation is missing a reason for deletion (Reason is empty)");
                }

                // Rule 15: Delete operation must retain the old value (OldValue cannot be empty)
                if (IsRule15Enabled &&
                    string.Equals(mappedRow.Operation, "Delete", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(mappedRow.OldValue))
                {
                    findings.Add($"[B7] Delete operation must retain the old value - OldValue is empty");
                }

                // Rule 16: No timestamps in the future — Timestamp cannot be later than system datetime.
                // Compared against UtcNow, not local server time (NFR-042), after resolving the
                // source timestamp to a true UTC instant first (see ResolveTimestampToUtc for why
                // that's needed for every Kind, not just Local -- an Unspecified-kind timestamp
                // used to be compared raw against UtcNow, silently assuming it was already UTC;
                // it's now assumed to be local plant time in workdayTimeZone instead, matching
                // rule C3's assumption so the two rules never silently disagree about the same
                // ambiguous timestamp).
                if (IsRule16Enabled && mappedRow.Timestamp.HasValue)
                {
                    var comparableTimestampUtc = ResolveTimestampToUtc(mappedRow.Timestamp.Value, workdayTimeZone);

                    if (comparableTimestampUtc > DateTime.UtcNow)
                    {
                        var kindNote = comparableTimestampUtc != mappedRow.Timestamp.Value
                            ? $" ({mappedRow.Timestamp.Value.Kind} kind{FormatRawSourceNote(mappedRow.Timestamp.Value.Kind, mappedRow.TimestampRaw)}, interpreted as {(mappedRow.Timestamp.Value.Kind == DateTimeKind.Unspecified ? workdayTimeZone.Id : "an explicit offset")} and converted to UTC as {comparableTimestampUtc:yyyy-MM-dd HH:mm:ss} for this comparison)"
                            : string.Empty;
                        findings.Add($"[C2] Timestamp is in the future — value: {mappedRow.Timestamp.Value:yyyy-MM-dd HH:mm:ss}{kindNote}, system time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                    }
                }

                // Rule 17: Detect deviations from normal working hours (night/weekend changes).
                // The source timestamp is resolved to a true UTC instant first (ResolveTimestampToUtc,
                // shared with rule C2's future-timestamp check) and then converted into
                // workdayTimeZone (resolved once above from EngineSettings.WorkdayTimeZone, falling
                // back to UTC) before WorkdayStartHour/WorkdayEndHour/weekend are evaluated against
                // it. For an Unspecified-kind timestamp (no timezone info in the source -- the
                // common case) this is a no-op round trip: it's assumed to already be local plant
                // time in workdayTimeZone, so it's evaluated at face value rather than being
                // shifted as if it were UTC (a real, user-reported bug -- see ResolveTimestampToUtc).
                if (IsRule17Enabled && mappedRow.Timestamp.HasValue)
                {
                    var dt = ConvertToWorkdayTimeZone(mappedRow.Timestamp.Value, workdayTimeZone);
                    bool isWeekend = !settings.AllowWeekends &&
                                     (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday);
                    bool isOutsideWorkingHours = dt.Hour < settings.WorkdayStartHour || dt.Hour >= settings.WorkdayEndHour;

                    if (isWeekend || isOutsideWorkingHours)
                    {
                        var reason = isWeekend
                            ? $"weekend ({dt.DayOfWeek} {workdayTimeZone.Id})"
                            : $"outside working hours ({settings.WorkdayStartHour:D2}:00\u2013{settings.WorkdayEndHour:D2}:00 {workdayTimeZone.Id})";
                        // Only note the conversion when it actually changed the wall-clock reading
                        // (a genuinely UTC- or offset-anchored timestamp) -- an Unspecified-kind
                        // timestamp round-trips to the same value, so there is nothing to explain.
                        // When it did shift, also name *why* (the parsed Kind) and, via
                        // FormatRawSourceNote, the exact raw source text that carried an explicit
                        // UTC/GMT/Z marker or numeric offset -- so a report that a timestamp
                        // "shouldn't have shifted" is answerable straight from the finding text.
                        var conversionNote = dt != mappedRow.Timestamp.Value
                            ? $" ({mappedRow.Timestamp.Value.Kind} kind{FormatRawSourceNote(mappedRow.Timestamp.Value.Kind, mappedRow.TimestampRaw)}, evaluated in {workdayTimeZone.Id} as {dt:yyyy-MM-dd HH:mm:ss})"
                            : string.Empty;
                        findings.Add($"[C3] Change recorded outside normal working hours — value: {mappedRow.Timestamp.Value:yyyy-MM-dd HH:mm:ss}{conversionNote}, reason: {reason}");
                    }
                }


                // Rule 20: Username must contain at least the configured number of characters
                if (IsRule20Enabled && !string.IsNullOrEmpty(mappedRow.UserID) && mappedRow.UserID.Length < settings.UsernameMinLength)
                {
                    findings.Add($"[D2] Username is too short — value: '{mappedRow.UserID}' ({mappedRow.UserID.Length} character(s), minimum {settings.UsernameMinLength} required)");
                }

                // Rule 22: Check if username matches a known shared/generic/system account
                // pattern (independent of LoginNameHeuristics -- see SystemAccountHeuristics' XML doc)
                if (IsRule22Enabled && SystemAccountHeuristics.IsLikelySystemAccount(mappedRow.UserID ?? string.Empty, additionalSystemAccountPatterns))
                {
                    findings.Add($"[D1] Username '{mappedRow.UserID}' appears to be a shared/generic/system account, not an individual login");
                }

                // Rule 21: Mass changes — MassChangeThresholdCount or more records modified
                // within MassChangeWindowSeconds *by the same user* (see the precomputation
                // above for why: a burst is meant to catch one user/script running abnormally
                // fast, not several unrelated users each happening to make one change in the
                // same window). The finding's timestamp only shows a milliseconds component
                // when the source data actually declared one (see HasSubSecondPrecision) -- a
                // user-reported concern that ".fff" always printing ".000" for a plain
                // "HH:mm:ss" source timestamp implied a precision that was never actually
                // recorded, rather than a genuine zero-millisecond reading.
                if (IsRule21Enabled && massChangeIndexes.Contains(rowIndex))
                {
                    var timestampDisplay = HasSubSecondPrecision(mappedRow.TimestampRaw)
                        ? mappedRow.Timestamp!.Value.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        : mappedRow.Timestamp!.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    findings.Add($"[E1] Mass change detected — record is part of a burst of {settings.MassChangeThresholdCount} or more modifications by the same user ('{mappedRow.UserID}') within {settings.MassChangeWindowSeconds:0.###} second(s) at {timestampDisplay}");
                }

                // Rule 24 (A2): This Update's OldValue does not match the immediately
                // preceding record's NewValue for the same object/field within this scan -- a
                // signal of a gap in the audit trail itself (deleted/purged rows, export
                // truncation, or tampering), not a defect in this row taken alone.
                if (IsRule24Enabled && chainContinuityBreaks.TryGetValue(rowIndex, out var chainBreak))
                {
                    findings.Add($"[A2] Record-chain continuity broken — OldValue '{chainBreak.ActualValue}' does not match the previous record's NewValue '{chainBreak.ExpectedValue}' for this object/field");
                }

                // Rule 27 (B8): the same user must not both create/modify an object and
                // approve/review it (segregation of duties).
                if (IsRule27Enabled && segregationOfDutiesViolations.Contains(rowIndex))
                {
                    findings.Add($"[B8] User '{mappedRow.UserID}' both created/modified and approved/reviewed the same object — segregation of duties violation");
                }

                // Rule 28 (E2): the same object/field's value has flipped back and forth
                // between exactly two values -- A→B→A→B -- which may indicate an unstable
                // process or an unusual pattern of corrections, not a one-off legitimate
                // change.
                if (IsRule28Enabled && oscillatingValuePatterns.TryGetValue(rowIndex, out var oscillation))
                {
                    findings.Add($"[E2] Oscillating value pattern detected — value alternates between '{oscillation.ValueA}' and '{oscillation.ValueB}' for this object/field");
                }

                // Rule 29 (E3): an Update changed a genuine prior value to blank/NULL or the
                // literal value "0" -- not itself a violation (a real correction can
                // legitimately zero out an erroneous value), but worth a second look: it may
                // indicate an earlier data-entry error or a misconfiguration being silently
                // reset rather than properly investigated and documented. Only fires when
                // OldValue was itself a real, non-blank, non-zero value -- going from "0" or
                // blank to "0" or blank isn't nullifying anything. Compared trimmed; "0" is
                // matched literally (not "0.0"/"0.00" or other numeric-zero spellings).
                if (IsRule29Enabled &&
                    string.Equals(mappedRow.Operation, "Update", StringComparison.OrdinalIgnoreCase))
                {
                    var trimmedOldForNullify = mappedRow.OldValue?.Trim();
                    var trimmedNewForNullify = mappedRow.NewValue?.Trim();
                    bool oldValueWasGenuine = !string.IsNullOrEmpty(trimmedOldForNullify) && trimmedOldForNullify != "0";
                    bool newValueIsNullified = string.IsNullOrEmpty(trimmedNewForNullify) || trimmedNewForNullify == "0";

                    if (oldValueWasGenuine && newValueIsNullified)
                    {
                        var nullifiedDescription = string.IsNullOrEmpty(trimmedNewForNullify) ? "blank" : "'0'";
                        findings.Add($"[E3] Value nullified — changed from '{mappedRow.OldValue}' to {nullifiedDescription}");
                    }
                }






                var scanResult = new ScanResultInfo
                {
                    ScanID           = scanId,
                    UniqueIdentifier = mappedRow.UniqueIdentifier,
                    Timestamp        = mappedRow.Timestamp,
                    UserID           = mappedRow.UserID,
                    ObjectType       = mappedRow.ObjectType,
                    Object           = mappedRow.Object,
                    Field            = mappedRow.Field,
                    Source           = mappedRow.Source,
                    Operation        = mappedRow.Operation,
                    OldValue         = mappedRow.OldValue,
                    NewValue         = mappedRow.NewValue,
                    Reason           = mappedRow.Reason,
                    Finding          = string.Join("; ", findings),
                    // Mirrors !string.IsNullOrEmpty(Finding) -- set here (not a SQL computed
                    // column) so a filtered index on it works identically whether the query runs
                    // against SQL Server or EF Core's InMemory provider in tests. The column and
                    // its filtered index (IX_ScanResult_HasFinding_ReviewStatus) ship in the
                    // baseline schema snapshot (ATR 0.8 20261020-SQL2025/2012.sql) -- added because
                    // the Dashboard's Total/Unreviewed findings counts used to be a full scan over
                    // Finding, an nvarchar(max) column SQL Server cannot index directly.
                    HasFinding       = findings.Count > 0
                };

                scanResults.Add(scanResult);
                rowIndex++;
            }

            return scanResults;
        }

        /// <summary>
        /// Identifies the "record" (business entity) a row belongs to, for Rule C1's
        /// per-record chronological-order check -- as ObjectType+Object. Rows where neither is
        /// populated all collapse into one shared group, matching C1's previous whole-file
        /// behavior for source data that doesn't carry object identity.
        /// </summary>
        private static string GetRecordKey(AuditTrailInfo row) =>
            $"{row.ObjectType ?? string.Empty}|{row.Object ?? string.Empty}";

        /// <summary>
        /// Identifies the specific object *attribute* (not just the object as a whole) a row
        /// belongs to, for Rule A2's record-chain-continuity check -- as
        /// ObjectType+Object+Field. Deliberately finer-grained than <see cref="GetRecordKey"/>
        /// (which C1 uses for whole-record chronological order): two different fields on the
        /// same object (e.g. "Status" and "Owner") each carry their own independent
        /// OldValue/NewValue chain, so grouping by object alone would compare unrelated
        /// fields' values against each other.
        /// </summary>
        private static string GetFieldHistoryKey(AuditTrailInfo row) =>
            $"{row.ObjectType ?? string.Empty}|{row.Object ?? string.Empty}|{row.Field ?? string.Empty}";

        /// <summary>
        /// Resolves a source timestamp to a true UTC instant, for both rule C2 (future-timestamp
        /// check) and rule C3 (working-hours check). Handles all three <see cref="DateTimeKind"/>
        /// values the mapper can produce:
        /// <list type="bullet">
        /// <item><see cref="DateTimeKind.Utc"/> -- already UTC, returned as-is.</item>
        /// <item><see cref="DateTimeKind.Local"/> -- only ever produced by
        /// <see cref="AuditTrailMapper.MapRowToAuditTrailInfo"/>'s RoundtripKind parse when the
        /// source string itself carried an explicit numeric offset (e.g. "+02:00"): a genuinely
        /// anchored instant, already converted by .NET into this server's own OS time zone, so
        /// <see cref="DateTime.ToUniversalTime"/> correctly and losslessly inverts it regardless
        /// of what that OS time zone actually is.</item>
        /// <item><see cref="DateTimeKind.Unspecified"/> -- no timezone info at all in the source
        /// (the overwhelming majority of real GxP audit trail exports, which typically log local
        /// plant-clock time). Rather than guessing it's UTC (the previous, user-reported-buggy
        /// behavior: a "5 PM" entry got silently treated as UTC and shifted forward into
        /// <paramref name="assumedZoneForAmbiguousTimestamps"/>, sometimes crossing the working-
        /// hours boundary it was never actually near), this now assumes the wall-clock reading is
        /// already expressed in <paramref name="assumedZoneForAmbiguousTimestamps"/> -- the same
        /// zone WorkdayStartHour/WorkdayEndHour are defined in, which is the best available proxy
        /// for "the audit trail source's own local time" absent a dedicated per-source setting.
        /// Converted properly via <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>
        /// rather than left untouched, so C2's UtcNow comparison and C3's working-hours check are
        /// both grounded in the same assumption instead of silently disagreeing with each other.
        /// </item>
        /// </list>
        /// (A dedicated rule used to flag Unspecified-vs-UTC ambiguity separately -- A3,
        /// timestamp-must-be-UTC -- but it was removed; see
        /// Audit-Trail-Rule-Regulatory-Basis-Review.md. This method is the load-bearing successor
        /// to that removed rule's underlying concern.)
        /// </summary>
        private static DateTime ResolveTimestampToUtc(DateTime timestamp, TimeZoneInfo assumedZoneForAmbiguousTimestamps)
        {
            if (timestamp.Kind == DateTimeKind.Utc)
                return timestamp;

            if (timestamp.Kind == DateTimeKind.Local)
                return timestamp.ToUniversalTime();

            // DateTimeKind.Unspecified: ConvertTimeToUtc requires the DateTime's Kind to either
            // be Unspecified already (it is) or match assumedZoneForAmbiguousTimestamps -- no
            // further adjustment needed before calling it.
            return TimeZoneInfo.ConvertTimeToUtc(timestamp, assumedZoneForAmbiguousTimestamps);
        }

        /// <summary>
        /// Whether a raw source timestamp string actually declared sub-second precision, for
        /// rule E1's finding message (see its call site) -- so the message never prints a
        /// fabricated ".000" for a source value that only ever had whole-second resolution
        /// (".NET's parser defaults an absent fractional-seconds component to zero, which is
        /// indistinguishable from a genuinely-measured zero milliseconds once parsed, so this
        /// has to be decided from the original string, not from the parsed <see cref="DateTime"/>).
        /// Recognizes "HH:mm:ss.fff"-shaped fractional seconds (covers both plain and ISO 8601
        /// timestamps) via a colon immediately before the two-digit seconds and the dot, so it
        /// doesn't false-positive on a dot-separated date like "15.03.2023" (no preceding colon);
        /// also recognizes a 13-or-more-digit all-numeric value as a Unix epoch timestamp in
        /// milliseconds or finer (see <see cref="AuditTrailMapper.TryParseDateTime"/>'s own
        /// Unix-timestamp handling), which inherently carries sub-second precision.
        /// </summary>
        private static bool HasSubSecondPrecision(string? rawTimestamp)
        {
            if (string.IsNullOrEmpty(rawTimestamp))
                return false;

            if (Regex.IsMatch(rawTimestamp, @":\d{2}\.\d+"))
                return true;

            var trimmed = rawTimestamp.Trim();
            return trimmed.Length >= 13 && Regex.IsMatch(trimmed, @"^-?\d+$");
        }

        /// <summary>
        /// Builds the ", from source value '...'" suffix that rules C2 and C3 append to their
        /// finding text whenever a timestamp's parsed <see cref="DateTimeKind"/> actually caused a
        /// conversion (i.e. <paramref name="kind"/> is not <see cref="DateTimeKind.Unspecified"/>).
        /// Exists so a report that a timestamp "shouldn't have shifted" is self-diagnosing from the
        /// finding text alone: it names not just *that* the source carried an explicit UTC/GMT/Z
        /// marker or numeric offset (rather than the engine assuming one, as it does for the
        /// Unspecified case -- see <see cref="ResolveTimestampToUtc"/>), but the exact raw source
        /// text that carried it, so a reviewer or this session doesn't need the original source
        /// file in hand to confirm whether the marker is really there. Returns an empty string for
        /// the Unspecified case (nothing to attribute to the source -- the assumption is the
        /// engine's own, not the data's) or when <paramref name="timestampRaw"/> is unavailable.
        /// </summary>
        private static string FormatRawSourceNote(DateTimeKind kind, string? timestampRaw) =>
            kind != DateTimeKind.Unspecified && !string.IsNullOrEmpty(timestampRaw)
                ? $", from source value '{timestampRaw}'"
                : string.Empty;

        /// <summary>
        /// Resolves rule C3's configured working-hours time zone from
        /// <see cref="EngineSettings.WorkdayTimeZone"/>. A null/empty/blank value (not yet
        /// configured by an admin) or an ID that no longer resolves on this server (e.g. removed
        /// from the OS's time zone database) falls back to <see cref="TimeZoneInfo.Utc"/> rather
        /// than failing the scan -- matching this rule's original UTC-only behavior.
        /// </summary>
        private static TimeZoneInfo ResolveWorkdayTimeZone(string? workdayTimeZoneId)
        {
            if (string.IsNullOrWhiteSpace(workdayTimeZoneId))
                return TimeZoneInfo.Utc;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(workdayTimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        /// <summary>
        /// Converts a source timestamp into rule C3's configured working-hours time zone, for
        /// reading Hour/DayOfWeek against WorkdayStartHour/WorkdayEndHour/AllowWeekends. Resolves
        /// to a true UTC instant first (see <see cref="ResolveTimestampToUtc"/>, which assumes an
        /// Unspecified-kind timestamp is already local plant time in <paramref name="workdayTimeZone"/>)
        /// and then converts that instant into <paramref name="workdayTimeZone"/> -- which is an
        /// exact round trip (no net change) for an Unspecified-kind input, since it was assumed to
        /// be in that zone to begin with, and a real conversion for a genuinely UTC- or
        /// offset-anchored (Local-kind) input.
        /// </summary>
        private static DateTime ConvertToWorkdayTimeZone(DateTime timestamp, TimeZoneInfo workdayTimeZone)
        {
            var utc = ResolveTimestampToUtc(timestamp, workdayTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, workdayTimeZone);
        }

        /// <summary>
        /// Converts an ENTIRE scan's rows (no filtering) to a compact JSON string for the
        /// AI-analysis prompt -- used for the "full scan" tier (a scan with
        /// AiSettings.FullScanRowThreshold rows or fewer), where every row, flagged or not, is
        /// sent so the AI can do a genuinely independent read of the whole audit trail. For a
        /// scan above that threshold, use the capped overload below instead -- serializing every
        /// row of a scan with up to ~5,000,000 rows is exactly how a large scan's AI analysis
        /// used to take minutes or fail outright (see ADR-0006's amendment). <c>WriteIndented</c>
        /// is deliberately off: this JSON is only ever read by OpenAI, and indentation is pure
        /// wasted tokens.
        /// </summary>
        public static string ConvertScanResultsToJson(List<ScanResultInfo> scanResults)
            => JsonSerializer.Serialize(scanResults, CompactJsonOptions);

        /// <summary>
        /// Converts only a scan's rule-engine findings (rows where <c>Finding</c> is non-empty)
        /// to a compact JSON string, capped at <paramref name="maxFindings"/>
        /// (AiSettings.MaxFindingsPerAnalysis) -- used for the "findings-only" tier (a scan above
        /// AiSettings.FullScanRowThreshold, where sending every row is not feasible). When there
        /// are more findings than the cap, they are sampled evenly across the whole ordered set
        /// of findings (first, last, and spread in between) rather than taking just the first
        /// ones encountered, so a pattern that only shows up partway through a huge file -- or
        /// recurs throughout it -- isn't missed just because the earliest findings happened to be
        /// unrelated.
        ///
        /// Filters on <c>!string.IsNullOrWhiteSpace(Finding)</c> rather than the <c>HasFinding</c>
        /// bool -- mirrors DetailsModel.GetRowClass/HasAnyFindingExpr's own "has a finding" check
        /// (Scan/Details.cshtml.cs) rather than depending on that redundant flag staying in sync.
        ///
        /// The full matching-findings list is materialized first (one O(n) pass over
        /// <paramref name="scanResults"/>, building a list of object *references*, not
        /// re-serializing anything) so even a scan where most of its up to ~5,000,000 rows are
        /// findings stays cheap -- only the final <paramref name="maxFindings"/>-sized subset is
        /// ever turned into JSON.
        /// </summary>
        /// <param name="scanResults">The scan's full row set (not just findings) -- filtered internally.</param>
        /// <param name="maxFindings">Maximum number of findings to include.</param>
        /// <param name="sampled">True if there were more findings than <paramref name="maxFindings"/>, so the included set is an even sample rather than the complete findings list.</param>
        /// <returns>JSON array of the included findings.</returns>
        public static string ConvertScanResultsToJson(List<ScanResultInfo> scanResults, int maxFindings, out bool sampled)
        {
            var findings = scanResults.Where(sr => !string.IsNullOrWhiteSpace(sr.Finding)).ToList();
            sampled = false;
            List<ScanResultInfo> included;

            if (findings.Count <= maxFindings)
            {
                included = findings;
            }
            else
            {
                included = new List<ScanResultInfo>(maxFindings);
                for (int i = 0; i < maxFindings; i++)
                {
                    // Endpoint-inclusive spread: i=0 always lands on index 0 (the earliest
                    // finding) and i=maxFindings-1 always lands on findings.Count-1 (the
                    // latest), with the rest spread evenly between -- i*count/cap (without the
                    // "-1"s) would never actually reach the last index.
                    int index = maxFindings == 1 ? 0 : (int)((long)i * (findings.Count - 1) / (maxFindings - 1));
                    included.Add(findings[index]);
                }
                sampled = true;
            }

            return JsonSerializer.Serialize(included, CompactJsonOptions);
        }

        /// <summary>Whether a scan's AI-analysis payload should use the findings-only tier (true)
        /// or send the full scan (false) -- see AiSettings.FullScanRowThreshold. Pulled out as
        /// its own tiny function, rather than an inline comparison in the two call sites
        /// (ScanProcessingService.cs / Scan.cshtml.cs), purely so the boundary condition (exactly
        /// at the threshold vs. one row over) has a direct, fast unit test instead of being
        /// buried inside an untestable background work item delegate.</summary>
        public static bool ShouldUseFindingsOnlyTier(int totalRows, int fullScanRowThreshold)
            => totalRows > fullScanRowThreshold;

        private static readonly JsonSerializerOptions CompactJsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Computes a compact, whole-scan statistical summary (counts/distributions) to send
        /// alongside the capped findings for the "findings-only" tier (see the capped overload
        /// of <see cref="ConvertScanResultsToJson"/> and AiSettings.FullScanRowThreshold). Unlike
        /// the capped findings payload, this covers EVERY row of the scan -- up to ~5,000,000 for
        /// a large upload -- not just the sampled subset, so the AI can still reason about
        /// patterns (one user responsible for most changes, most edits happening outside working
        /// hours, etc.) that don't depend on any single row being individually flagged by a rule.
        /// This is a deliberate, partial mitigation for the independent-analysis capability the
        /// findings-only tier otherwise loses -- not a full restoration (see ADR-0006's
        /// amendment).
        ///
        /// A single foreach pass over the already in-memory <paramref name="scanResults"/> list
        /// (not several separate LINQ GroupBy passes) keeps this cheap even at millions of rows;
        /// only the top 10 users/object types are kept (sorted at the very end, over at most a
        /// handful of distinct keys -- never over the row count itself), and the hour-of-day/
        /// day-of-week buckets are fixed-size arrays, so the OUTPUT size never grows with the
        /// scan's row count even though the computation reads every row.
        /// </summary>
        public static string BuildScanStatisticalSummaryJson(List<ScanResultInfo> scanResults)
        {
            int totalFindings = 0;
            int missingReasonCount = 0;
            DateTime? dateRangeStart = null;
            DateTime? dateRangeEnd = null;
            var byUser = new Dictionary<string, int>();
            var byOperation = new Dictionary<string, int>();
            var byObjectType = new Dictionary<string, int>();
            var byHour = new int[24];
            var byDayOfWeek = new int[7];

            foreach (var row in scanResults)
            {
                if (!string.IsNullOrWhiteSpace(row.Finding)) totalFindings++;
                if (string.IsNullOrWhiteSpace(row.Reason)) missingReasonCount++;

                if (row.Timestamp is { } ts)
                {
                    if (dateRangeStart is null || ts < dateRangeStart) dateRangeStart = ts;
                    if (dateRangeEnd is null || ts > dateRangeEnd) dateRangeEnd = ts;
                    byHour[ts.Hour]++;
                    byDayOfWeek[(int)ts.DayOfWeek]++;
                }

                IncrementCount(byUser, row.UserID);
                IncrementCount(byOperation, row.Operation);
                IncrementCount(byObjectType, row.ObjectType);
            }

            var summary = new ScanStatisticalSummary
            {
                TotalRows = scanResults.Count,
                TotalFindings = totalFindings,
                MissingReasonCount = missingReasonCount,
                DateRangeStart = dateRangeStart,
                DateRangeEnd = dateRangeEnd,
                TopUsersByRecordCount = TopN(byUser, 10),
                OperationCounts = byOperation,
                TopObjectTypesByRecordCount = TopN(byObjectType, 10),
                RecordsByHourOfDay = byHour,
                RecordsByDayOfWeek = byDayOfWeek
            };

            return JsonSerializer.Serialize(summary, CompactJsonOptions);
        }

        private static void IncrementCount(Dictionary<string, int> counts, string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            counts[key] = counts.TryGetValue(key, out var existing) ? existing + 1 : 1;
        }

        private static List<NamedCount> TopN(Dictionary<string, int> counts, int n)
            => counts
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => new NamedCount { Name = kv.Key, Count = kv.Value })
                .ToList();

        private sealed class ScanStatisticalSummary
        {
            public int TotalRows { get; set; }
            public int TotalFindings { get; set; }
            public int MissingReasonCount { get; set; }
            public DateTime? DateRangeStart { get; set; }
            public DateTime? DateRangeEnd { get; set; }
            public List<NamedCount> TopUsersByRecordCount { get; set; } = [];
            public Dictionary<string, int> OperationCounts { get; set; } = [];
            public List<NamedCount> TopObjectTypesByRecordCount { get; set; } = [];
            public int[] RecordsByHourOfDay { get; set; } = new int[24];
            public int[] RecordsByDayOfWeek { get; set; } = new int[7];
        }

        private sealed class NamedCount
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
        }

        /// <summary>
        /// Parses OpenAI API response and extracts insights and recommendations.
        /// </summary>
        /// <param name="openAIOutput">The raw OpenAI API response JSON</param>
        /// <returns>A tuple containing (insights, recommendations)</returns>
        public static (string Insights, string Recommendations) ParseOpenAIResponse(string openAIOutput)
        {
            string insights = string.Empty;
            string recommendations = string.Empty;

            if (string.IsNullOrEmpty(openAIOutput))
            {
                return (insights, recommendations);
            }

            try
            {
                // First parse the OpenAI API response wrapper
                var apiResponse = Newtonsoft.Json.Linq.JObject.Parse(openAIOutput);
                
                // Extract the actual content from the choices array
                var contentString = apiResponse["choices"]?[0]?["message"]?["content"]?.ToString();
                
                if (!string.IsNullOrEmpty(contentString))
                {
                    // Find the JSON object within the text response
                    // Look for the first '{' and last '}' to extract the JSON portion
                    int jsonStart = contentString.IndexOf('{');
                    int jsonEnd = contentString.LastIndexOf('}');
                    
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        string jsonPortion = contentString.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        
                        // Now parse the extracted JSON content. DateParseHandling.None keeps
                        // every value as its original JSON string token -- Newtonsoft's default
                        // (DateParseHandling.DateTime) silently reinterprets any ISO-8601-shaped
                        // string (e.g. a "timestamp" field inside a record the AI names per
                        // AuditTrailPromptBuilder.Build()'s record-anchoring instruction) as a
                        // DateTime, and JValue.ToString() on that then renders it back out using
                        // the CURRENT CULTURE rather than the original text (observed turning
                        // "2026-08-25T11:00:00Z" into "8/25/2026 11:00:00 AM" on a US-culture
                        // host) -- exactly the class of ambiguous-reformatting bug this project
                        // has otherwise been careful to avoid in the rule engine's own timestamp
                        // handling (see ResolveTimestampToUtc/FormatRawSourceNote).
                        var contentJson = ParseJsonPreservingRawStrings(jsonPortion);
                        
                        insights = HumanizeAiResponseValue(contentJson["insights"]);
                        recommendations = HumanizeAiResponseValue(contentJson["recommendations"]);
                    }
                    else
                    {
                        // If no JSON found, store the entire content as insights
                        insights = contentString;
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                // If parsing fails, store error info
                insights = $"Error parsing OpenAI response: {ex.Message}";
                recommendations = openAIOutput;
            }

            return (insights, recommendations);
        }

        /// <summary>
        /// Parses a JSON object without Newtonsoft's default date auto-detection -- every value
        /// stays exactly as it appeared in the source text, even one that looks like a date.
        /// </summary>
        private static Newtonsoft.Json.Linq.JObject ParseJsonPreservingRawStrings(string json)
        {
            using var stringReader = new System.IO.StringReader(json);
            using var jsonReader = new Newtonsoft.Json.JsonTextReader(stringReader)
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None
            };
            return Newtonsoft.Json.Linq.JObject.Load(jsonReader);
        }

        /// <summary>
        /// Converts an "insights"/"recommendations" JSON token into plain text for display.
        /// AuditTrailPromptBuilder.Build() instructs the AI that both fields must be a plain
        /// string, but a model does not always follow that instruction -- it has been observed
        /// returning a JSON array of finding/record groups instead (each record shaped like
        /// { "finding": "...", "records": [ { "timestamp", "userID", "object", "field" } ] }),
        /// most likely because the prompt's separate instruction to name specific records made
        /// structured JSON feel like the natural way to comply. JToken.ToString() on a
        /// JArray/JObject silently returns raw, indented JSON text rather than throwing, so
        /// without this the reviewer would see an unreadable JSON dump where a plain-English
        /// summary was expected. Recognizes that specific finding/records shape and renders a
        /// short human-readable line per item; anything else structured falls back to a compact
        /// (not indented) JSON rendering, which is still not ideal but reads far better than
        /// Newtonsoft's default indented multi-line dump.
        /// </summary>
        private static string HumanizeAiResponseValue(Newtonsoft.Json.Linq.JToken? token)
        {
            if (token == null)
                return string.Empty;

            if (token.Type != Newtonsoft.Json.Linq.JTokenType.Array
                && token.Type != Newtonsoft.Json.Linq.JTokenType.Object)
            {
                return token.ToString();
            }

            var items = token.Type == Newtonsoft.Json.Linq.JTokenType.Array
                ? (Newtonsoft.Json.Linq.JArray)token
                : new Newtonsoft.Json.Linq.JArray(token);

            var lines = new List<string>();
            foreach (var item in items)
            {
                var finding = item["finding"]?.ToString();
                var records = item["records"] as Newtonsoft.Json.Linq.JArray;

                if (!string.IsNullOrEmpty(finding) && records != null && records.Count > 0)
                {
                    var recordDescriptions = records.Select(r =>
                    {
                        var obj = r["object"]?.ToString();
                        var field = r["field"]?.ToString();
                        var user = r["userID"]?.ToString();
                        var timestamp = r["timestamp"]?.ToString();

                        var parts = new List<string>();
                        if (!string.IsNullOrEmpty(obj)) parts.Add(obj);
                        if (!string.IsNullOrEmpty(field)) parts.Add($"({field})");
                        if (!string.IsNullOrEmpty(user)) parts.Add($"by {user}");
                        if (!string.IsNullOrEmpty(timestamp)) parts.Add($"at {timestamp}");
                        return string.Join(" ", parts);
                    }).Where(description => !string.IsNullOrWhiteSpace(description));

                    lines.Add($"{finding}: {string.Join("; ", recordDescriptions)}");
                }
                else
                {
                    lines.Add(item.ToString(Newtonsoft.Json.Formatting.None));
                }
            }

            return string.Join(" ", lines);
        }
    }
}