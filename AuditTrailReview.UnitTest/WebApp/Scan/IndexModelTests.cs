using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.Scan;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Scan;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> (Pages/Scan/Index.cshtml.cs), including the
/// NFR-018 server-side paging it added (10-per-page window, clamped page number, and the
/// <c>Pagination.BuildPageWindow</c> ellipsis-gap page list), the "assign system" handler,
/// and the server-side <c>SearchTerm</c> search (System name/prefix, file name, file type,
/// and date) that replaced the old client-side, current-page-only JS filter on this page.
/// </summary>
[TestClass]
public class IndexModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IndexModel CreateModel(ApplicationDbContext ctx, int pageNumber = 1, string? searchTerm = null)
    {
        var model = new IndexModel(ctx) { PageNumber = pageNumber, SearchTerm = searchTerm };
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    private static async Task SeedScansAsync(ApplicationDbContext ctx, int count, int? systemId = null)
    {
        for (var i = 0; i < count; i++)
        {
            ctx.Scan.Add(new ScanInfo
            {
                ATFileName = $"scan-{i}.csv",
                ScanDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                SystemID = systemId ?? 0
            });
        }
        await ctx.SaveChangesAsync();
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_NoScans_ReturnsEmptyListAndOnePage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NoScans_ReturnsEmptyListAndOnePage));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Scan.Count);
        Assert.AreEqual(0, model.TotalCount);
        Assert.AreEqual(1, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesScanListOrderedByScanDateDescending()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesScanListOrderedByScanDateDescending));
        await SeedScansAsync(ctx, 3);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(3, model.Scan.Count);
        Assert.AreEqual("scan-2.csv", model.Scan[0].ATFileName, "Expected the most recent scan first.");
        Assert.AreEqual("scan-0.csv", model.Scan[2].ATFileName, "Expected the oldest scan last.");
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesSystemsListAndAttachesSystemNavigationProperty()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesSystemsListAndAttachesSystemNavigationProperty));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        await SeedScansAsync(ctx, 1, systemId: system.Id);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Systems.Count);
        Assert.IsNotNull(model.Scan[0].System, "Expected the System navigation property to be attached.");
        Assert.AreEqual("Test System", model.Scan[0].System!.SystemName);
    }

    [TestMethod]
    public async Task OnGetAsync_MoreScansThanPageSize_ReturnsOnlyOnePageOfScans()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_MoreScansThanPageSize_ReturnsOnlyOnePageOfScans));
        await SeedScansAsync(ctx, IndexModel.PageSize + 5);
        var model = CreateModel(ctx, pageNumber: 1);

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize, model.Scan.Count);
        Assert.AreEqual(IndexModel.PageSize + 5, model.TotalCount);
        Assert.AreEqual(2, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondPage_ReturnsRemainingScans()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SecondPage_ReturnsRemainingScans));
        await SeedScansAsync(ctx, IndexModel.PageSize + 5);
        var model = CreateModel(ctx, pageNumber: 2);

        await model.OnGetAsync();

        Assert.AreEqual(5, model.Scan.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage));
        await SeedScansAsync(ctx, 3);
        var model = CreateModel(ctx, pageNumber: 999);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberLessThanOne_ClampsToOne()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PageNumberLessThanOne_ClampsToOne));
        await SeedScansAsync(ctx, 3);
        var model = CreateModel(ctx, pageNumber: 0);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesPageNumbersToDisplayIncludingFirstPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesPageNumbersToDisplayIncludingFirstPage));
        await SeedScansAsync(ctx, IndexModel.PageSize + 5);
        var model = CreateModel(ctx, pageNumber: 1);

        await model.OnGetAsync();

        CollectionAssert.Contains(model.PageNumbersToDisplay.ToList(), (int?)1);
    }

    // ─── OnGetAsync search (server-side, spans all pages) ─────────────────

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemName_FiltersAcrossAllScansNotJustCurrentPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemName_FiltersAcrossAllScansNotJustCurrentPage));
        var matching = new SystemATRInfo { SystemPrefixID = "SYS-A", SystemName = "Laboratory LIMS" };
        var other = new SystemATRInfo { SystemPrefixID = "SYS-B", SystemName = "Finance ERP" };
        ctx.SystemATR.AddRange(matching, other);
        await ctx.SaveChangesAsync();
        await SeedScansAsync(ctx, IndexModel.PageSize + 5, systemId: other.Id);
        ctx.Scan.Add(new ScanInfo { ATFileName = "lims-scan.csv", ScanDate = new DateTime(2026, 2, 1), SystemID = matching.Id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, searchTerm: "Laboratory");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount, "Expected the search to match the one scan on the LIMS system, regardless of how many other scans exist.");
        Assert.AreEqual(1, model.Scan.Count);
        Assert.AreEqual("lims-scan.csv", model.Scan[0].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemPrefix_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemPrefix_FiltersResults));
        var system = new SystemATRInfo { SystemPrefixID = "QMS-42", SystemName = "Quality Management" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        await SeedScansAsync(ctx, 1, systemId: system.Id);
        await SeedScansAsync(ctx, 1);
        var model = CreateModel(ctx, searchTerm: "QMS-42");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesFileName_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesFileName_FiltersResults));
        ctx.Scan.Add(new ScanInfo { ATFileName = "special-export.csv", ScanDate = new DateTime(2026, 1, 1) });
        ctx.Scan.Add(new ScanInfo { ATFileName = "ordinary.csv", ScanDate = new DateTime(2026, 1, 2) });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, searchTerm: "special-export");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("special-export.csv", model.Scan[0].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesFileType_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesFileType_FiltersResults));
        ctx.Scan.Add(new ScanInfo { ATFileName = "a.xml", ATFileType = ATFileType.xml, ScanDate = new DateTime(2026, 1, 1) });
        ctx.Scan.Add(new ScanInfo { ATFileName = "b.csv", ATFileType = ATFileType.csv, ScanDate = new DateTime(2026, 1, 2) });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, searchTerm: "xml");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("a.xml", model.Scan[0].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesFourDigitYear_FiltersByYear()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesFourDigitYear_FiltersByYear));
        ctx.Scan.Add(new ScanInfo { ATFileName = "in-2025.csv", ScanDate = new DateTime(2025, 6, 15) });
        ctx.Scan.Add(new ScanInfo { ATFileName = "in-2026.csv", ScanDate = new DateTime(2026, 6, 15) });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, searchTerm: "2025");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("in-2025.csv", model.Scan[0].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesExactDate_FiltersToThatDayOnly()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesExactDate_FiltersToThatDayOnly));
        ctx.Scan.Add(new ScanInfo { ATFileName = "day-one.csv", ScanDate = new DateTime(2026, 3, 10, 9, 0, 0) });
        ctx.Scan.Add(new ScanInfo { ATFileName = "day-two.csv", ScanDate = new DateTime(2026, 3, 11, 9, 0, 0) });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, searchTerm: "2026-03-10");

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("day-one.csv", model.Scan[0].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyListNotAllScans()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyListNotAllScans));
        await SeedScansAsync(ctx, 3);
        var model = CreateModel(ctx, searchTerm: "no-such-scan-exists");

        await model.OnGetAsync();

        Assert.AreEqual(0, model.TotalCount);
        Assert.AreEqual(0, model.Scan.Count);
        Assert.AreEqual(1, model.TotalPages, "Zero matches should still report one (empty) page.");
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermIsWhitespace_ReturnsAllScansUnfiltered()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermIsWhitespace_ReturnsAllScansUnfiltered));
        await SeedScansAsync(ctx, 3);
        var model = CreateModel(ctx, searchTerm: "   ");

        await model.OnGetAsync();

        Assert.AreEqual(3, model.TotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermSet_ResetPagingStillHonorsExplicitPageNumber()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermSet_ResetPagingStillHonorsExplicitPageNumber));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-M", SystemName = "Matching System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        await SeedScansAsync(ctx, IndexModel.PageSize + 5, systemId: system.Id);
        var model = CreateModel(ctx, pageNumber: 2, searchTerm: "Matching");

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize + 5, model.TotalCount);
        Assert.AreEqual(5, model.Scan.Count, "Expected the second (partial) page of the filtered result set.");
    }

    [TestMethod]
    public async Task OnGetAsync_SystemIdZero_NeverShowsASystemEvenIfSearching()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SystemIdZero_NeverShowsASystemEvenIfSearching));
        await SeedScansAsync(ctx, 1, systemId: 0);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsNull(model.Scan[0].System, "SystemID 0 is the 'no system assigned' sentinel and should never resolve to a System.");
    }

    // ─── OnPostAssignSystemAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAssignSystemAsync_UnknownScanId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_UnknownScanId_ReturnsNotFound));
        var model = CreateModel(ctx);
        model.AssignScanId = 999;

        var result = await model.OnPostAssignSystemAsync();

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_ExistingScan_UpdatesSystemId()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_ExistingScan_UpdatesSystemId));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        var scan = new ScanInfo { ATFileName = "audit.csv" };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AssignScanId = scan.Id;
        model.AssignSystemId = system.Id;

        await model.OnPostAssignSystemAsync();

        var updated = await ctx.Scan.FindAsync(scan.Id);
        Assert.AreEqual(system.Id, updated!.SystemID);
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_ExistingScan_SetsUserIdToAssigningUser()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_ExistingScan_SetsUserIdToAssigningUser));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        var scan = new ScanInfo { ATFileName = "audit.csv" };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = new IndexModel(ctx) { AssignScanId = scan.Id, AssignSystemId = system.Id };
        PageModelTestHelpers.InitializePageContext(model, userName: "assigner", userId: "user-assigner");

        await model.OnPostAssignSystemAsync();

        var updated = await ctx.Scan.FindAsync(scan.Id);
        Assert.AreEqual("user-assigner", updated!.UserId);
        Assert.AreEqual(system.Id, updated.SystemID);
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_ScanUploadedByAnotherUser_OverwritesUserId()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_ScanUploadedByAnotherUser_OverwritesUserId));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        var scan = new ScanInfo { ATFileName = "audit.csv", UserId = "original-uploader" };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = new IndexModel(ctx) { AssignScanId = scan.Id, AssignSystemId = system.Id };
        PageModelTestHelpers.InitializePageContext(model, userName: "assigner", userId: "user-assigner");

        await model.OnPostAssignSystemAsync();

        var updated = await ctx.Scan.FindAsync(scan.Id);
        Assert.AreEqual("user-assigner", updated!.UserId);
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_PreservesPageNumberOnRedirect()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_PreservesPageNumberOnRedirect));
        var scan = new ScanInfo { ATFileName = "audit.csv" };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, pageNumber: 3);
        model.AssignScanId = scan.Id;
        model.AssignSystemId = 0;

        var result = await model.OnPostAssignSystemAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual(3, redirect!.RouteValues!["PageNumber"]);
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_PreservesSearchTermOnRedirect()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_PreservesSearchTermOnRedirect));
        var scan = new ScanInfo { ATFileName = "audit.csv" };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, pageNumber: 2, searchTerm: "audit");
        model.AssignScanId = scan.Id;
        model.AssignSystemId = 0;

        var result = await model.OnPostAssignSystemAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("audit", redirect!.RouteValues!["SearchTerm"]);
    }

    // ─── GetEnumDisplayName ─────────────────────────────────────────────────

    private enum NoDisplayAttributeEnum { Foo }

    [TestMethod]
    public void GetEnumDisplayName_EnumValueHasDisplayAttribute_ReturnsDisplayName()
    {
        var name = IndexModel.GetEnumDisplayName(ATFileType.csv);

        Assert.AreEqual("CSV", name);
    }

    [TestMethod]
    public void GetEnumDisplayName_EnumValueHasNoDisplayAttribute_FallsBackToToString()
    {
        var name = IndexModel.GetEnumDisplayName(NoDisplayAttributeEnum.Foo);

        Assert.AreEqual("Foo", name);
    }

    // ─── OnGetExportAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAsync_ReturnsExcelWithAllScansRegardlessOfSearchOrPaging()
    {
        using var ctx = CreateContext(nameof(OnGetExportAsync_ReturnsExcelWithAllScansRegardlessOfSearchOrPaging));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        await SeedScansAsync(ctx, 3, systemId: system.Id);
        var model = CreateModel(ctx, pageNumber: 1, searchTerm: "no-such-scan");

        var result = await model.OnGetExportAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Scans");

        Assert.AreEqual("SYS-1", worksheet.Cell(2, 1).GetString());
        Assert.AreEqual("Test System", worksheet.Cell(2, 2).GetString());
        Assert.AreEqual("scan-2.csv", worksheet.Cell(2, 4).GetString());
        Assert.AreEqual("SYS-1", worksheet.Cell(4, 1).GetString());
    }
}
