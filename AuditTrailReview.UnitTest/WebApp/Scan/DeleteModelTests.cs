using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.Scan;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Scan;

/// <summary>
/// Unit tests for <see cref="DeleteModel"/> (Pages/Scan/Delete.cshtml.cs).
///
/// Unlike every other PageModel test in this project, this one runs against a real SQLite
/// in-memory database (<see cref="SqliteTestDatabase"/>) rather than EF Core's InMemory
/// provider: OnPostAsync uses `ExecuteDeleteAsync` for its bulk cleanup of ScanResult/
/// ScanResultAI rows, and the InMemory provider does not implement `ExecuteDelete`/
/// `ExecuteDeleteAsync` at all (it throws InvalidOperationException) -- this is a real gap in
/// that provider, not a bug in DeleteModel, which works fine against the app's actual SQL
/// Server database. SQLite is a real relational provider that does support ExecuteDelete, so
/// it exercises the handler's actual code path rather than requiring a workaround in the test.
/// </summary>
[TestClass]
public class DeleteModelTests
{
    /// <summary>
    /// A SQLite ":memory:" database only exists for as long as its connection stays open, so
    /// this bundles the connection and the DbContext built on top of it into one disposable so
    /// each test gets its own isolated, real relational database.
    /// </summary>
    private sealed class SqliteTestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Context { get; }

        public SqliteTestDatabase()
        {
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;
            Context = new ApplicationDbContext(options);
            Context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }

    private static DeleteModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new DeleteModel(ctx);
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    /// <summary>
    /// Seeds a Scan row. Unlike the production schema (raw SQL scripts, not EF migrations --
    /// see ADR-0004), which never puts a foreign key constraint on Scan.SystemID, EF Core's own
    /// conventions infer one from ScanInfo's [ForeignKey("SystemID")] navigation property, and
    /// SQLite (a real relational provider, unlike EF Core InMemory) enforces whatever
    /// constraint EnsureCreated() builds from that. So unlike the app's own "SystemID == 0
    /// means unassigned" sentinel (see Pages/Scan/Index.cshtml.cs), a seeded Scan here always
    /// needs a real backing SystemATRInfo row -- one is created automatically when the caller
    /// doesn't supply a systemId, since which system a scan belongs to isn't what any test in
    /// this file is actually verifying.
    /// </summary>
    private static async Task<int> SeedScanAsync(ApplicationDbContext ctx, int? systemId = null)
    {
        if (systemId is null)
        {
            var system = new SystemATRInfo { SystemPrefixID = "SYS-AUTO", SystemName = "Auto-seeded System" };
            ctx.SystemATR.Add(system);
            await ctx.SaveChangesAsync();
            ctx.Entry(system).State = EntityState.Detached;
            systemId = system.Id;
        }

        var scan = new ScanInfo { ATFileName = "audit.csv", SystemID = systemId.Value };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        ctx.Entry(scan).State = EntityState.Detached;
        return scan.Id;
    }

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsNotFound()
    {
        using var db = new SqliteTestDatabase();
        var model = CreateModel(db.Context);

        var result = await model.OnGetAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_UnknownId_ReturnsNotFound()
    {
        using var db = new SqliteTestDatabase();
        var model = CreateModel(db.Context);

        var result = await model.OnGetAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingScan_PopulatesScanWithSystemIncluded()
    {
        using var db = new SqliteTestDatabase();
        var ctx = db.Context;
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, system.Id);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(scanId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(scanId, model.Scan.Id);
        Assert.IsNotNull(model.Scan.System, "Expected the System navigation property to be included.");
        Assert.AreEqual("Test System", model.Scan.System!.SystemName);
    }

    [TestMethod]
    public async Task OnPostAsync_NullId_ReturnsNotFound()
    {
        using var db = new SqliteTestDatabase();
        var model = CreateModel(db.Context);

        var result = await model.OnPostAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownId_RedirectsWithoutDeletingAnything()
    {
        // Documents actual behavior: unlike OnGetAsync, OnPostAsync does not return NotFound
        // for an id that doesn't exist -- the whole delete body is guarded by
        // `if (scan != null)`, so a POST for a nonexistent scan just redirects to Index having
        // done nothing. Same shape of quirk as System/DeleteModel's OnPostAsync (see that
        // test's own note).
        using var db = new SqliteTestDatabase();
        var model = CreateModel(db.Context);

        var result = await model.OnPostAsync(999);

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult even though nothing existed to delete.");
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingScan_DeletesScanAndRelatedResults()
    {
        using var db = new SqliteTestDatabase();
        var ctx = db.Context;
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "Some finding" });
        ctx.ScanResultAI.Add(new ScanResultAIInfo { ScanID = scanId, Insight = "Some insight" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnPostAsync(scanId);

        Assert.AreEqual(0, await ctx.Scan.CountAsync());
        Assert.AreEqual(0, await ctx.ScanResult.CountAsync());
        Assert.AreEqual(0, await ctx.ScanResultAI.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingScan_DoesNotDeleteUnrelatedScanResults()
    {
        using var db = new SqliteTestDatabase();
        var ctx = db.Context;
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = otherScanId, Finding = "Unrelated finding" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnPostAsync(scanId);

        Assert.AreEqual(1, await ctx.Scan.CountAsync());
        Assert.AreEqual(1, await ctx.ScanResult.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingScan_RedirectsToIndex()
    {
        using var db = new SqliteTestDatabase();
        var ctx = db.Context;
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(scanId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
    }
}
