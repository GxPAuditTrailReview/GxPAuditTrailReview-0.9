using System.Text.Json;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.Scan;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Scan;

/// <summary>
/// Unit tests for <see cref="PreviewModel"/> (Pages/Scan/Preview.cshtml.cs). This handler
/// parses an uploaded CSV/XML/JSON file into rows and, separately, exports a TempData-carried
/// row set to an .xlsx file -- so these tests build a real IFormFile
/// (PageModelTestHelpers.CreateFormFile, backed by Microsoft.AspNetCore.Http.FormFile) rather
/// than a mock, and read the produced workbook back with ClosedXML rather than just checking
/// its byte length. OnPostExportExcelAsync formats its DateTime column via
/// IDateTimeDisplayService (see PageModelTestHelpers.CreateDateTimeDisplayService), so each
/// test gets its own throwaway InMemory ApplicationDbContext to back that service even though
/// this handler otherwise has no DbContext dependency.
/// </summary>
[TestClass]
public class PreviewModelTests
{
    private static PreviewModel CreateModel()
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var model = new PreviewModel(PageModelTestHelpers.CreateDateTimeDisplayService(context));
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    // ─── OnPostAsync -- input validation ───────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_NoFile_ReturnsPageWithoutParsing()
    {
        var model = CreateModel();

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, model.Rows.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_EmptyFile_ReturnsPageWithoutParsing()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.csv", string.Empty);

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, model.Rows.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_UnsupportedExtension_AddsModelErrorAndReturnsPage()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.txt", "Name,Value\nAlice,100\n");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected an unsupported-file-type ModelState error.");
        Assert.AreEqual(0, model.Rows.Count);
    }

    // ─── OnPostAsync -- CSV ─────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_ParsesRowsAndHeaders()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.csv", "Name,Value\nAlice,100\n");

        await model.OnPostAsync();

        CollectionAssert.AreEqual(new[] { "Name", "Value" }, model.Headers);
        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual("Alice", model.Rows[0]["Name"].ToString());
        Assert.AreEqual("100", model.Rows[0]["Value"].ToString());
    }

    [TestMethod]
    public async Task OnPostAsync_CsvWithSemicolonDelimiter_ParsesCorrectly()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.csv", "Name;Value\nAlice;100\n");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual("Alice", model.Rows[0]["Name"].ToString());
    }

    [TestMethod]
    public async Task OnPostAsync_CsvWithBlankRow_SkipsBlankRow()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.csv", "Name,Value\nAlice,100\n,\n");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count, "Expected the all-empty row to be skipped.");
    }

    // ─── OnPostAsync -- JSON ────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidJsonArray_ParsesRows()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.json", "[{\"Name\":\"Alice\",\"Value\":100}]");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual("Alice", model.Rows[0]["Name"].ToString());
        Assert.AreEqual("100", model.Rows[0]["Value"].ToString());
    }

    [TestMethod]
    public async Task OnPostAsync_ValidJsonObjectWithArrayProperty_ParsesRows()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.json", "{\"data\":[{\"Name\":\"Bob\"}]}");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual("Bob", model.Rows[0]["Name"].ToString());
    }

    // ─── OnPostAsync -- XML ─────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidXml_ParsesRows()
    {
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.xml", "<Root><Row><Name>Alice</Name><Value>100</Value></Row></Root>");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual("Alice", model.Rows[0]["Name"].ToString());
        Assert.AreEqual("100", model.Rows[0]["Value"].ToString());
    }

    // ─── OnPostAsync -- MappedRows gap ──────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidFile_MappedRowsStaysEmpty()
    {
        // Documents an actual gap: none of ParseCsvAsync/ParseJsonAsync/ParseXml ever assign
        // to MappedRows -- they only populate Rows. So TempData["MappedRowsJson"] is always
        // `JsonSerializer.Serialize(MappedRows)` on an empty list, regardless of what was
        // uploaded, and OnPostExportExcel (which reads that TempData value back) will always
        // export zero rows in practice. Not fixed here (out of scope for adding test coverage);
        // flagged so the gap is visible from the test suite itself.
        var model = CreateModel();
        model.CsvFile = PageModelTestHelpers.CreateFormFile("data.csv", "Name,Value\nAlice,100\n");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.Rows.Count, "Sanity check: the row itself was parsed.");
        Assert.AreEqual(0, model.MappedRows.Count, "MappedRows is never populated by any Parse* method.");
        Assert.AreEqual("[]", model.TempData["MappedRowsJson"]);
    }

    // ─── OnPostExportExcel ──────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostExportExcel_NoTempData_ReturnsPage()
    {
        var model = CreateModel();

        var result = await model.OnPostExportExcelAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
    }

    [TestMethod]
    public async Task OnPostExportExcel_EmptyMappedRowsJson_ReturnsPage()
    {
        var model = CreateModel();
        model.TempData["MappedRowsJson"] = JsonSerializer.Serialize(new List<AuditTrailInfo>());

        var result = await model.OnPostExportExcelAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
    }

    [TestMethod]
    public async Task OnPostExportExcel_ValidMappedRows_ReturnsExcelFileWithExpectedHeadersAndData()
    {
        var model = CreateModel();
        var timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        model.TempData["MappedRowsJson"] = JsonSerializer.Serialize(new List<AuditTrailInfo>
        {
            new AuditTrailInfo { Timestamp = timestamp, UserID = "jsmith", OldValue = "old", NewValue = "new", Reason = "correction" }
        });

        var result = await model.OnPostExportExcelAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);
        StringAssert.StartsWith(fileResult.FileDownloadName, "AuditTrail_");
        StringAssert.EndsWith(fileResult.FileDownloadName, ".xlsx");

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("Audit Trail");
        Assert.AreEqual("DateTime", ws.Cell(1, 1).GetString());
        Assert.AreEqual("User name", ws.Cell(1, 2).GetString());
        Assert.AreEqual("jsmith", ws.Cell(2, 2).GetString());
        Assert.AreEqual("old", ws.Cell(2, 3).GetString());
        Assert.AreEqual("new", ws.Cell(2, 4).GetString());
        Assert.AreEqual("correction", ws.Cell(2, 5).GetString());
    }

    [TestMethod]
    public async Task OnPostExportExcel_UserIdLooksLikePersonalName_FlagsFinding()
    {
        var model = CreateModel();
        model.TempData["MappedRowsJson"] = JsonSerializer.Serialize(new List<AuditTrailInfo>
        {
            new AuditTrailInfo { UserID = "Adrian" }
        });

        var result = await model.OnPostExportExcelAsync();

        var fileResult = result as FileContentResult;
        using var stream = new MemoryStream(fileResult!.FileContents);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("Audit Trail");
        Assert.AreEqual("Name should be not login", ws.Cell(2, 6).GetString());
    }

    [TestMethod]
    public async Task OnPostExportExcel_UserIdNotAName_NoFinding()
    {
        var model = CreateModel();
        model.TempData["MappedRowsJson"] = JsonSerializer.Serialize(new List<AuditTrailInfo>
        {
            new AuditTrailInfo { UserID = "svc-integration-account" }
        });

        var result = await model.OnPostExportExcelAsync();

        var fileResult = result as FileContentResult;
        using var stream = new MemoryStream(fileResult!.FileContents);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("Audit Trail");
        Assert.AreEqual(string.Empty, ws.Cell(2, 6).GetString());
    }
}
