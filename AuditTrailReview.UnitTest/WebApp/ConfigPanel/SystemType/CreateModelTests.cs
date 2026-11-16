using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType;

/// <summary>Unit tests for <see cref="CreateModel"/> (Pages/ConfigPanel/SystemType/Create.cshtml.cs).</summary>
[TestClass]
public class CreateModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CreateModel CreateModel(ApplicationDbContext ctx, string? userName = "reviewer1")
    {
        var model = new CreateModel(ctx, new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    // ─── OnGet ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnGet_ReturnsPage()
    {
        using var ctx = CreateContext(nameof(OnGet_ReturnsPage));
        var model = CreateModel(ctx);

        var result = model.OnGet();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
    }

    // ─── OnPostAsync — invalid state ───────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving));
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };
        model.ModelState.AddModelError("SystemTypeInfo.SystemType", "Required");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, await ctx.SystemType.CountAsync());
    }

    // ─── OnPostAsync — valid state ─────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidModel_SavesSystemTypeToDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_SavesSystemTypeToDatabase));
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };

        await model.OnPostAsync();

        var saved = await ctx.SystemType.SingleAsync();
        Assert.AreEqual("LIMS", saved.SystemType);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_RedirectsToIndex));
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect);
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_LogsAuditTrailCreateEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_LogsAuditTrailCreateEntry));
        var model = CreateModel(ctx, userName: "reviewer1");
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count, "Only SystemType is audited -- Id carries [Key] and is excluded.");
        var entry = entries.Single();
        Assert.AreEqual("SystemType", entry.Field);
        Assert.AreEqual("LIMS", entry.NewValue);
        Assert.AreEqual("Create", entry.Operation);
        Assert.AreEqual("ConfigPanel", entry.ObjectType);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    [TestMethod]
    public async Task OnPostAsync_NoUserOnContext_AuditEntryHasNullUserId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NoUserOnContext_AuditEntryHasNullUserId));
        var model = CreateModel(ctx, userName: null);
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.UserID == null));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemTypeId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemTypeId));
        var model = CreateModel(ctx);
        model.SystemTypeInfo = new SystemTypeInfo { SystemType = "LIMS" };

        await model.OnPostAsync();

        var saved = await ctx.SystemType.SingleAsync();
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.SystemTypeID == saved.Id),
            "Every audit entry for a System Type create must carry the new System Type's Id");
    }
}
