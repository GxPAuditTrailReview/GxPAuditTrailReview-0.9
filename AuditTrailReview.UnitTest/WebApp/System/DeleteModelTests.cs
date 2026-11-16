using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.System;

/// <summary>Unit tests for <see cref="DeleteModel"/> (Pages/System/Delete.cshtml.cs).</summary>
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

    private static async Task<int> SeedSystemAsync(ApplicationDbContext ctx, int? typeId = null)
    {
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = typeId };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        return system.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_ExistingId_LoadsSystemAndType()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_LoadsSystemAndType));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 3, SystemType = "LIMS" });
        await ctx.SaveChangesAsync();
        var id = await SeedSystemAsync(ctx, typeId: 3);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("Test System", model.SystemATRInfo.SystemName);
        Assert.IsNotNull(model.SystemTypeInfo);
        Assert.AreEqual("LIMS", model.SystemTypeInfo!.SystemType);
    }

    [TestMethod]
    public async Task OnGetAsync_SystemWithNoType_TypeInfoRemainsNull()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SystemWithNoType_TypeInfoRemainsNull));
        var id = await SeedSystemAsync(ctx, typeId: null);
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.IsNull(model.SystemTypeInfo);
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
    public async Task OnPostAsync_ExistingId_DeletesFromDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ExistingId_DeletesFromDatabase));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnPostAsync(id);

        Assert.AreEqual(0, await ctx.SystemATR.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingId_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ExistingId_RedirectsToIndex));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(id);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingId_LogsAuditTrailDeleteEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ExistingId_LogsAuditTrailDeleteEntry));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx, userName: "reviewer1");

        await model.OnPostAsync(id);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.Operation == "Delete"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "System"));
        Assert.IsTrue(entries.All(e => e.UserID == "reviewer1"));
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingId_AuditEntryRecordsDeletedSystemId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ExistingId_AuditEntryRecordsDeletedSystemId));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnPostAsync(id);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.SystemID == id),
            "Every audit entry for a System delete must carry the deleted system's Id");
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
        // Documents the handler's actual current behaviour: unlike OnGetAsync, a missing id
        // on POST does not return NotFound -- it silently redirects to Index without deleting
        // or auditing anything, since `systemATRInfo != null` guards the whole body.
        await using var ctx = CreateContext(nameof(OnPostAsync_NonExistentId_RedirectsWithoutDeletingOrLogging));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(999);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }
}
