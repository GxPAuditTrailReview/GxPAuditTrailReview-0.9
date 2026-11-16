using System.Text.Json;
using AuditTrailReview.Database;
using AuditTrailReview.McpServer.Tools;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.McpServer
{
    /// <summary>Unit tests for the read-only MCP tools added to <see cref="AuditTrailTools"/>
    /// (get_scan_summary, get_scan_findings, query_scan_records, query_app_audit_trail).</summary>
    [TestClass]
    public class AuditTrailToolsTests
    {
        private static ApplicationDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        private static async Task SeedAsync(ApplicationDbContext ctx)
        {
            ctx.SystemATR.Add(new SystemATRInfo { Id = 1, SystemPrefixID = "S1", SystemName = "Sys" });
            ctx.Scan.Add(new ScanInfo { Id = 1, SystemID = 1, ATFileName = "a.csv", ScanDate = DateTime.UtcNow });
            ctx.ScanResult.AddRange(
                new ScanResultInfo { ScanID = 1, UserID = "bob", Operation = "Update", Timestamp = new DateTime(2026, 1, 1), HasFinding = true, Finding = "F1", ReviewStatus = FindingReviewStatus.Deviation },
                new ScanResultInfo { ScanID = 1, UserID = "bob", Operation = "Create", Timestamp = new DateTime(2026, 1, 2), HasFinding = true, Finding = "F2" },
                new ScanResultInfo { ScanID = 1, UserID = "eve", Operation = "Create", Timestamp = new DateTime(2026, 1, 3) },
                new ScanResultInfo { ScanID = 2, UserID = "eve", Operation = "Create", Timestamp = new DateTime(2026, 1, 4), HasFinding = true });
            ctx.AuditTrail.AddRange(
                new AuditTrailInfo { UserID = "admin", ObjectType = "Scan", ScanID = 1, Timestamp = new DateTime(2026, 2, 1) },
                new AuditTrailInfo { UserID = "admin", ObjectType = "System", Timestamp = new DateTime(2026, 2, 2) });
            await ctx.SaveChangesAsync();
        }

        [TestMethod]
        public async Task GetScanSummary_ExistingScan_CountsRecordsAndFindings()
        {
            await using var ctx = CreateContext(nameof(GetScanSummary_ExistingScan_CountsRecordsAndFindings));
            await SeedAsync(ctx);

            var json = await AuditTrailTools.GetScanSummary(ctx, 1);
            var root = JsonDocument.Parse(json).RootElement;

            Assert.AreEqual(3, root.GetProperty("totalRecords").GetInt32());
            Assert.AreEqual(2, root.GetProperty("findingCount").GetInt32());
        }

        [TestMethod]
        public async Task GetScanSummary_UnknownScan_ReturnsErrorObject()
        {
            await using var ctx = CreateContext(nameof(GetScanSummary_UnknownScan_ReturnsErrorObject));

            var json = await AuditTrailTools.GetScanSummary(ctx, 99);

            Assert.IsTrue(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
        }

        [TestMethod]
        public async Task GetScanFindings_ReviewStatusFilter_ReturnsOnlyMatching()
        {
            await using var ctx = CreateContext(nameof(GetScanFindings_ReviewStatusFilter_ReturnsOnlyMatching));
            await SeedAsync(ctx);

            var json = await AuditTrailTools.GetScanFindings(ctx, 1, reviewStatus: "Deviation");

            Assert.AreEqual(1, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        [TestMethod]
        public async Task GetScanFindings_InvalidReviewStatus_ReturnsErrorObject()
        {
            await using var ctx = CreateContext(nameof(GetScanFindings_InvalidReviewStatus_ReturnsErrorObject));

            var json = await AuditTrailTools.GetScanFindings(ctx, 1, reviewStatus: "bogus");

            Assert.IsTrue(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
        }

        [TestMethod]
        public async Task QueryScanRecords_UserAndFindingsOnly_FiltersWithinScan()
        {
            await using var ctx = CreateContext(nameof(QueryScanRecords_UserAndFindingsOnly_FiltersWithinScan));
            await SeedAsync(ctx);

            var json = await AuditTrailTools.QueryScanRecords(ctx, 1, userId: "bob", findingsOnly: true);

            Assert.AreEqual(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        [TestMethod]
        public async Task QueryScanRecords_TakeAboveCapAndDateRange_FiltersAndDoesNotThrow()
        {
            await using var ctx = CreateContext(nameof(QueryScanRecords_TakeAboveCapAndDateRange_FiltersAndDoesNotThrow));
            await SeedAsync(ctx);

            var json = await AuditTrailTools.QueryScanRecords(ctx, 1, from: new DateTime(2026, 1, 2), take: 100000);

            Assert.AreEqual(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        [TestMethod]
        public async Task QueryAppAuditTrail_ScanIdFilter_ReturnsOnlyThatScansEntries()
        {
            await using var ctx = CreateContext(nameof(QueryAppAuditTrail_ScanIdFilter_ReturnsOnlyThatScansEntries));
            await SeedAsync(ctx);

            var json = await AuditTrailTools.QueryAppAuditTrail(ctx, scanId: 1);

            Assert.AreEqual(1, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        [TestMethod]
        public async Task ListSystems_ReturnsSeededSystemsOrderedById()
        {
            await using var ctx = CreateContext(nameof(ListSystems_ReturnsSeededSystemsOrderedById));
            ctx.SystemATR.AddRange(
                new SystemATRInfo { Id = 2, SystemPrefixID = "B", SystemName = "Second" },
                new SystemATRInfo { Id = 1, SystemPrefixID = "A", SystemName = "First" });
            await ctx.SaveChangesAsync();

            var root = JsonDocument.Parse(await AuditTrailTools.ListSystems(ctx, default)).RootElement;

            Assert.AreEqual(2, root.GetArrayLength());
            Assert.AreEqual("First", root[0].GetProperty("SystemName").GetString());
        }

        [TestMethod]
        public async Task ListScans_SystemFilterAndTake_ReturnsMostRecentFirstWithinFilter()
        {
            await using var ctx = CreateContext(nameof(ListScans_SystemFilterAndTake_ReturnsMostRecentFirstWithinFilter));
            ctx.SystemATR.AddRange(
                new SystemATRInfo { Id = 1, SystemPrefixID = "A", SystemName = "One" },
                new SystemATRInfo { Id = 2, SystemPrefixID = "B", SystemName = "Two" });
            ctx.Scan.AddRange(
                new ScanInfo { Id = 1, SystemID = 1, ATFileName = "old.csv", ScanDate = new DateTime(2026, 1, 1) },
                new ScanInfo { Id = 2, SystemID = 1, ATFileName = "new.csv", ScanDate = new DateTime(2026, 2, 1) },
                new ScanInfo { Id = 3, SystemID = 2, ATFileName = "other.csv", ScanDate = new DateTime(2026, 3, 1) });
            await ctx.SaveChangesAsync();

            var root = JsonDocument.Parse(await AuditTrailTools.ListScans(ctx, systemId: 1, take: 1)).RootElement;

            Assert.AreEqual(1, root.GetArrayLength());
            Assert.AreEqual("new.csv", root[0].GetProperty("ATFileName").GetString());
        }

        /// <summary>The InMemory provider evaluates LINQ in-process, so it can't prove the queries
        /// translate to SQL. SQLite is a real relational provider: it catches untranslatable
        /// expressions (GroupBy on a nullable enum, enum ToString() projections, date-range
        /// comparisons). It is not SQL Server/PostgreSQL, so provider-specific behaviour is still
        /// unverified.</summary>
        [TestMethod]
        public async Task AllTools_OnRelationalProvider_QueriesTranslateAndReturnData()
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            await using var ctx = new ApplicationDbContext(options);
            await ctx.Database.EnsureCreatedAsync();
            await SeedAsync(ctx);

            Assert.AreEqual(1, JsonDocument.Parse(await AuditTrailTools.ListSystems(ctx, default)).RootElement.GetArrayLength());
            Assert.AreEqual(1, JsonDocument.Parse(await AuditTrailTools.ListScans(ctx)).RootElement.GetArrayLength());
            var summary = JsonDocument.Parse(await AuditTrailTools.GetScanSummary(ctx, 1)).RootElement;
            Assert.AreEqual(2, summary.GetProperty("findingCount").GetInt32());
            Assert.AreEqual(1, JsonDocument.Parse(await AuditTrailTools.GetScanFindings(ctx, 1, reviewStatus: "Deviation")).RootElement.GetArrayLength());
            Assert.AreEqual(2, JsonDocument.Parse(await AuditTrailTools.QueryScanRecords(ctx, 1, from: new DateTime(2026, 1, 2), take: 500)).RootElement.GetArrayLength());
            Assert.AreEqual(1, JsonDocument.Parse(await AuditTrailTools.QueryAppAuditTrail(ctx, scanId: 1, from: new DateTime(2026, 1, 1))).RootElement.GetArrayLength());
        }
    }
}
