using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType;

/// <summary>Unit tests for <see cref="EditModel"/> (Pages/ConfigPanel/SystemType/Edit.cshtml.cs).</summary>
[TestClass]
public class EditModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static EditModel CreateModel(ApplicationDbContext ctx, string? userName = "reviewer1")
    {
        var model = new EditModel(ctx, new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    private static async Task<int> SeedSystemTypeAsync(ApplicationDbContext ctx, string systemType = "Original")
    {
        var entity = new SystemTypeInfo { SystemType = systemType };
        ctx.SystemType.Add(entity);
        await ctx.SaveChangesAsync();

        // Detach: EditModel.OnPostAsync attaches a *new* SystemTypeInfo instance carrying the
        // same Id (matching a real request, which never has this entity already tracked in a
        // fresh scoped DbContext). See System/EditModelTests.cs's SeedSystemAsync for the same
        // pattern and rationale.
        ctx.Entry(entity).State = EntityState.Detached;

        return entity.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_ExistingId_LoadsSystemType()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_LoadsSystemType));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("Original", model.SystemTypeInfo.SystemType);
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

    // ─── OnPostAsync — invalid state ───────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Changed" };
        model.ModelState.AddModelError("SystemTypeInfo.SystemType", "Required");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        var stillOriginal = await ctx.SystemType.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.AreEqual("Original", stillOriginal.SystemType);
    }

    // ─── OnPostAsync — valid state ─────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidModel_UpdatesSystemTypeInDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_UpdatesSystemTypeInDatabase));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Updated" };

        await model.OnPostAsync();

        var updated = await ctx.SystemType.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.AreEqual("Updated", updated.SystemType);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_RedirectsToIndex));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Updated" };

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_ChangedSystemType_LogsAuditTrailUpdateEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ChangedSystemType_LogsAuditTrailUpdateEntry));
        var id = await SeedSystemTypeAsync(ctx, systemType: "Original");
        var model = CreateModel(ctx, userName: "reviewer1");
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Updated" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count);
        var entry = entries.Single();
        Assert.AreEqual("SystemType", entry.Field);
        Assert.AreEqual("Original", entry.OldValue);
        Assert.AreEqual("Updated", entry.NewValue);
        Assert.AreEqual("Update", entry.Operation);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    [TestMethod]
    public async Task OnPostAsync_NoActualChange_NoAuditEntryLogged()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NoActualChange_NoAuditEntryLogged));
        var id = await SeedSystemTypeAsync(ctx, systemType: "Same");
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Same" };

        await model.OnPostAsync();

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ChangedSystemType_AuditEntryRecordsEditedSystemTypeId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ChangedSystemType_AuditEntryRecordsEditedSystemTypeId));
        var id = await SeedSystemTypeAsync(ctx, systemType: "Original");
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { Id = id, SystemType = "Updated" };

        await model.OnPostAsync();

        var entry = await ctx.AuditTrail.SingleAsync();
        Assert.AreEqual(id, entry.SystemTypeID);
    }
}
