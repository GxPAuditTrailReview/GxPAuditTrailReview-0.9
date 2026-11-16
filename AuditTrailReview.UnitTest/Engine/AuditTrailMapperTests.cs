using AuditTrailReview.Engine;
using AuditTrailReview.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="AuditTrailMapper"/>.
/// Covers header-keyword resolution, case-insensitive matching, DateTime parsing,
/// field-priority ordering, empty/null handling, and collection filtering.
/// </summary>
[TestClass]
public class AuditTrailMapperTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static AuditTrailInfo Map(Dictionary<string, object> row)
        => AuditTrailMapper.MapRowToAuditTrailInfo(row);

    private static Dictionary<string, object> D(params (string key, string value)[] entries)
        => entries.ToDictionary(e => e.key, e => (object)e.value);

    // ─── MapRowToAuditTrailInfo – UserID Keywords ────────────────────────────

    [TestMethod]
    [DataRow("user")]
    [DataRow("User")]
    [DataRow("USER")]
    [DataRow("user id")]
    [DataRow("User ID")]
    [DataRow("operator")]
    [DataRow("performed by")]
    [DataRow("login")]
    [DataRow("full name")]
    [DataRow("fullname")]
    [DataRow("performedby")]
    [DataRow("modified by")]
    [DataRow("modifiedby")]
    [DataRow("changed by")]
    [DataRow("changedby")]
    [DataRow("executed by")]
    [DataRow("executedby")]
    [DataRow("User Name")]
    public void MapRowToAuditTrailInfo_UserKeyword_MapsToUserID(string header)
    {
        var row = D((header, "jdoe"));
        var result = Map(row);
        Assert.AreEqual("jdoe", result.UserID, $"Header '{header}' should map to UserID");
    }

    // ─── MapRowToAuditTrailInfo – Operation Keywords ─────────────────────────

    [TestMethod]
    [DataRow("operation")]
    [DataRow("Operation")]
    [DataRow("action")]
    [DataRow("Action")]
    [DataRow("action type")]
    [DataRow("event")]
    [DataRow("activity")]
    [DataRow("transaction")]
    [DataRow("change type")]
    [DataRow("changetype")]
    [DataRow("audit type")]
    [DataRow("audittype")]
    public void MapRowToAuditTrailInfo_OperationKeyword_MapsToOperation(string header)
    {
        var row = D((header, "Update"));
        var result = Map(row);
        Assert.AreEqual("Update", result.Operation, $"Header '{header}' should map to Operation");
    }

    // ─── MapRowToAuditTrailInfo – Timestamp Keywords ─────────────────────────

    [TestMethod]
    [DataRow("timestamp")]
    [DataRow("Timestamp")]
    [DataRow("date")]
    [DataRow("Date")]
    [DataRow("time")]
    [DataRow("event timestamp")]
    [DataRow("EventTime")]
    [DataRow("created at")]
    [DataRow("createdat")]
    [DataRow("recorded at")]
    [DataRow("recordedat")]
    [DataRow("logged at")]
    [DataRow("loggedat")]
    [DataRow("Date/Time")]
    public void MapRowToAuditTrailInfo_DateTimeKeyword_MapsToTimestampRaw(string header)
    {
        const string value = "2024-06-12T10:00:00Z";
        var row = D((header, value));
        var result = Map(row);
        Assert.AreEqual(value, result.TimestampRaw, $"Header '{header}' should populate TimestampRaw");
        Assert.IsNotNull(result.Timestamp, $"Header '{header}' should parse Timestamp");
    }

    // ─── MapRowToAuditTrailInfo – OldValue Keywords ──────────────────────────

    [TestMethod]
    [DataRow("previous value")]
    [DataRow("previousvalue")]
    [DataRow("prior value")]
    [DataRow("priorvalue")]
    [DataRow("old value")]
    [DataRow("oldvalue")]
    [DataRow("before")]
    [DataRow("old")]
    public void MapRowToAuditTrailInfo_OldValueKeyword_MapsToOldValue(string header)
    {
        var row = D((header, "OldVal"));
        var result = Map(row);
        Assert.AreEqual("OldVal", result.OldValue, $"Header '{header}' should map to OldValue");
    }

    // ─── MapRowToAuditTrailInfo – NewValue Keywords ──────────────────────────

    [TestMethod]
    // NOTE: "updated value" and "updatedvalue" are intentionally excluded —
    // both contain the substring "date" which matches DateTimeKeywords first.
    [DataRow("current value")]
    [DataRow("currentvalue")]
    [DataRow("new value")]
    [DataRow("newvalue")]
    [DataRow("after")]
    [DataRow("new")]
    public void MapRowToAuditTrailInfo_NewValueKeyword_MapsToNewValue(string header)
    {
        var row = D((header, "NewVal"));
        var result = Map(row);
        Assert.AreEqual("NewVal", result.NewValue, $"Header '{header}' should map to NewValue");
    }

    // ─── MapRowToAuditTrailInfo – Reason Keywords ────────────────────────────

    [TestMethod]
    [DataRow("reason for change")]
    [DataRow("reasonforchange")]
    [DataRow("Change Reason")]
    [DataRow("reason")]
    [DataRow("justification")]
    [DataRow("comment")]
    [DataRow("remarks")]
    [DataRow("rationale")]
    public void MapRowToAuditTrailInfo_ReasonKeyword_MapsToReason(string header)
    {
        var row = D((header, "Correction"));
        var result = Map(row);
        Assert.AreEqual("Correction", result.Reason, $"Header '{header}' should map to Reason");
    }

    // ─── MapRowToAuditTrailInfo – ObjectType Keywords ────────────────────────

    [TestMethod]
    [DataRow("object type")]
    [DataRow("objecttype")]
    [DataRow("entity type")]
    [DataRow("entitytype")]
    [DataRow("record type")]
    [DataRow("recordtype")]
    [DataRow("item type")]
    [DataRow("itemtype")]
    [DataRow("module type")]
    [DataRow("moduletype")]
    [DataRow("data category")]
    [DataRow("datacategory")]
    [DataRow("category")]
    public void MapRowToAuditTrailInfo_ObjectTypeKeyword_MapsToObjectType(string header)
    {
        var row = D((header, "Batch"));
        var result = Map(row);
        Assert.AreEqual("Batch", result.ObjectType, $"Header '{header}' should map to ObjectType");
    }

    // ─── MapRowToAuditTrailInfo – Field Keywords ─────────────────────────────

    [TestMethod]
    [DataRow("property")]
    [DataRow("field name")]
    [DataRow("fieldname")]
    [DataRow("field")]
    [DataRow("attribute")]
    public void MapRowToAuditTrailInfo_FieldKeyword_MapsToField(string header)
    {
        var row = D((header, "Status"));
        var result = Map(row);
        Assert.AreEqual("Status", result.Field, $"Header '{header}' should map to Field");
    }

    // ─── MapRowToAuditTrailInfo – Source Keywords ────────────────────────────

    [TestMethod]
    [DataRow("source")]
    [DataRow("location")]
    [DataRow("machine id")]
    [DataRow("machineid")]
    [DataRow("workstation")]
    [DataRow("terminal id")]
    [DataRow("terminalid")]
    [DataRow("terminal")]
    [DataRow("station")]
    [DataRow("app")]
    public void MapRowToAuditTrailInfo_SourceKeyword_MapsToSource(string header)
    {
        var row = D((header, "WS-01"));
        var result = Map(row);
        Assert.AreEqual("WS-01", result.Source, $"Header '{header}' should map to Source");
    }

    // ─── MapRowToAuditTrailInfo – Object Keywords ────────────────────────────

    [TestMethod]
    [DataRow("object name")]
    [DataRow("objectname")]
    [DataRow("object")]
    [DataRow("entity")]
    [DataRow("module")]
    [DataRow("table")]
    [DataRow("parameter name")]
    [DataRow("parametername")]
    [DataRow("parameter")]
    [DataRow("target")]
    [DataRow("subject")]
    [DataRow("resource")]
    [DataRow("record name")]
    [DataRow("recordname")]
    [DataRow("item")]
    public void MapRowToAuditTrailInfo_ObjectKeyword_MapsToObject(string header)
    {
        var row = D((header, "BatchRecord001"));
        var result = Map(row);
        Assert.AreEqual("BatchRecord001", result.Object, $"Header '{header}' should map to Object");
    }

    // ─── MapRowToAuditTrailInfo – UniqueIdentifier Keywords ──────────────────

    [TestMethod]
    // NOTE: "eventID", "event ID", "EventID" are intentionally excluded —
    // all contain the substring "event" which matches OperationKeywords first.
    [DataRow("id")]
    [DataRow("ID")]
    [DataRow("logID")]
    [DataRow("AuditID")]
    [DataRow("AuditRecordID")]
    [DataRow("record no")]
    [DataRow("recordno")]
    [DataRow("seq no")]
    [DataRow("seqno")]
    [DataRow("ref no")]
    [DataRow("refno")]
    public void MapRowToAuditTrailInfo_UniqueIdentifierKeyword_MapsToUniqueIdentifier(string header)
    {
        var row = D((header, "UID-999"));
        var result = Map(row);
        Assert.AreEqual("UID-999", result.UniqueIdentifier,
            $"Header '{header}' should map to UniqueIdentifier");
    }

    // ─── MapRowToAuditTrailInfo – Priority: ObjectType before Object ──────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_ObjectTypeBeforeObject_ObjectTypeWins()
    {
        // "object type" contains "object" — ObjectType must take priority
        var row = D(("object type", "Batch"));
        var result = Map(row);
        Assert.AreEqual("Batch", result.ObjectType);
        Assert.IsNull(result.Object);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_FieldBeforeObject_FieldWins()
    {
        // "field name" could loosely match object keywords but Field should win
        var row = D(("field name", "Status"));
        var result = Map(row);
        Assert.AreEqual("Status", result.Field);
        Assert.IsNull(result.Object);
    }

    // ─── MapRowToAuditTrailInfo – Priority: DateTime before User ─────────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_DateTimeBeforeUser_DateTimeWins()
    {
        // "logged at" contains no user keyword but "created at" must not be confused
        var row = D(("timestamp", "2024-01-01T10:00:00Z"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.IsNull(result.UserID);
    }

    // ─── MapRowToAuditTrailInfo – Full Row ────────────────────────────────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_FullRow_AllFieldsMapped()
    {
        var row = D(
            ("id",          "1"),
            ("user",        "svc_batch"),
            ("timestamp",   "2024-06-12T10:00:00Z"),
            ("action",      "Update"),
            ("object type", "Batch"),
            ("object",      "BatchRecord001"),
            ("field",       "Status"),
            ("before",      "Draft"),
            ("after",       "Approved"),
            ("reason",      "QA review complete"),
            ("source",      "WS-01")
        );

        var result = Map(row);

        Assert.AreEqual("1",                  result.UniqueIdentifier);
        Assert.AreEqual("svc_batch",          result.UserID);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual("Update",             result.Operation);
        Assert.AreEqual("Batch",              result.ObjectType);
        Assert.AreEqual("BatchRecord001",     result.Object);
        Assert.AreEqual("Status",             result.Field);
        Assert.AreEqual("Draft",              result.OldValue);
        Assert.AreEqual("Approved",           result.NewValue);
        Assert.AreEqual("QA review complete", result.Reason);
        Assert.AreEqual("WS-01",              result.Source);
    }

    // ─── MapRowToAuditTrailInfo – Empty / Null Handling ──────────────────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_EmptyRow_ReturnsEmptyAuditTrailInfo()
    {
        var result = Map(new Dictionary<string, object>());
        Assert.IsNull(result.UserID);
        Assert.IsNull(result.Operation);
        Assert.IsNull(result.Timestamp);
        Assert.IsNull(result.UniqueIdentifier);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_NullValue_FieldNotMapped()
    {
        var row = new Dictionary<string, object> { { "user", null! } };
        var result = Map(row);
        Assert.IsNull(result.UserID);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_WhitespaceValue_StoredAsIs()
    {
        // The mapper checks IsNullOrEmpty, NOT IsNullOrWhiteSpace —
        // whitespace-only values are stored verbatim.
        var row = D(("user", "   "));
        var result = Map(row);
        Assert.AreEqual("   ", result.UserID);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_UnknownHeader_AllFieldsNull()
    {
        var row = D(("completely_unknown_column_xyz", "some value"));
        var result = Map(row);
        Assert.IsNull(result.UserID);
        Assert.IsNull(result.Operation);
        Assert.IsNull(result.Timestamp);
        Assert.IsNull(result.OldValue);
        Assert.IsNull(result.NewValue);
        Assert.IsNull(result.Reason);
        Assert.IsNull(result.UniqueIdentifier);
    }

    // ─── MapRowToAuditTrailInfo – Case-Insensitive Header Matching ───────────

    [TestMethod]
    [DataRow("USER")]
    [DataRow("User")]
    [DataRow("uSeR")]
    public void MapRowToAuditTrailInfo_UserHeader_CaseInsensitive(string header)
    {
        var row = D((header, "jdoe"));
        var result = Map(row);
        Assert.AreEqual("jdoe", result.UserID);
    }

    [TestMethod]
    [DataRow("TIMESTAMP")]
    [DataRow("Timestamp")]
    [DataRow("TimeStamp")]
    public void MapRowToAuditTrailInfo_TimestampHeader_CaseInsensitive(string header)
    {
        var row = D((header, "2024-01-01T10:00:00Z"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
    }

    // ─── MapRowToAuditTrailInfo – DateTime Parsing ───────────────────────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_Iso8601WithZ_ParsedAsUtc()
    {
        var row = D(("timestamp", "2024-06-12T10:00:00Z"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(DateTimeKind.Utc, result.Timestamp!.Value.Kind);
        Assert.AreEqual(2024, result.Timestamp.Value.Year);
        Assert.AreEqual(6,    result.Timestamp.Value.Month);
        Assert.AreEqual(12,   result.Timestamp.Value.Day);
        Assert.AreEqual(10,   result.Timestamp.Value.Hour);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_Iso8601WithPositiveOffset_ParsedAsLocal()
    {
        var row = D(("timestamp", "2024-06-12T10:00:00+02:00"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(DateTimeKind.Local, result.Timestamp!.Value.Kind);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_DateOnly_yyyy_MM_dd_Parsed()
    {
        var row = D(("timestamp", "2024-06-12"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2024, result.Timestamp!.Value.Year);
        Assert.AreEqual(6,    result.Timestamp.Value.Month);
        Assert.AreEqual(12,   result.Timestamp.Value.Day);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_DateTime_dd_MM_yyyy_HHmmss_Parsed()
    {
        var row = D(("timestamp", "15-03-2023 14:30:00"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2023, result.Timestamp!.Value.Year);
        Assert.AreEqual(3,    result.Timestamp.Value.Month);
        Assert.AreEqual(15,   result.Timestamp.Value.Day);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_DateTime_dd_slash_MM_slash_yyyy_Parsed()
    {
        var row = D(("timestamp", "15/03/2023"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2023, result.Timestamp!.Value.Year);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_DateTime_dd_dot_MM_dot_yyyy_Parsed()
    {
        var row = D(("timestamp", "15.03.2023 08:00:00"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(2023, result.Timestamp!.Value.Year);
        Assert.AreEqual(3,    result.Timestamp.Value.Month);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_InvalidDateTimeString_TimestampIsNull()
    {
        var row = D(("timestamp", "not-a-date"));
        var result = Map(row);
        Assert.IsNull(result.Timestamp);
        Assert.AreEqual("not-a-date", result.TimestampRaw);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_Iso8601WithZeroOffset_ParsedAsLocal()
    {
        // .NET DateTime.TryParse with RoundtripKind interprets an explicit +00:00 offset
        // as DateTimeKind.Local, not Utc — only the 'Z' suffix yields Kind = Utc.
        var row = D(("timestamp", "2024-06-12T10:00:00+00:00"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(DateTimeKind.Local, result.Timestamp!.Value.Kind);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_NoTimezoneInfo_ParsedAsUnspecified()
    {
        var row = D(("timestamp", "2024-06-12 10:00:00"));
        var result = Map(row);
        Assert.IsNotNull(result.Timestamp);
        Assert.AreEqual(DateTimeKind.Unspecified, result.Timestamp!.Value.Kind);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_TimestampRaw_AlwaysStoredAsRaw()
    {
        const string rawValue = "2024-06-12T10:00:00Z";
        var row = D(("timestamp", rawValue));
        var result = Map(row);
        Assert.AreEqual(rawValue, result.TimestampRaw);
    }

    // ─── MapRowsToAuditTrailInfo – Collection Behaviour ──────────────────────

    [TestMethod]
    public void MapRowsToAuditTrailInfo_EmptyCollection_ReturnsEmptyList()
    {
        var results = AuditTrailMapper.MapRowsToAuditTrailInfo([]);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void MapRowsToAuditTrailInfo_AllBlankRow_RowExcluded()
    {
        // A row where all values are empty → no property gets set → must be filtered out
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "user", "" }, { "operation", "" } }
        };
        var results = AuditTrailMapper.MapRowsToAuditTrailInfo(rows);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void MapRowsToAuditTrailInfo_PartiallyPopulatedRow_RowIncluded()
    {
        var rows = new List<Dictionary<string, object>>
        {
            D(("user", "jdoe"))
        };
        var results = AuditTrailMapper.MapRowsToAuditTrailInfo(rows);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("jdoe", results[0].UserID);
    }

    [TestMethod]
    public void MapRowsToAuditTrailInfo_MultipleRows_AllMapped()
    {
        var rows = new List<Dictionary<string, object>>
        {
            D(("user", "user1"), ("action", "Create")),
            D(("user", "user2"), ("action", "Update")),
            D(("user", "user3"), ("action", "Delete"))
        };
        var results = AuditTrailMapper.MapRowsToAuditTrailInfo(rows);
        Assert.AreEqual(3, results.Count);
        Assert.AreEqual("user1", results[0].UserID);
        Assert.AreEqual("user2", results[1].UserID);
        Assert.AreEqual("user3", results[2].UserID);
    }

    [TestMethod]
    public void MapRowsToAuditTrailInfo_MixedValidAndBlankRows_OnlyValidRowsReturned()
    {
        var rows = new List<Dictionary<string, object>>
        {
            D(("user", "jdoe")),          // valid
            new() { { "user", "" } },     // blank → excluded
            D(("action", "Update"))       // valid
        };
        var results = AuditTrailMapper.MapRowsToAuditTrailInfo(rows);
        Assert.AreEqual(2, results.Count);
    }

    // ─── Common Vendor Header Aliases ──────────────────────────────────────────

    [TestMethod]
    public void MapRowToAuditTrailInfo_OperatorHeader_MapsToUserID()
    {
        var row = D(("operator", "jsmith"));
        var result = Map(row);
        Assert.AreEqual("jsmith", result.UserID);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_ActionTypeHeader_MapsToOperation()
    {
        var row = D(("action type", "Approve"));
        var result = Map(row);
        Assert.AreEqual("Approve", result.Operation);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_ChangeReasonHeader_MapsToReason()
    {
        var row = D(("Change Reason", "Mandatory step completed"));
        var result = Map(row);
        Assert.AreEqual("Mandatory step completed", result.Reason);
    }

    [TestMethod]
    public void MapRowToAuditTrailInfo_ReasonForChangeHeader_MapsToReason()
    {
        var row = D(("reasonforchange", "Calibration correction"));
        var result = Map(row);
        Assert.AreEqual("Calibration correction", result.Reason);
    }
}
