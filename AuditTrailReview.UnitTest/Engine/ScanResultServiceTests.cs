using AuditTrailReview.Engine;
using AuditTrailReview.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="ScanResultService.ApplyRulesAndGenerateResults"/>.
/// Each test class section covers one rule (A1–A3, B1–B8, C1–C3, D1–D2, E1–E3) plus edge-case scenarios.
/// Only the rule under test is enabled; all others remain disabled to isolate behaviour.
/// </summary>
[TestClass]
public class ScanResultServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private const int ScanId = 42;

    /// <summary>
    /// Builds a minimal valid <see cref="AuditTrailInfo"/> row.
    /// Override individual properties in each test as needed.
    /// </summary>
    private static AuditTrailInfo Row(
        string? uid        = "1",
        string? user       = "svc_batch",
        string? operation  = "Update",
        DateTime? ts       = null,
        string? tsRaw      = null,
        string? oldValue   = "OldVal",
        string? newValue   = "NewVal",
        string? reason     = "Valid reason text",
        string? objectType = null,
        string? objectName = null,
        string? field      = null)
    {
        var row = new AuditTrailInfo
        {
            UniqueIdentifier = uid,
            UserID           = user,
            Operation        = operation,
            Timestamp        = ts ?? new DateTime(2024, 6, 12, 10, 0, 0, DateTimeKind.Utc),
            TimestampRaw     = tsRaw ?? "2024-06-12T10:00:00Z",
            OldValue         = oldValue,
            NewValue         = newValue,
            Reason           = reason,
            ObjectType       = objectType,
            Object           = objectName,
            Field            = field
        };
        return row;
    }

    /// <summary>
    /// Calls <see cref="ScanResultService.ApplyRulesAndGenerateResults"/> with all 19 rule
    /// flags defaulting to <c>false</c>. Pass the rule numbers you want enabled (1-based).
    /// </summary>
    private static List<ScanResultInfo> Apply(
        List<AuditTrailInfo> rows,
        EngineSettings? settings = null,
        bool r3  = false, bool r4  = false,
        bool r5  = false, bool r7  = false,
        bool r9  = false, bool r10 = false, bool r11 = false,
        bool r13 = false, bool r14 = false, bool r15 = false, bool r16 = false,
        bool r17 = false, bool r20 = false,
        bool r21 = false, bool r22 = false, bool r24 = false, bool r27 = false, bool r28 = false, bool r29 = false)
    {
        return ScanResultService.ApplyRulesAndGenerateResults(
            rows, ScanId,
            r3,  r4,  r5,  r7,  r9,  r10,
            r11, r13, r14, r15, r16, r17, r20, r21, r22, r24, r27, r28, r29,
            settings);
    }

    private static void AssertFinding(ScanResultInfo result, string ruleCode)
        => Assert.IsTrue(result.Finding?.Contains(ruleCode),
            $"Expected finding [{ruleCode}] in: \"{result.Finding}\"");

    private static void AssertNoFinding(ScanResultInfo result, string ruleCode)
        => Assert.IsFalse(result.Finding?.Contains(ruleCode) ?? false,
            $"Unexpected finding [{ruleCode}] in: \"{result.Finding}\"");

    // ─── Edge Cases ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ApplyRulesAndGenerateResults_NullRows_ReturnsEmptyList()
    {
        var results = Apply(null!);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void ApplyRulesAndGenerateResults_EmptyRows_ReturnsEmptyList()
    {
        var results = Apply([]);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void ApplyRulesAndGenerateResults_AllRulesDisabled_NoFindings()
    {
        var rows = new List<AuditTrailInfo> { Row(), Row(uid: "2") };
        var results = Apply(rows);
        Assert.IsTrue(results.All(r => string.IsNullOrEmpty(r.Finding)));
        Assert.IsTrue(results.All(r => !r.HasFinding));
    }

    [TestMethod]
    public void ApplyRulesAndGenerateResults_ScanIdPropagatedToResults()
    {
        var results = Apply([Row()]);
        Assert.IsTrue(results.All(r => r.ScanID == ScanId));
    }

    [TestMethod]
    public void ApplyRulesAndGenerateResults_NullEngineSettings_UsesDefaults_NoException()
    {
        // Should not throw when settings is null
        var results = Apply([Row()], settings: null, r20: true);
        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public void ApplyRulesAndGenerateResults_RowCountMatchesInputCount()
    {
        var rows = Enumerable.Range(1, 5).Select(i => Row(uid: i.ToString())).ToList();
        var results = Apply(rows);
        Assert.AreEqual(5, results.Count);
    }

    // ─── Rule B1 — Modify Must Change Value ──────────────────────────────────

    [TestMethod]
    public void B1_UpdateWithSameBeforeAfter_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(operation: "Update", oldValue: "same", newValue: "same") };
        var results = Apply(rows, r3: true);
        AssertFinding(results[0], "[B1]");
        Assert.IsTrue(results[0].HasFinding);
    }

    [TestMethod]
    public void B1_UpdateWithDifferentBeforeAfter_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row(operation: "Update", oldValue: "old", newValue: "new") };
        var results = Apply(rows, r3: true);
        AssertNoFinding(results[0], "[B1]");
    }

    [TestMethod]
    public void B1_CreateOperationIgnored()
    {
        var rows = new List<AuditTrailInfo> { Row(operation: "Create", oldValue: "same", newValue: "same") };
        var results = Apply(rows, r3: true);
        AssertNoFinding(results[0], "[B1]");
    }

    [TestMethod]
    public void B1_UpdateWithEmptyValues_NoFinding()
    {
        // Rule only fires when both old and new are non-empty
        var rows = new List<AuditTrailInfo> { Row(operation: "Update", oldValue: "", newValue: "") };
        var results = Apply(rows, r3: true);
        AssertNoFinding(results[0], "[B1]");
    }

    // ─── Rule C1 — Timestamps in Logical Order ───────────────────────────────

    [TestMethod]
    public void C1_AscendingTimestamps_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, r4: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[C1]")));
    }

    [TestMethod]
    public void C1_DescendingTimestamps_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, r4: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[C1]")));
    }

    [TestMethod]
    public void C1_OutOfOrderTimestamp_FindingGenerated()
    {
        // Ascending set with one step going backwards
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 7,  0, 0, DateTimeKind.Utc))  // out of order
        };
        var results = Apply(rows, r4: true);
        AssertFinding(results[2], "[C1]");
    }

    [TestMethod]
    public void C1_InterleavedRecordsEachInternallyOrdered_NoFinding()
    {
        // Two records' rows interleaved in the file. Neither record's own history is out of
        // order, even though the file as a whole jumps back and forth in time -- a per-record
        // check must not flag any of these, unlike a whole-file check would.
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
            Row(ts: new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-2"),
            Row(ts: new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
            Row(ts: new DateTime(2024, 1, 1, 7, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-2"), // earlier than B-2's own previous row (7 < 8) -- still fine, B-2 is descending
            Row(ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
        };
        var results = Apply(rows, r4: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[C1]")));
    }

    [TestMethod]
    public void C1_InterleavedRecordsOneOutOfOrder_FindingOnlyOnOffendingRecord()
    {
        // B-1 is ascending and stays ascending throughout. B-2 is ascending for its first two
        // rows (establishing its own auto-detected direction) and then its third row goes
        // backwards. Interleaving B-1's rows in between must not affect B-1's own result, and
        // the finding must land only on B-2's offending row.
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-2"),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-2"), // still ascending for B-2 (8 -> 9)
            Row(ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-1"),
            Row(ts: new DateTime(2024, 1, 1, 7,  0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B-2"), // out of order for B-2 (ascending: 9 -> 7)
        };
        var results = Apply(rows, r4: true);

        AssertNoFinding(results[0], "[C1]"); // B-1
        AssertNoFinding(results[1], "[C1]"); // B-2, first row
        AssertNoFinding(results[2], "[C1]"); // B-1
        AssertNoFinding(results[3], "[C1]"); // B-2, still ascending
        AssertNoFinding(results[4], "[C1]"); // B-1
        AssertFinding(results[5], "[C1]");   // B-2, out of order
    }

    [TestMethod]
    public void C1_NoObjectPopulated_BehavesLikeWholeFileCheck()
    {
        // When ObjectType/Object aren't populated at all (as in every other C1 test above and
        // in most other rules' tests), all rows collapse into one shared group -- reproducing
        // the previous whole-file behavior exactly.
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 1, 8,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 9,  0, 0, DateTimeKind.Utc)),
            Row(ts: new DateTime(2024, 1, 1, 7,  0, 0, DateTimeKind.Utc))  // out of order
        };
        var results = Apply(rows, r4: true);
        AssertNoFinding(results[0], "[C1]");
        AssertNoFinding(results[1], "[C1]");
        AssertFinding(results[2], "[C1]");
    }

    // ─── Rule A1 — Mandatory Fields ──────────────────────────────────────────

    [TestMethod]
    public void A1_AllMandatoryFieldsPresent_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row() };
        var results = Apply(rows, r5: true);
        AssertNoFinding(results[0], "[A1]");
    }

    [TestMethod]
    public void A1_MissingUser_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(user: null) };
        var results = Apply(rows, r5: true);
        AssertFinding(results[0], "[A1]");
    }

    [TestMethod]
    public void A1_MissingTimestamp_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            new() { UserID = "svc_batch", Operation = "Update", Timestamp = null }
        };
        var results = Apply(rows, r5: true);
        AssertFinding(results[0], "[A1]");
    }

    [TestMethod]
    public void A1_MissingOperation_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(operation: null) };
        var results = Apply(rows, r5: true);
        AssertFinding(results[0], "[A1]");
    }

    // ─── Rule A3 — No Duplicate Records ──────────────────────────────────────

    [TestMethod]
    public void A3_DuplicateCompositeKey_FindingGenerated()
    {
        var ts = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var row1 = Row(user: "jdoe", ts: ts, operation: "Update", oldValue: "A", newValue: "B");
        var row2 = Row(user: "jdoe", ts: ts, operation: "Update", oldValue: "A", newValue: "B");
        var results = Apply([row1, row2], r7: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("[A3]")));
    }

    [TestMethod]
    public void A3_UniqueCompositeKeys_NoFinding()
    {
        var ts1 = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var ts2 = new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var results = Apply([Row(ts: ts1), Row(ts: ts2)], r7: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[A3]")));
    }

    [TestMethod]
    public void A3_SameUserTimestampValuesButDifferentObjects_NoFinding()
    {
        // Regression test for a user-reported bug: a legitimate bulk operation (e.g. a script
        // setting many different pieces of equipment to the same new status in the same second)
        // used to have every one of its rows misflagged as "duplicates" of each other, because
        // the duplicate key never considered which record was actually changed -- only
        // UserID/Timestamp/Operation/OldValue/NewValue. Two rows that share all of those but
        // change a different object are not duplicates of each other.
        var ts = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
        var row1 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-10", field: "Status");
        var row2 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-11", field: "Status");
        var results = Apply([row1, row2], r7: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[A3]")));
    }

    [TestMethod]
    public void A3_SameUserTimestampValuesAndSameObjectAndField_FindingGenerated()
    {
        // The narrowed key still catches a genuine duplicate: the same field on the same record
        // logged as changing the same way, by the same user, at the same instant, more than
        // once -- e.g. an import that ingested the same audit trail line twice.
        var ts = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
        var row1 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-10", field: "Status");
        var row2 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-10", field: "Status");
        var results = Apply([row1, row2], r7: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("[A3]")));
    }

    [TestMethod]
    public void A3_SameEverythingButDifferentField_NoFinding()
    {
        // Two different fields on the same record, changed the same way at the same instant,
        // are independent changes -- not duplicates of each other.
        var ts = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
        var row1 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-10", field: "Status");
        var row2 = Row(user: "twojcik", ts: ts, operation: "Update", oldValue: "Idle", newValue: "InUse",
            objectType: "Equipment", objectName: "EQUIP-3017-10", field: "SubStatus");
        var results = Apply([row1, row2], r7: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[A3]")));
    }

    // ─── Rule B2 — Update Must Have Both Values ───────────────────────────────

    [TestMethod]
    public void B2_UpdateMissingOldValue_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "", newValue: "NewVal")
        };
        var results = Apply(rows, r9: true);
        AssertFinding(results[0], "[B2]");
    }

    [TestMethod]
    public void B2_UpdateMissingNewValue_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "OldVal", newValue: "")
        };
        var results = Apply(rows, r9: true);
        AssertFinding(results[0], "[B2]");
    }

    [TestMethod]
    public void B2_UpdateWithBothValues_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "OldVal", newValue: "NewVal")
        };
        var results = Apply(rows, r9: true);
        AssertNoFinding(results[0], "[B2]");
    }

    // ─── Rule B3 — Create Must Have NewValue ─────────────────────────────────

    [TestMethod]
    public void B3_CreateMissingNewValue_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: null, newValue: "")
        };
        var results = Apply(rows, r10: true);
        AssertFinding(results[0], "[B3]");
    }

    [TestMethod]
    public void B3_CreateWithNewValue_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: null, newValue: "SomeValue")
        };
        var results = Apply(rows, r10: true);
        AssertNoFinding(results[0], "[B3]");
    }

    // ─── Rule B4 — Create Must Not Have OldValue ─────────────────────────────

    [TestMethod]
    public void B4_CreateWithOldValue_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: "ShouldNotExist", newValue: "NewVal")
        };
        var results = Apply(rows, r11: true);
        AssertFinding(results[0], "[B4]");
    }

    [TestMethod]
    public void B4_CreateWithNullOldValue_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: null, newValue: "NewVal")
        };
        var results = Apply(rows, r11: true);
        AssertNoFinding(results[0], "[B4]");
    }

    [TestMethod]
    public void B4_CreateWithNullPlaceholder_NoFinding()
    {
        // "(null)" is treated as an empty/no value — not a real old value
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: "(null)", newValue: "NewVal")
        };
        var results = Apply(rows, r11: true);
        AssertNoFinding(results[0], "[B4]");
    }

    // ─── Rule B5 — Update Must Have Reason ───────────────────────────────────

    [TestMethod]
    public void B5_UpdateMissingReason_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", reason: "")
        };
        var results = Apply(rows, r13: true);
        AssertFinding(results[0], "[B5]");
    }

    [TestMethod]
    public void B5_UpdateWithReason_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", reason: "Correction of error")
        };
        var results = Apply(rows, r13: true);
        AssertNoFinding(results[0], "[B5]");
    }

    // ─── Rule B6 — Delete Must Have Reason ───────────────────────────────────

    [TestMethod]
    public void B6_DeleteMissingReason_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Delete", reason: "")
        };
        var results = Apply(rows, r14: true);
        AssertFinding(results[0], "[B6]");
    }

    [TestMethod]
    public void B6_DeleteWithReason_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Delete", reason: "Obsolete record removed")
        };
        var results = Apply(rows, r14: true);
        AssertNoFinding(results[0], "[B6]");
    }

    // ─── Rule B7 — Delete Must Retain OldValue ───────────────────────────────

    [TestMethod]
    public void B7_DeleteMissingOldValue_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Delete", oldValue: "")
        };
        var results = Apply(rows, r15: true);
        AssertFinding(results[0], "[B7]");
    }

    [TestMethod]
    public void B7_DeleteWithOldValue_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Delete", oldValue: "RetainedValue")
        };
        var results = Apply(rows, r15: true);
        AssertNoFinding(results[0], "[B7]");
    }

    // ─── Rule C2 — No Future Timestamps ─────────────────────────────────────

    [TestMethod]
    public void C2_FutureTimestamp_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.Now.AddYears(1))
        };
        var results = Apply(rows, r16: true);
        AssertFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_PastTimestamp_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, r16: true);
        AssertNoFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_NullTimestamp_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row(ts: null) };
        var results = Apply(rows, r16: true);
        AssertNoFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_UtcKindFutureTimestamp_FindingGenerated()
    {
        // Kind = Utc needs no conversion; this is the simple, always-correct case.
        var rows = new List<AuditTrailInfo> { Row(ts: DateTime.UtcNow.AddDays(1)) };
        var results = Apply(rows, r16: true);
        AssertFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_LocalKindTimestamp_ConvertedToUtcBeforeComparison_PastNotFlagged()
    {
        // Regression test for a bug where C2 compared a Kind == Local value's raw clock digits
        // directly against DateTime.UtcNow, ignoring Kind entirely -- correct only when the
        // machine running the comparison happens to sit at UTC+0. AuditTrailMapper's
        // RoundtripKind parse produces exactly this Local-kind shape for a source timestamp
        // with an explicit non-UTC offset (e.g. "+05:00"): an instant expressed in the
        // *server's* own local time zone. ToLocalTime() here builds that same shape from a
        // known UTC instant, so the fix's ToUniversalTime() call should always recover it
        // correctly and never flag a genuinely-past instant as being in the future, regardless
        // of which time zone this test happens to run in.
        var pastUtcInstant = DateTime.UtcNow.AddHours(-3);
        var localKindPast = pastUtcInstant.ToLocalTime(); // Kind = Local, same instant as pastUtcInstant

        var rows = new List<AuditTrailInfo> { Row(ts: localKindPast) };
        var results = Apply(rows, r16: true);

        AssertNoFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_LocalKindTimestamp_ConvertedToUtcBeforeComparison_FutureFlagged()
    {
        // Mirror of the above: a genuinely future instant, expressed as a Local-kind value the
        // same way RoundtripKind parsing would produce it, must still be flagged after
        // conversion -- deterministically, on every machine, regardless of its own time zone.
        var futureUtcInstant = DateTime.UtcNow.AddHours(3);
        var localKindFuture = futureUtcInstant.ToLocalTime();

        var rows = new List<AuditTrailInfo> { Row(ts: localKindFuture) };
        var results = Apply(rows, r16: true);

        AssertFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_UnspecifiedKindTimestamp_NoWorkdayTimeZoneConfigured_AssumedUtc_FindingGenerated()
    {
        // Unspecified-kind timestamps (no timezone info at all in the source data) are assumed
        // to already be local plant time in EngineSettings.WorkdayTimeZone (see
        // ResolveTimestampToUtc). With no WorkdayTimeZone configured, that resolves to UTC
        // (ResolveWorkdayTimeZone's fallback), so this case is unchanged from the original
        // behavior: an ambiguous future-looking timestamp is still flagged. (A dedicated rule
        // used to flag Unspecified-vs-UTC ambiguity separately -- A3, timestamp-must-be-UTC --
        // but it was removed; see Audit-Trail-Rule-Regulatory-Basis-Review.md.)
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified))
        };
        var results = Apply(rows, r16: true);
        AssertFinding(results[0], "[C2]");
    }

    [TestMethod]
    public void C2_UnspecifiedKindTimestamp_WorkdayTimeZoneConfigured_InterpretedInThatZoneBeforeComparison()
    {
        // Regression test mirroring the C3 fix below: an Unspecified-kind timestamp is assumed
        // to already be local plant-clock time in the configured WorkdayTimeZone, so it's
        // converted to UTC using that zone's own offset before comparing to "now" -- it is
        // *not* compared as written, which would silently assume it was already UTC. Uses the
        // zone's own current offset (via TimeZoneInfo) rather than a hardcoded number, so this
        // is correct year-round regardless of Warsaw's DST state when the test runs.
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var settings = new EngineSettings { WorkdayTimeZone = "Europe/Warsaw" };

        // A wall-clock reading that is genuinely ~30 minutes in the past once correctly
        // interpreted as Warsaw local time, but would look ~30 minutes into the *future* if
        // instead compared to UtcNow as written (Warsaw is always ahead of UTC).
        var nowInWarsaw = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, warsaw);
        var ambiguousTimestamp = DateTime.SpecifyKind(nowInWarsaw.AddMinutes(-30), DateTimeKind.Unspecified);

        var rows = new List<AuditTrailInfo> { Row(ts: ambiguousTimestamp) };
        var results = Apply(rows, settings: settings, r16: true);

        AssertNoFinding(results[0], "[C2]");
    }

    // Note: FormatRawSourceNote (the "..., from source value '...'" suffix) is a single helper
    // shared by C2's kindNote and C3's conversionNote -- exercised deterministically below via
    // C3 (anchored to the named "Europe/Warsaw" IANA zone via WorkdayTimeZone). A parallel C2 test
    // would need a Local-kind timestamp, whose conversion runs through this test-runner machine's
    // own TimeZoneInfo.Local -- on a UTC-configured host (the common case for CI/build machines)
    // that conversion is a no-op, so no note would ever be written and the test would need to
    // branch on host state to stay correct either way. Not worth the added complexity for what is
    // otherwise the same one-line wiring already covered by the C3 tests below.

    // ─── Rule C3 — Working Hours ─────────────────────────────────────────────

    [TestMethod]
    public void C3_TimestampOutsideWorkingHours_FindingGenerated()
    {
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        // 3:00 AM — outside working hours
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 12, 3, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_TimestampInsideWorkingHours_NoFinding()
    {
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 12, 10, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_WeekendTimestamp_AllowWeekendsTrue_NoFinding()
    {
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        // Saturday
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_WeekendTimestamp_AllowWeekendsFalse_FindingGenerated()
    {
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = false };
        // Saturday
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_LocalKindTimestamp_EvaluatedInUtc_InsideWorkingHours_NoFinding()
    {
        // Regression test for a bug where C3 read dt.Hour/dt.DayOfWeek directly off whatever
        // Kind the timestamp had, ignoring Kind entirely -- correct only when the machine
        // running the comparison happened to sit at UTC+0. Build a Local-kind value the same
        // way AuditTrailMapper's RoundtripKind parse would (via ToLocalTime() from a known UTC
        // instant) so the fix's UTC normalization is exercised deterministically: this instant
        // is inside the configured working-hours window *in UTC*, so it must never be flagged,
        // regardless of which time zone this test happens to run in.
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        var insideWindowUtc = new DateTime(2024, 6, 12, 10, 0, 0, DateTimeKind.Utc); // 10:00 UTC, within 07:00-18:00
        var rows = new List<AuditTrailInfo> { Row(ts: insideWindowUtc.ToLocalTime()) };

        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_LocalKindTimestamp_EvaluatedInUtc_OutsideWorkingHours_FindingGenerated()
    {
        // Mirror of the above: an instant outside the configured window *in UTC* must still be
        // flagged after normalization, deterministically, on every machine.
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        var outsideWindowUtc = new DateTime(2024, 6, 12, 3, 0, 0, DateTimeKind.Utc); // 03:00 UTC, before 07:00
        var rows = new List<AuditTrailInfo> { Row(ts: outsideWindowUtc.ToLocalTime()) };

        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_UnspecifiedKindTimestamp_NoWorkdayTimeZoneConfigured_AssumedUtc_FindingGenerated()
    {
        // Unspecified-kind timestamps are assumed to already be local plant time in
        // EngineSettings.WorkdayTimeZone (see ResolveTimestampToUtc). With no WorkdayTimeZone
        // configured, that resolves to UTC (ResolveWorkdayTimeZone's fallback), so this case is
        // unchanged from the original behavior. (A dedicated rule used to flag Unspecified-vs-UTC
        // ambiguity separately -- A3, timestamp-must-be-UTC -- but it was removed; see
        // Audit-Trail-Rule-Regulatory-Basis-Review.md.)
        var settings = new EngineSettings { WorkdayStartHour = 7, WorkdayEndHour = 18, AllowWeekends = true };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.SpecifyKind(new DateTime(2024, 6, 12, 3, 0, 0), DateTimeKind.Unspecified))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_UnspecifiedKindTimestamp_WorkdayTimeZoneConfigured_EvaluatedAtFaceValue_NoShift_NoFinding()
    {
        // Regression test for a real user-reported bug: a change recorded at 17:00 with no
        // timezone marker at all -- clearly inside a 07:00-18:00 window -- used to be treated as
        // if it were UTC and then converted into the configured WorkdayTimeZone, shifting it to
        // 18:00 in Europe/Warsaw (UTC+1 in March, before the DST switch) and pushing it exactly
        // onto the end-of-day boundary, producing a false "[C3]" finding for a change that was
        // never actually near it. Since most GxP audit trail sources record local plant-clock
        // time with no offset at all, an Unspecified-kind timestamp is now assumed to already be
        // expressed in WorkdayTimeZone, so it's evaluated at face value with no conversion, and
        // 17:00 correctly stays inside the window.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.SpecifyKind(new DateTime(2026, 3, 24, 17, 0, 0), DateTimeKind.Unspecified))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_UnspecifiedKindTimestamp_WorkdayTimeZoneConfigured_GenuineViolationStillFlagged()
    {
        // The fix above doesn't just suppress every finding for Unspecified timestamps -- a
        // value that is genuinely outside the window, evaluated at face value in the configured
        // zone, is still flagged.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.SpecifyKind(new DateTime(2026, 3, 24, 3, 0, 0), DateTimeKind.Unspecified))
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_UnspecifiedKindTimestamp_ShiftedFinding_DoesNotNameRawSourceValue()
    {
        // Companion to the explicit-Kind test below: an Unspecified-kind timestamp is evaluated
        // at face value in workdayTimeZone with no actual UTC round-trip conversion needed (see
        // ResolveTimestampToUtc), so it round-trips to the exact same wall-clock value here too --
        // there is genuinely nothing to explain, and no "(... kind ...)" parenthetical is added
        // at all (per the existing "only note the conversion when it actually changed the
        // wall-clock reading" comment). This locks that in: the assumption the engine itself
        // made for an ambiguous timestamp must never be dressed up as something the source data
        // asserted, so above all the finding must never claim a "source value" backed it.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: DateTime.SpecifyKind(new DateTime(2026, 3, 24, 3, 0, 0), DateTimeKind.Unspecified),
                tsRaw: "3/24/2026 3:00:00 AM")
        };

        var results = Apply(rows, settings: settings, r17: true);

        AssertFinding(results[0], "[C3]");
        Assert.IsFalse(results[0].Finding!.Contains(" kind"),
            $"Unspecified kind round-trips to the same value, so no Kind parenthetical should appear at all: \"{results[0].Finding}\"");
        Assert.IsFalse(results[0].Finding!.Contains("from source value"),
            $"Unspecified kind should never attribute its assumed shift to a source value: \"{results[0].Finding}\"");
    }

    [TestMethod]
    public void C3_ExplicitUtcKindTimestamp_ShiftedFinding_NamesKindAndRawSourceValue()
    {
        // Regression test for a user-reported follow-up: a fresh scan (AT-0017, "8/24/2026
        // 4:30:00 PM" displayed, evaluated as 18:30 in Europe/Warsaw) still showed the same
        // shift the earlier Unspecified-kind fix was meant to eliminate, even after a confirmed
        // rebuild. The rendered Timestamp/Finding columns are the already-*parsed* value
        // reformatted -- they cannot reveal whether the original source cell carried an explicit
        // UTC/GMT/Z marker or numeric offset, which (unlike the Unspecified/assumed case) is a
        // legitimate reason for a real conversion: a source system that genuinely timestamps in
        // UTC and logs, say, "2026-08-24T16:30:00Z" for an event that is truly 18:30 local during
        // Warsaw's August DST is being evaluated *correctly*, not shifted in error. This test
        // locks in that the finding text now says so explicitly -- naming both the Kind and the
        // exact raw source text -- so a future report like this one is self-diagnosing without
        // needing the original file: if the finding names a source value, the shift is a real,
        // data-asserted UTC/offset timestamp, not an assumption the engine made up.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2026, 8, 24, 16, 30, 0, DateTimeKind.Utc),
                tsRaw: "2026-08-24T16:30:00Z")
        };

        var results = Apply(rows, settings: settings, r17: true);

        AssertFinding(results[0], "[C3]");
        StringAssert.Contains(results[0].Finding, "Utc kind");
        StringAssert.Contains(results[0].Finding, "from source value '2026-08-24T16:30:00Z'");
        StringAssert.Contains(results[0].Finding, "evaluated in Europe/Warsaw as 2026-08-24 18:30:00");
    }

    [TestMethod]
    public void C3_WorkdayTimeZoneConfigured_InsideHoursInThatZone_NoFinding()
    {
        // 06:30 UTC on a January weekday is before the 07:00 UTC window start -- would be
        // flagged if evaluated in UTC (as it was before WorkdayTimeZone existed). Warsaw is a
        // fixed UTC+1 in January (no DST to worry about), so 06:30 UTC is 07:30 in
        // Europe/Warsaw -- inside the configured window once evaluated in the configured zone.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 15, 6, 30, 0, DateTimeKind.Utc)) // Monday
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_WorkdayTimeZoneConfigured_OutsideHoursInThatZone_FindingGenerated()
    {
        // Mirror of the above: 17:30 UTC is inside 07:00-18:00 if evaluated in UTC, but is
        // 18:30 in Europe/Warsaw (fixed UTC+1 in January) -- outside the window once evaluated
        // in the configured zone.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Europe/Warsaw"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 1, 15, 17, 30, 0, DateTimeKind.Utc)) // Monday
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_WorkdayTimeZoneConfigured_WeekendCheckUsesThatZoneToo_FindingGenerated()
    {
        // Full 24-hour working-hours window (0-24) so only the weekend check can produce a
        // finding here. 2024-06-14 23:30 UTC is a Friday (a weekday) in UTC, but Asia/Tokyo is a
        // fixed UTC+9 (no DST), so the same instant is 2024-06-15 08:30 -- a Saturday -- in that
        // zone. The weekend check must follow WorkdayTimeZone, not the source Kind's own zone.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 0,
            WorkdayEndHour = 24,
            AllowWeekends = false,
            WorkdayTimeZone = "Asia/Tokyo"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 14, 23, 30, 0, DateTimeKind.Utc)) // Friday in UTC
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertFinding(results[0], "[C3]");
    }

    [TestMethod]
    public void C3_UnresolvableWorkdayTimeZone_FallsBackToUtc_NoException()
    {
        // A WorkdayTimeZone value that doesn't resolve on this machine (e.g. a stale/garbage
        // ID left over from a server migration) must degrade to UTC rather than throwing and
        // failing the whole scan.
        var settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = true,
            WorkdayTimeZone = "Not/ARealTimeZone"
        };
        var rows = new List<AuditTrailInfo>
        {
            Row(ts: new DateTime(2024, 6, 12, 10, 0, 0, DateTimeKind.Utc)) // inside 07:00-18:00 UTC
        };
        var results = Apply(rows, settings: settings, r17: true);
        AssertNoFinding(results[0], "[C3]");
    }

    // ─── Rule D2 — Username Min Length ───────────────────────────────────────

    [TestMethod]
    public void D2_UsernameTooShort_FindingGenerated()
    {
        var settings = new EngineSettings { UsernameMinLength = 3 };
        var rows = new List<AuditTrailInfo> { Row(user: "ab") };
        var results = Apply(rows, settings: settings, r20: true);
        AssertFinding(results[0], "[D2]");
    }

    [TestMethod]
    public void D2_UsernameAtMinLength_NoFinding()
    {
        var settings = new EngineSettings { UsernameMinLength = 3 };
        var rows = new List<AuditTrailInfo> { Row(user: "abc") };
        var results = Apply(rows, settings: settings, r20: true);
        AssertNoFinding(results[0], "[D2]");
    }

    [TestMethod]
    public void D2_NullUsername_NoFinding()
    {
        var settings = new EngineSettings { UsernameMinLength = 3 };
        var rows = new List<AuditTrailInfo> { Row(user: null) };
        var results = Apply(rows, settings: settings, r20: true);
        AssertNoFinding(results[0], "[D2]");
    }

    // ─── Rule D1 — Shared/Generic/System Account Detection ───────────────────

    [TestMethod]
    public void D1_KnownAccountName_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(user: "admin") };
        var results = Apply(rows, r22: true);
        AssertFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_KnownPrefixWithSeparator_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(user: "svc-integration") };
        var results = Apply(rows, r22: true);
        AssertFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_KnownPrefixFollowedByDigit_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo> { Row(user: "sys01") };
        var results = Apply(rows, r22: true);
        AssertFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_PrefixWithoutSeparatorOrDigit_NoFinding()
    {
        // "system" is a known exact-match name, but "systema" is a real word/name that merely
        // starts with the same letters -- the separator/digit guard exists precisely so the
        // built-in "system" prefix doesn't swallow it.
        var rows = new List<AuditTrailInfo> { Row(user: "systema") };
        var results = Apply(rows, r22: true);
        AssertNoFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_OrdinaryIndividualLogin_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row(user: "jsmith") };
        var results = Apply(rows, r22: true);
        AssertNoFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_AdditionalExactPattern_FindingGenerated()
    {
        var settings = new EngineSettings { AdditionalSystemAccountPatterns = "reportinguser" };
        var rows = new List<AuditTrailInfo> { Row(user: "reportinguser") };
        var results = Apply(rows, settings: settings, r22: true);
        AssertFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_TestPrefixedLoginWithoutWildcardConfig_NoFinding()
    {
        // "test" is only in the built-in exact-match list, not the prefix list -- so
        // "testuser42" (no configured additional pattern) is NOT flagged by the built-in
        // heuristic alone, in contrast with D1_AdditionalWildcardPattern_FindingGenerated below.
        var rows = new List<AuditTrailInfo> { Row(user: "testuser42") };
        var results = Apply(rows, r22: true);
        AssertNoFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_AdditionalWildcardPattern_FindingGenerated()
    {
        // "test" is only in the built-in exact-match list, not the prefix list (so
        // "testuser42" alone isn't flagged -- see D1_OrdinaryIndividualLogin_NoFinding-style
        // reasoning); a wildcard AdditionalSystemAccountPatterns entry is exactly the escape
        // hatch for an organization whose test accounts follow this convention instead.
        var settings = new EngineSettings { AdditionalSystemAccountPatterns = "testuser*" };
        var rows = new List<AuditTrailInfo> { Row(user: "testuser42") };
        var results = Apply(rows, settings: settings, r22: true);
        AssertFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_NullUser_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row(user: null) };
        var results = Apply(rows, r22: true);
        AssertNoFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_Disabled_KnownAccountName_NoFinding()
    {
        var rows = new List<AuditTrailInfo> { Row(user: "admin") };
        var results = Apply(rows, r22: false);
        AssertNoFinding(results[0], "[D1]");
    }

    [TestMethod]
    public void D1_PersonalNameShapedLoginNotFlaggedByD1()
    {
        // A Title-Case personal name is a different signal from D1's system-account
        // patterns -- the ORIGINAL rule D1 (a different, unrelated rule: "username must not
        // be a real personal name") that used to flag this login was removed in an earlier
        // round, but the heuristics remain independent: a personal-name shape matches none of
        // this (current) D1's built-in system-account patterns.
        var rows = new List<AuditTrailInfo> { Row(user: "Adam") };
        var results = Apply(rows, r22: true);
        AssertNoFinding(results[0], "[D1]");
    }

    // ─── Rule E1 — Mass Changes ───────────────────────────────────────────────

    [TestMethod]
    public void E1_TenOrMoreRecordsWithinOneSecond_FindingGenerated()
    {
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, r21: true);

        Assert.IsTrue(results.All(r => r.Finding!.Contains("[E1]")),
            "All 10 records in a 1-second burst should be flagged.");
    }

    [TestMethod]
    public void E1_NineRecordsWithinOneSecond_NoFinding()
    {
        // Only 9 records — threshold is 10+
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 9)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, r21: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_RecordsSpreadOverMultipleSeconds_NoFinding()
    {
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddSeconds(i * 5)))
            .ToList();

        var results = Apply(rows, r21: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_DefaultSettings_MatchesOldHardcodedTenPerSecond()
    {
        // EngineSettings() with no explicit MassChangeThresholdCount/MassChangeWindowSeconds
        // defaults to 10/1.0 -- the exact values this rule used to hard-code -- so an
        // installation that never touches the new fields sees no behavior change.
        var settings = new EngineSettings();
        Assert.AreEqual(10, settings.MassChangeThresholdCount);
        Assert.AreEqual(1.0, settings.MassChangeWindowSeconds);
    }

    [TestMethod]
    public void E1_CustomLowerThreshold_FindingGeneratedBelowOldDefault()
    {
        // A configured threshold of 3 flags a burst that the old hard-coded 10 never would.
        var settings = new EngineSettings { MassChangeThresholdCount = 3, MassChangeWindowSeconds = 1.0 };
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 3)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 100)))
            .ToList();

        var results = Apply(rows, settings: settings, r21: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_CustomHigherThreshold_NoFindingForBurstThatWouldHaveMatchedOldDefault()
    {
        // 10 records in under a second used to always flag; a configured threshold of 15 no
        // longer does.
        var settings = new EngineSettings { MassChangeThresholdCount = 15, MassChangeWindowSeconds = 1.0 };
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, settings: settings, r21: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_CustomWiderWindow_FindingGeneratedAcrossLongerSpan()
    {
        // 10 records spread over 5 seconds didn't qualify under the old fixed 1-second window,
        // but does once the window is configured wider.
        var settings = new EngineSettings { MassChangeThresholdCount = 10, MassChangeWindowSeconds = 10.0 };
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddSeconds(i)))
            .ToList();

        var results = Apply(rows, settings: settings, r21: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_CustomNarrowerWindow_NoFindingForBurstThatWouldHaveMatchedOldDefault()
    {
        // The same 10-records-in-under-a-second burst from
        // E1_TenOrMoreRecordsWithinOneSecond_FindingGenerated no longer qualifies once the
        // window is configured narrower than the burst's own span.
        var settings = new EngineSettings { MassChangeThresholdCount = 10, MassChangeWindowSeconds = 0.1 };
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, settings: settings, r21: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_FindingMessage_ReflectsConfiguredThresholdAndWindow()
    {
        var settings = new EngineSettings { MassChangeThresholdCount = 3, MassChangeWindowSeconds = 2.5 };
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 3)
            .Select(i => Row(uid: i.ToString(), ts: baseTime.AddMilliseconds(i * 100)))
            .ToList();

        var results = Apply(rows, settings: settings, r21: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("burst of 3 or more") && r.Finding.Contains("2.5 second")));
    }

    [TestMethod]
    public void E1_FindingMessage_SourceHadNoSubSecondPrecision_TimestampShownWithoutMilliseconds()
    {
        // Regression test for a user-reported concern: the finding used to always print
        // "HH:mm:ss.fff", so a source timestamp with no fractional-seconds component at all
        // (e.g. "8/25/2026 11:00:00 AM") showed as "...11:00:00.000" -- implying a millisecond
        // reading that was never actually recorded, not a genuine zero. With no sub-second
        // info in TimestampRaw, the message now omits the milliseconds entirely.
        var baseTime = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime, tsRaw: "8/25/2026 11:00:00 AM"))
            .ToList();

        var results = Apply(rows, r21: true);

        Assert.IsTrue(results.All(r => r.Finding!.Contains("at 2026-08-25 11:00:00")
            && !r.Finding.Contains("11:00:00.")));
    }

    [TestMethod]
    public void E1_FindingMessage_SourceHadSubSecondPrecision_TimestampShownWithMilliseconds()
    {
        // Mirror of the above: when the source data genuinely declared fractional seconds, the
        // message still shows them -- this isn't about never printing milliseconds, only about
        // not fabricating them.
        var baseTime = new DateTime(2026, 8, 25, 11, 0, 0, 123, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(uid: i.ToString(), ts: baseTime, tsRaw: "2026-08-25T11:00:00.123Z"))
            .ToList();

        var results = Apply(rows, r21: true);

        Assert.IsTrue(results.All(r => r.Finding!.Contains("at 2026-08-25 11:00:00.123")));
    }

    [TestMethod]
    public void E1_TenModificationsFromTenDifferentUsers_NoFinding()
    {
        // Regression test for a user-reported fix: E1 used to treat any 10+ modifications
        // within the window as a "mass change" burst regardless of who made them, so 10
        // unrelated users each making one ordinary change in the same second were wrongly
        // flagged together. A mass change is meant to describe one user (or script) running
        // abnormally fast, not a coincidence of several different users.
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(user: $"user{i}", ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, r21: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E1]")));
    }

    [TestMethod]
    public void E1_OneUserBurstsAmongAnotherUsersUnrelatedChanges_OnlyBurstingUsersRowsFlagged()
    {
        // A genuine same-user burst (10 rows from "burstUser") alongside a different user's two
        // ordinary, non-bursting changes in the same overall time span: only the bursting
        // user's rows should be flagged, not "otherUser"'s.
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = new List<AuditTrailInfo>();
        rows.AddRange(Enumerable.Range(0, 10)
            .Select(i => Row(user: "burstUser", ts: baseTime.AddMilliseconds(i * 50))));
        rows.Add(Row(user: "otherUser", ts: baseTime.AddMilliseconds(20)));
        rows.Add(Row(user: "otherUser", ts: baseTime.AddMilliseconds(700)));

        var results = Apply(rows, r21: true);

        for (int i = 0; i < 10; i++)
            AssertFinding(results[i], "[E1]");
        AssertNoFinding(results[10], "[E1]");
        AssertNoFinding(results[11], "[E1]");
    }

    [TestMethod]
    public void E1_FindingMessage_NamesTheBurstingUser()
    {
        var baseTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 10)
            .Select(i => Row(user: "burstUser", ts: baseTime.AddMilliseconds(i * 50)))
            .ToList();

        var results = Apply(rows, r21: true);
        Assert.IsTrue(results.All(r => r.Finding!.Contains("same user ('burstUser')")));
    }

    // ─── Rule A2 — Record-Chain Continuity ───────────────────────────────────

    [TestMethod]
    public void A2_OldValueMatchesPreviousNewValue_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                oldValue: null, newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Draft", newValue: "Approved", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);

        AssertNoFinding(results[0], "[A2]");
        AssertNoFinding(results[1], "[A2]");
    }

    [TestMethod]
    public void A2_OldValueDoesNotMatchPreviousNewValue_FindingGeneratedOnMismatchedRowOnly()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                oldValue: null, newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Approved", newValue: "Closed", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);

        AssertNoFinding(results[0], "[A2]");
        AssertFinding(results[1], "[A2]");
    }

    [TestMethod]
    public void A2_FirstRecordInChain_NoFindingRegardlessOfOldValue()
    {
        // No preceding record exists in this scan for this object/field -- nothing to compare
        // against, so the row is never flagged even though its OldValue is arbitrary.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Update", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                oldValue: "Anything", newValue: "X", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);
        AssertNoFinding(results[0], "[A2]");
    }

    [TestMethod]
    public void A2_DifferentFieldsOnSameObject_EvaluatedIndependently()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Draft", newValue: "Approved", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Alice", objectType: "Batch", objectName: "B1", field: "Owner"),
            Row(uid: "4", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Bob", newValue: "Carol", objectType: "Batch", objectName: "B1", field: "Owner"),
        };

        var results = Apply(rows, r24: true);

        AssertNoFinding(results[1], "[A2]");  // Status chain is intact
        AssertFinding(results[3], "[A2]");    // Owner chain is broken ("Bob" != "Alice")
    }

    [TestMethod]
    public void A2_DifferentObjects_EvaluatedIndependently()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Draft", newValue: "Approved", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B2", field: "Status"),
            Row(uid: "4", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Approved", newValue: "Closed", objectType: "Batch", objectName: "B2", field: "Status"),
        };

        var results = Apply(rows, r24: true);

        AssertNoFinding(results[1], "[A2]");  // B1's chain is intact
        AssertFinding(results[3], "[A2]");    // B2's chain is broken
    }

    [TestMethod]
    public void A2_CaseInsensitiveAndTrimmedValueMatching_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: " Draft ", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "draft", newValue: "Approved", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);
        AssertNoFinding(results[1], "[A2]");
    }

    [TestMethod]
    public void A2_RowsWithoutObject_ExcludedFromEvaluation()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: null, objectName: null, field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "SomethingElse", newValue: "Approved", objectType: null, objectName: null, field: "Status"),
        };

        var results = Apply(rows, r24: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[A2]")));
    }

    [TestMethod]
    public void A2_RowWithoutTimestamp_ExcludedFromChain()
    {
        // Row 1 has no Timestamp and is excluded from the chain entirely -- if it were
        // included, Row 2's OldValue ("Anything") would mismatch Row 1's NewValue ("Draft")
        // and be flagged. Since Row 1 is excluded, Row 2 becomes the chain's first (and only)
        // evaluated record, with nothing to compare against.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: null,
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Anything", newValue: "X", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);
        AssertNoFinding(results[1], "[A2]");
    }

    [TestMethod]
    public void A2_DeleteEndsChain_SubsequentRecordNotComparedAgainstPreDeletionValue()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Delete", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Draft", newValue: null, objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                oldValue: "SomethingElse", newValue: "Y", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);
        AssertNoFinding(results[2], "[A2]");
    }

    [TestMethod]
    public void A2_MissingOldValueOnUpdate_NoFindingFromThisRule()
    {
        // A missing OldValue on an Update is B2's concern (both values required), not A8's --
        // this rule stays silent rather than double-flagging the same gap.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: null, newValue: "X", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);
        AssertNoFinding(results[1], "[A2]");
    }

    [TestMethod]
    public void A2_Disabled_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Approved", newValue: "Closed", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: false);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[A2]")));
    }

    [TestMethod]
    public void A2_FindingMessage_ContainsExpectedAndActualValues()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "WrongValue", newValue: "X", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);

        Assert.IsTrue(results[1].Finding!.Contains("[A2]"));
        Assert.IsTrue(results[1].Finding!.Contains("Draft"));
        Assert.IsTrue(results[1].Finding!.Contains("WrongValue"));
    }

    [TestMethod]
    public void A2_RowsOutOfFileOrderButCorrectByTimestamp_EvaluatedInChronologicalOrder()
    {
        // The Update row is listed BEFORE the Create row in the input list, but its Timestamp
        // is later -- continuity must be evaluated by actual chronological order, not the
        // order rows happen to appear in the source file/list.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "2", operation: "Update", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                oldValue: "Draft", newValue: "Approved", objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "1", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                newValue: "Draft", objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r24: true);

        AssertNoFinding(results[0], "[A2]"); // the Update row, listed first but timestamped second
        AssertNoFinding(results[1], "[A2]"); // the Create row, listed second but timestamped first
    }

    // ─── Rule B8 — Segregation of Duties ─────────────────────────────────────

    [TestMethod]
    public void B8_SameUserCreatedAndApproved_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);

        AssertNoFinding(results[0], "[B8]");
        AssertFinding(results[1], "[B8]");
    }

    [TestMethod]
    public void B8_SameUserModifiedAndReviewed_FindingGenerated()
    {
        // "Review" is recognized as an approval-step operation too, not just "Approve".
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Update", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Review", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertFinding(results[1], "[B8]");
    }

    [TestMethod]
    public void B8_DifferentUsers_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "mjones", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertNoFinding(results[1], "[B8]");
    }

    [TestMethod]
    public void B8_ApprovalWithNoPriorCreateOrUpdate_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertNoFinding(results[0], "[B8]");
    }

    [TestMethod]
    public void B8_DifferentObjects_EvaluatedIndependently()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B2"),
        };

        var results = Apply(rows, r27: true);
        AssertNoFinding(results[1], "[B8]"); // same user, but a different object -- no conflict
    }

    [TestMethod]
    public void B8_CaseInsensitiveOperationAndUserMatching_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "JSmith", operation: "create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "APPROVE", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertFinding(results[1], "[B8]");
    }

    [TestMethod]
    public void B8_ApprovalNotImmediatelyAdjacent_StillFlagged()
    {
        // Segregation of duties is a standing requirement, not a one-time adjacency check --
        // unrelated rows for other objects in between don't clear the conflict.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "mjones", operation: "Update", ts: new DateTime(2024, 1, 1, 10, 30, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B2"),
            Row(uid: "3", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 2, 9, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertFinding(results[2], "[B8]");
    }

    [TestMethod]
    public void B8_NonApprovalOperationsByOtherwiseConflictingUser_NotFlagged()
    {
        // The same user creating and later deleting the same object is not a segregation-of-
        // duties conflict -- only Approve/Review rows are ever flagged by this rule.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Delete", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[B8]")));
    }

    [TestMethod]
    public void B8_RowsWithoutObject_ExcludedFromEvaluation()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: null, objectName: null),
            Row(uid: "2", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: null, objectName: null),
        };

        var results = Apply(rows, r27: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[B8]")));
    }

    [TestMethod]
    public void B8_NullUser_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: null, operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        AssertNoFinding(results[1], "[B8]");
    }

    [TestMethod]
    public void B8_Disabled_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: false);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[B8]")));
    }

    [TestMethod]
    public void B8_FindingMessage_ContainsUserId()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", user: "jsmith", operation: "Create", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
            Row(uid: "2", user: "jsmith", operation: "Approve", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),
                objectType: "Batch", objectName: "B1"),
        };

        var results = Apply(rows, r27: true);
        Assert.IsTrue(results[1].Finding!.Contains("jsmith"));
    }

    // ─── Rule E2 — Oscillating Value Pattern ─────────────────────────────────

    [TestMethod]
    public void E2_ABAB_Pattern_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);

        AssertNoFinding(results[0], "[E2]");
        AssertNoFinding(results[1], "[E2]");
        AssertNoFinding(results[2], "[E2]");
        AssertFinding(results[3], "[E2]");
    }

    [TestMethod]
    public void E2_LongerOscillation_EachSubsequentRepeatAlsoFlagged()
    {
        // A→B→A→B→A→B: once the minimum 4-value alternation is established, each further
        // value that continues the pattern is flagged too, not just the first occurrence.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "5", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "6", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);

        AssertNoFinding(results[0], "[E2]");
        AssertNoFinding(results[1], "[E2]");
        AssertNoFinding(results[2], "[E2]");
        AssertFinding(results[3], "[E2]");
        AssertFinding(results[4], "[E2]");
        AssertFinding(results[5], "[E2]");
    }

    [TestMethod]
    public void E2_NonAlternatingValueSequence_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Rejected", ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_SameValueRepeated_NoFinding()
    {
        // Four identical values in a row is not "oscillating" -- there is only one state, not
        // two alternating ones.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft", ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Draft", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft", ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Draft", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_OnlyThreeValueChanges_NoFinding()
    {
        // A→B→A is only 3 values -- the minimum alternating window this rule looks for is 4
        // (A→B→A→B), so a partial reversal alone is not enough to flag.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_DifferentFieldsOnSameObject_EvaluatedIndependently()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),  objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Create", newValue: "Alice",    ts: new DateTime(2024, 1, 1, 10, 30, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Owner"),
            Row(uid: "3", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc),  objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Bob",      ts: new DateTime(2024, 1, 1, 11, 30, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Owner"),
            Row(uid: "5", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),  objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "6", operation: "Update", newValue: "Carol",    ts: new DateTime(2024, 1, 1, 12, 30, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Owner"),
            Row(uid: "7", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc),  objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "8", operation: "Update", newValue: "Dave",     ts: new DateTime(2024, 1, 1, 13, 30, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Owner"),
        };

        var results = Apply(rows, r28: true);

        AssertFinding(results[6], "[E2]");   // Status: Draft, Approved, Draft, Approved -> oscillating
        AssertNoFinding(results[7], "[E2]"); // Owner: Alice, Bob, Carol, Dave -- never repeats
    }

    [TestMethod]
    public void E2_DifferentObjects_EvaluatedIndependently()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "5", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B2", field: "Status"),
            Row(uid: "6", operation: "Update", newValue: "Rejected", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B2", field: "Status"),
            Row(uid: "7", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B2", field: "Status"),
            Row(uid: "8", operation: "Update", newValue: "Closed",   ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B2", field: "Status"),
        };

        var results = Apply(rows, r28: true);

        AssertFinding(results[3], "[E2]");   // B1's Status field oscillates
        AssertNoFinding(results[7], "[E2]"); // B2's Status field does not
    }

    [TestMethod]
    public void E2_CaseInsensitiveAndTrimmedValueMatching_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",      ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: " APPROVED ", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "draft",      ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved",   ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        AssertFinding(results[3], "[E2]");
    }

    [TestMethod]
    public void E2_DeleteResetsSequence_NotFlaggedAcrossDeletion()
    {
        // The value re-established after a deletion starts a fresh sequence -- it is not
        // compared against values from before the deletion, the same reset A2 already applies.
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Delete", oldValue: "Approved", newValue: null, ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "5", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_RowsWithoutObject_ExcludedFromEvaluation()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: null, objectName: null, field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: null, objectName: null, field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: null, objectName: null, field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: null, objectName: null, field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_Disabled_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: false);
        Assert.IsTrue(results.All(r => !r.Finding!.Contains("[E2]")));
    }

    [TestMethod]
    public void E2_FindingMessage_ContainsBothValues()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(uid: "1", operation: "Create", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "2", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 11, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "3", operation: "Update", newValue: "Draft",    ts: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
            Row(uid: "4", operation: "Update", newValue: "Approved", ts: new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), objectType: "Batch", objectName: "B1", field: "Status"),
        };

        var results = Apply(rows, r28: true);
        Assert.IsTrue(results[3].Finding!.Contains("Draft"));
        Assert.IsTrue(results[3].Finding!.Contains("Approved"));
    }

    // ─── Rule E3 — Nullifying Data ────────────────────────────────────────────

    [TestMethod]
    public void E3_UpdateFromValueToBlank_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "45.5", newValue: ""),
        };

        var results = Apply(rows, r29: true);
        AssertFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_UpdateFromValueToZero_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "45.5", newValue: "0"),
        };

        var results = Apply(rows, r29: true);
        AssertFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_UpdateFromZeroToBlank_NoFinding()
    {
        // The old value wasn't "genuine" to begin with -- it was already zero, so there is
        // nothing meaningful being nullified.
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "0", newValue: ""),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_UpdateFromBlankToZero_NoFinding()
    {
        // The old value was already blank -- nothing genuine to nullify.
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "", newValue: "0"),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_UpdateBetweenTwoNonZeroValues_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "45.5", newValue: "46.0"),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_UpdateFromZeroToNonZero_NoFinding()
    {
        // Restoring a real value from zero is the opposite of nullifying -- never flagged.
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "0", newValue: "45.5"),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_CreateWithZeroValue_NoFinding()
    {
        // This rule only looks at Update rows -- a Create establishing an initial value of "0"
        // is not "nullifying" anything (there was no prior genuine value to lose).
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Create", oldValue: null, newValue: "0"),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_DeleteRetainingOldValue_NoFinding()
    {
        // A Delete retaining its old value (correct B7 behavior) is not an Update, so E3 never
        // applies to it -- nullifying-via-Update and deletion are different concerns.
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Delete", oldValue: "45.5", newValue: null),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_CaseAndWhitespaceInsensitive_FindingGenerated()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "UPDATE", oldValue: " 45.5 ", newValue: "   "),
        };

        var results = Apply(rows, r29: true);
        AssertFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_NullOldValue_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: null, newValue: ""),
        };

        var results = Apply(rows, r29: true);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_Disabled_NoFinding()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "45.5", newValue: ""),
        };

        var results = Apply(rows, r29: false);
        AssertNoFinding(results[0], "[E3]");
    }

    [TestMethod]
    public void E3_FindingMessage_ContainsOldValue()
    {
        var rows = new List<AuditTrailInfo>
        {
            Row(operation: "Update", oldValue: "45.5", newValue: ""),
        };

        var results = Apply(rows, r29: true);
        Assert.IsTrue(results[0].Finding!.Contains("45.5"));
    }

    // ─── Multiple Rules in a Single Row ──────────────────────────────────────

    [TestMethod]
    public void MultipleRules_RowWithSeveralViolations_AllFindingsInSingleResult()
    {
        // This row violates A1 (no user), B5 (no reason for update)
        var row = new AuditTrailInfo
        {
            UserID       = null,
            Operation    = "Update",
            Timestamp    = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local),
            TimestampRaw = "2024-01-01T10:00:00",
            OldValue     = "Old",
            NewValue     = "New",
            Reason       = ""
        };

        var results = Apply([row], r5: true, r13: true);
        var finding = results[0].Finding;

        Assert.IsTrue(finding!.Contains("[A1]"), "Expected [A1]");
        Assert.IsTrue(finding!.Contains("[B5]"), "Expected [B5]");
    }

    // ─── ConvertScanResultsToJson ─────────────────────────────────────────────

    [TestMethod]
    public void ConvertScanResultsToJson_EmptyList_ReturnsEmptyJsonArray()
    {
        var json = ScanResultService.ConvertScanResultsToJson([]);
        Assert.AreEqual("[]", json.Trim());
    }

    [TestMethod]
    public void ConvertScanResultsToJson_SingleResult_ReturnsValidJson()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "jdoe", Finding = "[A1] test" }
        };
        var json = ScanResultService.ConvertScanResultsToJson(results);
        Assert.IsTrue(json.Contains("jdoe"));
        Assert.IsTrue(json.Contains("[A1] test"));
    }

    [TestMethod]
    public void ConvertScanResultsToJson_Uncapped_IncludesAllRowsRegardlessOfFinding()
    {
        // The uncapped overload is the "full scan" tier's payload builder (a scan with
        // AiSettings.FullScanRowThreshold rows or fewer) -- it must send EVERY row, flagged or
        // not, so the AI can independently analyze the whole audit trail.
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "clean1", Finding = "" },
            new() { ScanID = 1, UserID = "flagged", Finding = "[A1] test" },
            new() { ScanID = 1, UserID = "clean2", Finding = "   " },
            new() { ScanID = 1, UserID = "clean3", Finding = null }
        };

        var json = ScanResultService.ConvertScanResultsToJson(results);

        Assert.IsTrue(json.Contains("flagged"));
        Assert.IsTrue(json.Contains("clean1"));
        Assert.IsTrue(json.Contains("clean2"));
        Assert.IsTrue(json.Contains("clean3"));
    }

    [TestMethod]
    public void ConvertScanResultsToJson_Capped_FiltersOutRowsWithoutFindings()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "clean1", Finding = "" },
            new() { ScanID = 1, UserID = "flagged", Finding = "[A1] test" },
            new() { ScanID = 1, UserID = "clean2", Finding = "   " },
            new() { ScanID = 1, UserID = "clean3", Finding = null }
        };

        var json = ScanResultService.ConvertScanResultsToJson(results, maxFindings: 5, out var sampled);

        Assert.IsFalse(sampled);
        Assert.IsTrue(json.Contains("flagged"));
        Assert.IsFalse(json.Contains("clean1"));
        Assert.IsFalse(json.Contains("clean2"));
        Assert.IsFalse(json.Contains("clean3"));
    }

    [TestMethod]
    public void ConvertScanResultsToJson_FewerFindingsThanCap_IncludesAllAndReportsNotSampled()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "a", Finding = "[A1] one" },
            new() { ScanID = 1, UserID = "b", Finding = "[A1] two" }
        };

        var json = ScanResultService.ConvertScanResultsToJson(results, maxFindings: 5, out var sampled);

        Assert.IsFalse(sampled);
        Assert.IsTrue(json.Contains("\"a\""));
        Assert.IsTrue(json.Contains("\"b\""));
    }

    [TestMethod]
    public void ConvertScanResultsToJson_FindingsCountEqualsCap_ReportsNotSampled()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "a", Finding = "[A1] one" },
            new() { ScanID = 1, UserID = "b", Finding = "[A1] two" }
        };

        ScanResultService.ConvertScanResultsToJson(results, maxFindings: 2, out var sampled);

        Assert.IsFalse(sampled);
    }

    [TestMethod]
    public void ConvertScanResultsToJson_MoreFindingsThanCap_SamplesEvenlyAndReportsSampled()
    {
        // 10 findings, cap of 3 -- proves the selection is an even spread across the whole
        // ordered set (first, last, and one in between), not just a prefix of the first 3.
        var results = Enumerable.Range(1, 10)
            .Select(i => new ScanResultInfo { ScanID = 1, UserID = $"user{i}", Finding = $"[A1] finding {i}" })
            .ToList();

        var json = ScanResultService.ConvertScanResultsToJson(results, maxFindings: 3, out var sampled);

        Assert.IsTrue(sampled);
        var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(3, parsed.RootElement.GetArrayLength());
        Assert.IsTrue(json.Contains("user1"), "Expected the first finding to be included in the sample.");
        Assert.IsTrue(json.Contains("user10"), "Expected the last finding to be included in the sample.");
    }

    [TestMethod]
    public void ConvertScanResultsToJson_Compact_OutputContainsNoNewlines()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "a", Finding = "[A1] one" }
        };

        var json = ScanResultService.ConvertScanResultsToJson(results);

        Assert.IsFalse(json.Contains('\n'));
    }

    // ─── ShouldUseFindingsOnlyTier ───────────────────────────────────────────

    [TestMethod]
    public void ShouldUseFindingsOnlyTier_TotalRowsEqualsThreshold_ReturnsFalse()
    {
        Assert.IsFalse(ScanResultService.ShouldUseFindingsOnlyTier(totalRows: 300, fullScanRowThreshold: 300));
    }

    [TestMethod]
    public void ShouldUseFindingsOnlyTier_TotalRowsOneOverThreshold_ReturnsTrue()
    {
        Assert.IsTrue(ScanResultService.ShouldUseFindingsOnlyTier(totalRows: 301, fullScanRowThreshold: 300));
    }

    [TestMethod]
    public void ShouldUseFindingsOnlyTier_TotalRowsBelowThreshold_ReturnsFalse()
    {
        Assert.IsFalse(ScanResultService.ShouldUseFindingsOnlyTier(totalRows: 1, fullScanRowThreshold: 300));
    }

    // ─── BuildScanStatisticalSummaryJson ────────────────────────────────────

    [TestMethod]
    public void BuildScanStatisticalSummaryJson_EmptyList_ReturnsWellFormedZeroedSummary()
    {
        var json = ScanResultService.BuildScanStatisticalSummaryJson([]);

        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
        Assert.AreEqual(0, parsed.GetProperty("totalRows").GetInt32());
        Assert.AreEqual(0, parsed.GetProperty("totalFindings").GetInt32());
        Assert.AreEqual(0, parsed.GetProperty("missingReasonCount").GetInt32());
        Assert.AreEqual(0, parsed.GetProperty("topUsersByRecordCount").GetArrayLength());
        Assert.AreEqual(24, parsed.GetProperty("recordsByHourOfDay").GetArrayLength());
        Assert.AreEqual(7, parsed.GetProperty("recordsByDayOfWeek").GetArrayLength());
    }

    [TestMethod]
    public void BuildScanStatisticalSummaryJson_ComputesCountsCorrectly()
    {
        var results = new List<ScanResultInfo>
        {
            new() { ScanID = 1, UserID = "alice", Operation = "Update", ObjectType = "Batch", Reason = "fix", Finding = "[A1] x", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0) },
            new() { ScanID = 1, UserID = "alice", Operation = "Update", ObjectType = "Batch", Reason = "", Finding = "", Timestamp = new DateTime(2026, 1, 2, 11, 0, 0) },
            new() { ScanID = 1, UserID = "bob",   Operation = "Create", ObjectType = "Equipment", Reason = null, Finding = "[B3] y", Timestamp = null }
        };

        var json = ScanResultService.BuildScanStatisticalSummaryJson(results);
        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;

        Assert.AreEqual(3, parsed.GetProperty("totalRows").GetInt32());
        Assert.AreEqual(2, parsed.GetProperty("totalFindings").GetInt32());
        Assert.AreEqual(2, parsed.GetProperty("missingReasonCount").GetInt32());

        var dateStart = parsed.GetProperty("dateRangeStart").GetDateTime();
        var dateEnd = parsed.GetProperty("dateRangeEnd").GetDateTime();
        Assert.AreEqual(new DateTime(2026, 1, 1, 10, 0, 0), dateStart);
        Assert.AreEqual(new DateTime(2026, 1, 2, 11, 0, 0), dateEnd);

        var topUsers = parsed.GetProperty("topUsersByRecordCount");
        Assert.AreEqual("alice", topUsers[0].GetProperty("name").GetString());
        Assert.AreEqual(2, topUsers[0].GetProperty("count").GetInt32());

        // Two rows have a non-null Timestamp (Jan 1 10:00 and Jan 2 11:00); the third row's
        // null Timestamp must not be counted into either histogram.
        var byHour = parsed.GetProperty("recordsByHourOfDay");
        Assert.AreEqual(1, byHour[10].GetInt32());
        Assert.AreEqual(1, byHour[11].GetInt32());
    }

    [TestMethod]
    public void BuildScanStatisticalSummaryJson_TopUsersCappedAtTen()
    {
        var results = Enumerable.Range(1, 15)
            .Select(i => new ScanResultInfo { ScanID = 1, UserID = $"user{i}" })
            .ToList();

        var json = ScanResultService.BuildScanStatisticalSummaryJson(results);
        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;

        Assert.AreEqual(10, parsed.GetProperty("topUsersByRecordCount").GetArrayLength());
    }

    // ─── ParseOpenAIResponse ─────────────────────────────────────────────────

    [TestMethod]
    public void ParseOpenAIResponse_NullOrEmpty_ReturnsEmptyStrings()
    {
        var (insights, recommendations) = ScanResultService.ParseOpenAIResponse(string.Empty);
        Assert.AreEqual(string.Empty, insights);
        Assert.AreEqual(string.Empty, recommendations);
    }

    [TestMethod]
    public void ParseOpenAIResponse_ValidResponse_ExtractsInsightsAndRecommendations()
    {
        // Arrange — minimal valid OpenAI API response wrapper
        var response = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"insights\":\"Found issues.\",\"recommendations\":\"Fix them.\"}"
                  }
                }
              ]
            }
            """;

        // Act
        var (insights, recommendations) = ScanResultService.ParseOpenAIResponse(response);

        // Assert
        Assert.AreEqual("Found issues.", insights);
        Assert.AreEqual("Fix them.", recommendations);
    }

    [TestMethod]
    public void ParseOpenAIResponse_InvalidJson_ReturnsErrorInInsights()
    {
        var (insights, _) = ScanResultService.ParseOpenAIResponse("not json at all");
        Assert.IsTrue(insights.Contains("Error") || !string.IsNullOrEmpty(insights));
    }

    [TestMethod]
    public void ParseOpenAIResponse_InsightsAsFindingGroupArray_RendersReadableSummaryNotRawJson()
    {
        // User-reported (AT-0030-shaped): despite AuditTrailPromptBuilder.Build() instructing the
        // AI that "insights" must be a plain string, a model returned a JSON array of
        // { "finding", "records": [...] } groups instead -- JToken.ToString() on that array
        // silently returns raw indented JSON rather than throwing, which used to reach the
        // reviewer as an unreadable dump. This locks in the human-readable fallback instead.
        var response = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"insights\":[{\"finding\":\"[E1] Mass change detected\",\"records\":[{\"timestamp\":\"2026-08-25T11:00:00Z\",\"userID\":\"twojcik\",\"object\":\"EQUIP-3017-10\",\"field\":\"Status\"}]}],\"recommendations\":\"Review the burst.\"}"
                  }
                }
              ]
            }
            """;

        var (insights, recommendations) = ScanResultService.ParseOpenAIResponse(response);

        // Exact equality, not a "doesn't start with '[' " heuristic -- rule-coded finding text
        // legitimately starts with "[E1]" etc. throughout this codebase, so that heuristic can't
        // tell readable text apart from raw JSON (both start with "["). Assert the precise
        // humanized shape instead.
        Assert.AreEqual(
            "[E1] Mass change detected: EQUIP-3017-10 (Status) by twojcik at 2026-08-25T11:00:00Z",
            insights);
        Assert.AreEqual("Review the burst.", recommendations);
    }

    [TestMethod]
    public void ParseOpenAIResponse_RecommendationsAsFindingGroupArray_RendersReadableSummaryNotRawJson()
    {
        // Same fallback, exercised on "recommendations" instead of "insights" -- both fields go
        // through the same HumanizeAiResponseValue helper, so both need coverage.
        var response = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"insights\":\"Ten equipment records changed within the same minute.\",\"recommendations\":[{\"finding\":\"[B8] Segregation of duties\",\"records\":[{\"timestamp\":\"2026-08-25T09:30:00Z\",\"userID\":\"jkowalski\",\"object\":\"BATCH-3011\",\"field\":\"Status\"}]}]}"
                  }
                }
              ]
            }
            """;

        var (insights, recommendations) = ScanResultService.ParseOpenAIResponse(response);

        Assert.AreEqual("Ten equipment records changed within the same minute.", insights);
        // Same exact-equality reasoning as the insights test above -- "[B8] Segregation of
        // duties" legitimately starts with "[", so a StartsWith("[") check can't distinguish
        // readable text from raw JSON here.
        Assert.AreEqual(
            "[B8] Segregation of duties: BATCH-3011 (Status) by jkowalski at 2026-08-25T09:30:00Z",
            recommendations);
    }

    [TestMethod]
    public void ParseOpenAIResponse_InsightsAsUnrecognizedJsonShape_FallsBackToCompactJsonNotIndented()
    {
        // A shape HumanizeAiResponseValue doesn't specifically recognize (no "finding"/"records"
        // keys) should still not come back as Newtonsoft's default multi-line indented dump --
        // compact JSON is a lesser evil, kept as a last-resort fallback rather than a target.
        var response = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"insights\":[{\"note\":\"unexpected shape\"}],\"recommendations\":\"n/a\"}"
                  }
                }
              ]
            }
            """;

        var (insights, _) = ScanResultService.ParseOpenAIResponse(response);

        StringAssert.Contains(insights, "unexpected shape");
        Assert.IsFalse(insights.Contains("\r\n") || insights.Contains("\n"), "Expected compact JSON, not an indented multi-line dump.");
    }
}
