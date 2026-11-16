using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.System;

/// <summary>Unit tests for <see cref="EditModel"/> (Pages/System/Edit.cshtml.cs).</summary>
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

    private static async Task<int> SeedSystemAsync(ApplicationDbContext ctx, string name = "Original", int? typeId = null)
    {
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = name, SystemTypeID = typeId };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();

        // Detach: EditModel.OnPostAsync attaches a *new* SystemATRInfo instance carrying the
        // same Id (that's exactly what a real request does -- the posted model is a fresh
        // object, not the one a previous query loaded). If the entity we just seeded is still
        // tracked in this same context, EF Core throws "cannot be tracked because another
        // instance with the same key value is already being tracked" the moment OnPostAsync
        // calls Attach. A real HTTP request never hits this because each request gets its own
        // scoped DbContext with nothing tracked yet; detaching here reproduces that.
        ctx.Entry(system).State = EntityState.Detached;

        return system.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_ExistingId_LoadsSystemAndTypeList()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_LoadsSystemAndTypeList));
        var id = await SeedSystemAsync(ctx);
        ctx.SystemType.Add(new SystemTypeInfo { Id = 1, SystemType = "LIMS" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("Original", model.SystemATRInfo.SystemName);
        Assert.AreEqual(1, model.SystemTypeList.Count());
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
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Changed" };
        model.ModelState.AddModelError("SystemATRInfo.SystemName", "Required");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        var stillOriginal = await ctx.SystemATR.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.AreEqual("Original", stillOriginal.SystemName);
    }

    // ─── OnPostAsync — valid state ─────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidModel_UpdatesSystemInDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_UpdatesSystemInDatabase));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Updated" };

        await model.OnPostAsync();

        var updated = await ctx.SystemATR.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.AreEqual("Updated", updated.SystemName);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_RedirectsToIndex));
        var id = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Updated" };

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_ChangedName_LogsAuditTrailUpdateEntryForChangedFieldOnly()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ChangedName_LogsAuditTrailUpdateEntryForChangedFieldOnly));
        var id = await SeedSystemAsync(ctx, name: "Original");
        var model = CreateModel(ctx, userName: "reviewer1");
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Updated" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count, "Only SystemName changed -- exactly one entry expected");
        var entry = entries.Single();
        Assert.AreEqual("SystemName", entry.Field);
        Assert.AreEqual("Original", entry.OldValue);
        Assert.AreEqual("Updated", entry.NewValue);
        Assert.AreEqual("Update", entry.Operation);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    [TestMethod]
    public async Task OnPostAsync_NoActualChange_NoAuditEntryLogged()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NoActualChange_NoAuditEntryLogged));
        var id = await SeedSystemAsync(ctx, name: "Same", typeId: null);
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Same" };

        await model.OnPostAsync();

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ChangedName_AuditEntryRecordsEditedSystemId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ChangedName_AuditEntryRecordsEditedSystemId));
        var id = await SeedSystemAsync(ctx, name: "Original");
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { Id = id, SystemPrefixID = "SYS-1", SystemName = "Updated" };

        await model.OnPostAsync();

        var entry = await ctx.AuditTrail.SingleAsync();
        Assert.AreEqual(id, entry.SystemID);
    }
}
