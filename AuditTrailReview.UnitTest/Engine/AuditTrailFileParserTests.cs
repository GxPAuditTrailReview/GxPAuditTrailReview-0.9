using AuditTrailReview.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

[TestClass]
public class AuditTrailFileParserTests
{
    // ─── IsSupportedExtension ────────────────────────────────────────────────

    [TestMethod]
    [DataRow(".csv")]
    [DataRow(".xml")]
    [DataRow(".json")]
    [DataRow(".CSV")]
    [DataRow(".Json")]
    public void IsSupportedExtension_SupportedExtension_ReturnsTrue(string extension)
    {
        Assert.IsTrue(AuditTrailFileParser.IsSupportedExtension(extension));
    }

    [TestMethod]
    [DataRow(".txt")]
    [DataRow(".pdf")]
    [DataRow("")]
    public void IsSupportedExtension_UnsupportedExtension_ReturnsFalse(string extension)
    {
        Assert.IsFalse(AuditTrailFileParser.IsSupportedExtension(extension));
    }

    // ─── ParseContent – CSV ──────────────────────────────────────────────────

    [TestMethod]
    public void ParseContent_CommaDelimitedCsv_ParsesRowsAndHeaders()
    {
        const string csv = "Id,User,Action\r\n1,jdoe,Create\r\n2,asmith,Update\r\n";

        var rows = AuditTrailFileParser.ParseContent(csv, ".csv");

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
        Assert.AreEqual("Update", rows[1]["Action"].ToString());
    }

    [TestMethod]
    public void ParseContent_SemicolonDelimitedCsv_DetectsDelimiterAndParses()
    {
        const string csv = "Id;User;Action\r\n1;jdoe;Create\r\n";

        var rows = AuditTrailFileParser.ParseContent(csv, ".csv");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
    }

    [TestMethod]
    public void ParseContent_CsvWithBlankRow_SkipsBlankRow()
    {
        const string csv = "Id,User,Action\r\n1,jdoe,Create\r\n,,\r\n2,asmith,Update\r\n";

        var rows = AuditTrailFileParser.ParseContent(csv, ".csv");

        Assert.AreEqual(2, rows.Count);
    }

    // ─── ParseContent – JSON ─────────────────────────────────────────────────

    [TestMethod]
    public void ParseContent_JsonArray_ParsesRows()
    {
        const string json = "[{\"Id\":\"1\",\"User\":\"jdoe\"},{\"Id\":\"2\",\"User\":\"asmith\"}]";

        var rows = AuditTrailFileParser.ParseContent(json, ".json");

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
    }

    [TestMethod]
    public void ParseContent_JsonObjectWithArrayProperty_ParsesRows()
    {
        const string json = "{\"Records\":[{\"Id\":\"1\",\"User\":\"jdoe\"}]}";

        var rows = AuditTrailFileParser.ParseContent(json, ".json");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
    }

    [TestMethod]
    public void ParseContent_JsonRowWithOnlyBlankValues_IsSkipped()
    {
        const string json = "[{\"Id\":\"\",\"User\":\"   \"},{\"Id\":\"1\",\"User\":\"jdoe\"}]";

        var rows = AuditTrailFileParser.ParseContent(json, ".json");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
    }

    [TestMethod]
    public void ParseContent_JsonSingleObjectWithNoArrayProperty_ReturnsNoRows()
    {
        // Canonical (Scan.cshtml.cs / ScanProcessingService) behavior: unlike Preview.cshtml.cs's
        // old, now-removed fallback, a lone object with no array-valued property yields zero
        // rows rather than being treated as a single record. Path to v1.0.md item 23.
        const string json = "{\"Id\":\"1\",\"User\":\"jdoe\"}";

        var rows = AuditTrailFileParser.ParseContent(json, ".json");

        Assert.AreEqual(0, rows.Count);
    }

    // ─── ParseContent – XML ──────────────────────────────────────────────────

    [TestMethod]
    public void ParseContent_XmlWithChildElements_ParsesRows()
    {
        const string xml = "<Records><Record><Id>1</Id><User>jdoe</User></Record></Records>";

        var rows = AuditTrailFileParser.ParseContent(xml, ".xml");

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("jdoe", rows[0]["User"].ToString());
    }

    [TestMethod]
    public void ParseContent_XmlRecordWithBlankChildValues_IsSkipped()
    {
        const string xml =
            "<Records><Record><Id></Id><User></User></Record>" +
            "<Record><Id>1</Id><User>jdoe</User></Record></Records>";

        var rows = AuditTrailFileParser.ParseContent(xml, ".xml");

        Assert.AreEqual(1, rows.Count);
    }

    [TestMethod]
    public void ParseContent_XmlRecordWithOnlyAttributesAndNoChildElements_ReturnsNoRows()
    {
        // Canonical (Scan.cshtml.cs / ScanProcessingService) behavior: unlike Preview.cshtml.cs's
        // old, now-removed fallback, an element with no child elements is never expanded from its
        // attributes -- it has no Elements() at all, so it's skipped as blank. Path to v1.0.md
        // item 23.
        const string xml = "<Records><Record Id=\"1\" User=\"jdoe\" /></Records>";

        var rows = AuditTrailFileParser.ParseContent(xml, ".xml");

        Assert.AreEqual(0, rows.Count);
    }
}
