using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType;

/// <summary>Unit tests for <see cref="DeleteModel"/> (Pages/ConfigPanel/SystemType/Delete.cshtml.cs).</summary>
[TestClass]
public class DeleteModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DeleteModel CreateModel(ApplicationDbContext ctx, string? userName = "reviewer1")
    {
        var model = new DeleteModel(ctx, new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    private static async Task<int> SeedSystemTypeAsync(ApplicationDbContext ctx, string systemType = "LIMS")
    {
        var entity = new SystemTypeInfo { SystemType = systemType };
        ctx.SystemType.Add(entity);
        await ctx.SaveChangesAsync();
        return entity.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_ExistingIdNotInUse_LoadsSystemTypeWithIsInUseFalse()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingIdNotInUse_LoadsSystemTypeWithIsInUseFalse));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("LIMS", model.SystemTypeInfo.SystemType);
        Assert.IsFalse(model.IsInUse);
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingIdInUse_IsInUseTrue()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingIdInUse_IsInUseTrue));
        var id = await SeedSystemTypeAsync(ctx);
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.IsTrue(model.IsInUse);
    }

    [TestMethod]
    public async Task OnGetAsync_NonExistentId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NonExistentId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    // ─── OnPostAsync ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_NotInUse_DeletesFromDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NotInUse_DeletesFromDatabase));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnPostAsync(id);

        Assert.AreEqual(0, await ctx.SystemType.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_NotInUse_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NotInUse_RedirectsToIndex));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(id);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_NotInUse_LogsAuditTrailDeleteEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NotInUse_LogsAuditTrailDeleteEntry));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx, userName: "reviewer1");

        await model.OnPostAsync(id);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count);
        var entry = entries.Single();
        Assert.AreEqual("Delete", entry.Operation);
        Assert.AreEqual("ConfigPanel", entry.ObjectType);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    [TestMethod]
    public async Task OnPostAsync_NotInUse_AuditEntryRecordsDeletedSystemTypeId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NotInUse_AuditEntryRecordsDeletedSystemTypeId));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnPostAsync(id);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.SystemTypeID == id),
            "Every audit entry for a System Type delete must carry the deleted System Type's Id");
    }

    [TestMethod]
    public async Task OnPostAsync_InUse_RedirectsWithoutDeletingOrLogging()
    {
        // Documents the handler's guard: a system type still referenced by at least one
        // SystemATR row is never deleted, even if the caller bypasses the confirmation page's
        // own "can't delete" messaging and posts directly.
        await using var ctx = CreateContext(nameof(OnPostAsync_InUse_RedirectsWithoutDeletingOrLogging));
        var id = await SeedSystemTypeAsync(ctx);
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(id);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
        Assert.AreEqual(1, await ctx.SystemType.CountAsync());
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_NonExistentId_RedirectsWithoutDeletingOrLogging()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NonExistentId_RedirectsWithoutDeletingOrLogging));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(999);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }
}
