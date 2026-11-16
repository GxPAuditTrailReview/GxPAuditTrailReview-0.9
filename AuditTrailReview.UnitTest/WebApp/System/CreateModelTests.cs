using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.System;

/// <summary>
/// Unit tests for <see cref="CreateModel"/> (Pages/System/Create.cshtml.cs).
/// Uses EF Core InMemory (matching Engine/LogServiceTests.cs) and the real
/// <see cref="AuditTrailService"/> rather than a mock, since this project has no mocking
/// library and the rest of the suite prefers real objects over test doubles.
/// </summary>
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

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_PopulatesSystemTypeListFromDatabase()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesSystemTypeListFromDatabase));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 1, SystemType = "LIMS" });
        ctx.SystemType.Add(new SystemTypeInfo { Id = 2, SystemType = "ERP" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(2, model.SystemTypeList.Count());
    }

    [TestMethod]
    public async Task OnGetAsync_NoSystemTypes_ReturnsEmptyList()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NoSystemTypes_ReturnsEmptyList));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.SystemTypeList.Count());
    }

    // ─── OnPostAsync — invalid state ───────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_InvalidModelState_ReturnsPageWithoutSaving));
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        model.ModelState.AddModelError("SystemATRInfo.SystemName", "Required");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, await ctx.SystemATR.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_InvalidModelState_RepopulatesSystemTypeList()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_InvalidModelState_RepopulatesSystemTypeList));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 1, SystemType = "LIMS" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo();
        model.ModelState.AddModelError("SystemATRInfo.SystemName", "Required");

        await model.OnPostAsync();

        Assert.AreEqual(1, model.SystemTypeList.Count());
    }

    // ─── OnPostAsync — valid state ─────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidModel_SavesSystemToDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_SavesSystemToDatabase));
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };

        await model.OnPostAsync();

        var saved = await ctx.SystemATR.SingleAsync();
        Assert.AreEqual("SYS-1", saved.SystemPrefixID);
        Assert.AreEqual("Test System", saved.SystemName);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_RedirectsToIndex));
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };

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
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "System"));
        Assert.IsTrue(entries.All(e => e.UserID == "reviewer1"));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_AuditEntryRecordsSystemTypeDisplayName()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_AuditEntryRecordsSystemTypeDisplayName));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 7, SystemType = "LIMS" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = 7 };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        var typeEntry = entries.SingleOrDefault(e => e.Field == "SystemType");
        Assert.IsNotNull(typeEntry, "Expected a SystemType audit field mapped to its display name");
        Assert.AreEqual("LIMS", typeEntry!.NewValue);
    }

    [TestMethod]
    public async Task OnPostAsync_NoUserOnContext_AuditEntryHasNullUserId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_NoUserOnContext_AuditEntryHasNullUserId));
        var model = CreateModel(ctx, userName: null);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.UserID == null));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemId()
    {
        await using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemId));
        var model = CreateModel(ctx);
        model.SystemATRInfo = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };

        await model.OnPostAsync();

        var saved = await ctx.SystemATR.SingleAsync();
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.SystemID == saved.Id),
            "Every audit entry for a System create must carry the new system's Id");
    }
}
