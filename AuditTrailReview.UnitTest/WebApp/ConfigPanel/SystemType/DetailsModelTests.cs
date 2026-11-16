using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType;

/// <summary>Unit tests for <see cref="DetailsModel"/> (Pages/ConfigPanel/SystemType/Details.cshtml.cs).</summary>
[TestClass]
public class DetailsModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DetailsModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new DetailsModel(ctx);
        PageModelTestHelpers.InitializePageContext(model);
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
    public async Task OnGetAsync_ExistingId_LoadsSystemType()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_LoadsSystemType));
        var id = await SeedSystemTypeAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("LIMS", model.SystemTypeInfo.SystemType);
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

    // ─── Audit trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesForThisSystemType_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesForThisSystemType_Included));
        var id = await SeedSystemTypeAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = id, Timestamp = DateTime.UtcNow, Operation = "Update", Field = "SystemType" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("SystemType", model.AuditTrailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromOtherSystemType_NotIncluded()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromOtherSystemType_NotIncluded));
        var id = await SeedSystemTypeAsync(ctx);
        var otherId = await SeedSystemTypeAsync(ctx, systemType: "ERP");
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = otherId, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromRuleEnablement_NotIncluded()
    {
        // Rule enablement changes (RuleBasedReviewEngine.cshtml.cs) log under the same
        // AuditObjectType.ConfigPanel but never set SystemTypeID -- this tab must not pick
        // those up just because they share an ObjectType.
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromRuleEnablement_NotIncluded));
        var id = await SeedSystemTypeAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = null, ObjectType = "ConfigPanel", Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "Rule enablement change" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries));
        var id = await SeedSystemTypeAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = id, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "System type updated" });
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = id, Timestamp = DateTime.UtcNow, Operation = "Create", Reason = "System type created" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSearchTerm = "created";

        await model.OnGetAsync(id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Create", model.AuditTrailEntries[0].Operation);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailPaging_ReturnsRequestedPage));
        var id = await SeedSystemTypeAsync(ctx);
        for (var i = 0; i < DetailsModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { SystemTypeID = id, Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailPage = 2;

        await model.OnGetAsync(id);

        Assert.AreEqual(2, model.AuditTrailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEntries.Count);
    }
}
