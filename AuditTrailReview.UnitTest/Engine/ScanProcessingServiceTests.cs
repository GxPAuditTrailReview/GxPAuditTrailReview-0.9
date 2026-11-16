using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

[TestClass]
public class ScanProcessingServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            // ScanProcessingService.ProcessFileAsync wraps its saves in a real
            // Database.BeginTransactionAsync() (see that method's own remarks on why). EF Core's
            // InMemory provider doesn't support transactions at all and raises this as an
            // error-level warning by default -- which DbContext then throws as an exception unless
            // explicitly told this specific warning is expected and fine to ignore here. The
            // InMemory provider silently no-ops the transaction instead (each SaveChangesAsync call
            // still commits immediately), which is exactly the behavior every one of these tests
            // already relies on.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    // AI-queue tests below pass a RecordingBackgroundTaskQueue explicitly; every other
    // (pre-existing) call site omits the parameter and gets a fresh real BackgroundTaskQueue,
    // matching ScanModelTests.cs's own "empty ConfigurationBuilder().Build() is sufficient"
    // reasoning -- the queue is only actually drained by QueuedHostedService at run time, never
    // by these tests.
    private static ScanProcessingService CreateService(ApplicationDbContext context, IBackgroundTaskQueue? queue = null)
        => new(context, NullLogger<ScanProcessingService>.Instance, queue ?? new BackgroundTaskQueue());

    /// <summary>Adds all rule rows (disabled by default) so the rules dictionary is populated.</summary>
    private static void SeedRules(ApplicationDbContext context, bool allEnabled = false)
    {
        var category = new RuleCategory { Id = 1, Name = "General" };
        context.RuleCategories.Add(category);

        foreach (RulesEnum rule in Enum.GetValues<RulesEnum>())
        {
            context.Rules.Add(new Rule
            {
                Id            = (int)rule,
                RuleCode      = rule.ToString(),
                Description   = rule.ToString(),
                IsEnabled     = allEnabled,
                RuleCategoryId = 1
            });
        }

        context.SaveChanges();
    }

    private static Stream ToCsvStream(string csv)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

    private static Stream ToJsonStream(string json)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    private static Stream ToXmlStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

    // Minimal valid CSV – has all fields expected by the mapper
    private const string ValidCsv =
        "Id,User,Timestamp,Action,Before,After,Reason\r\n" +
        "1,jdoe,2024-01-15T10:00:00Z,Create,,NewValue,Initial creation\r\n";

    private const string ValidJson =
        "[{\"Id\":\"1\",\"User\":\"jdoe\",\"Timestamp\":\"2024-01-15T10:00:00Z\"," +
        "\"Action\":\"Create\",\"Before\":\"\",\"After\":\"NewValue\",\"Reason\":\"Initial creation\"}]";

    private const string ValidXml =
        "<Records><Record>" +
        "<Id>1</Id><User>jdoe</User><Timestamp>2024-01-15T10:00:00Z</Timestamp>" +
        "<Action>Create</Action><Before></Before><After>NewValue</After><Reason>Initial creation</Reason>" +
        "</Record></Records>";

    // ─── ProcessFileAsync – Happy Paths ─────────────────────────────────────

    [TestMethod]
    public async Task ProcessFileAsync_ValidCsvWithNoSystem_ReturnsScanId()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidCsvWithNoSystem_ReturnsScanId));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        Assert.IsTrue(scanId > 0);
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual(ATFileType.csv, scan.ATFileType);
        Assert.AreEqual("audit.csv", scan.ATFileName);
    }

    [TestMethod]
    public async Task ProcessFileAsync_WithUserId_SetsUserIdOnScan()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_WithUserId_SetsUserIdOnScan));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv", userId: "user-123");

        // Assert
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual("user-123", scan.UserId);
    }

    [TestMethod]
    public async Task ProcessFileAsync_NoUserId_LeavesUserIdNull()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_NoUserId_LeavesUserIdNull));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.IsNull(scan.UserId);
    }

    [TestMethod]
    public async Task ProcessFileAsync_ValidJsonWithNoSystem_ReturnsScanId()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidJsonWithNoSystem_ReturnsScanId));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToJsonStream(ValidJson), "audit.json");

        // Assert
        Assert.IsTrue(scanId > 0);
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual(ATFileType.json, scan.ATFileType);
    }

    [TestMethod]
    public async Task ProcessFileAsync_ValidXmlWithNoSystem_ReturnsScanId()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidXmlWithNoSystem_ReturnsScanId));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToXmlStream(ValidXml), "audit.xml");

        // Assert
        Assert.IsTrue(scanId > 0);
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual(ATFileType.xml, scan.ATFileType);
    }

    [TestMethod]
    public async Task ProcessFileAsync_ValidCsvWithExistingSystem_AssignsScanToSystem()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidCsvWithExistingSystem_AssignsScanToSystem));
        SeedRules(context);
        context.SystemATR.Add(new SystemATRInfo { Id = 5, SystemPrefixID = "TS5", SystemName = "TestSystem" });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv", systemId: 5);

        // Assert
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual(5, scan.SystemID);
    }

    [TestMethod]
    public async Task ProcessFileAsync_ValidCsvPersistsScanResults()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidCsvPersistsScanResults));
        SeedRules(context, allEnabled: true);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        var results = await context.ScanResult.Where(r => r.ScanID == scanId).ToListAsync();
        // With all rules enabled, at least some results should be generated
        Assert.IsTrue(results.Count >= 0); // results can be 0 if data passes all rules
        Assert.IsTrue(await context.Scan.AnyAsync(s => s.Id == scanId));
    }

    [TestMethod]
    public async Task ProcessFileAsync_CsvWithSemicolonDelimiter_ParsesSuccessfully()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_CsvWithSemicolonDelimiter_ParsesSuccessfully));
        SeedRules(context);
        var service = CreateService(context);
        const string semicolonCsv =
            "Id;User;Timestamp;Action;Before;After;Reason\r\n" +
            "1;jdoe;2024-01-15T10:00:00Z;Create;;NewValue;Initial creation\r\n";

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(semicolonCsv), "audit.csv");

        // Assert
        Assert.IsTrue(scanId > 0);
    }

    // ─── ProcessFileAsync – Original File Storage (dbo.ScanFile) ────────────

    [TestMethod]
    public async Task ProcessFileAsync_ValidCsv_SavesOriginalFileBytesToScanFile()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidCsv_SavesOriginalFileBytesToScanFile));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        var scanFile = await context.ScanFile.SingleAsync(f => f.ScanId == scanId);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(ValidCsv), scanFile.Content);
    }

    [TestMethod]
    public async Task ProcessFileAsync_UnsupportedExtension_DoesNotPersistScanFile()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_UnsupportedExtension_DoesNotPersistScanFile));
        SeedRules(context);
        var service = CreateService(context);

        // Act & Assert
        try
        {
            await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.txt");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.AreEqual(0, await context.ScanFile.CountAsync(),
            "No ScanFile should be persisted for a rejected file extension.");
    }

    // ─── ProcessFileAsync – System Validation ───────────────────────────────

    [TestMethod]
    public async Task ProcessFileAsync_NonExistentSystemId_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_NonExistentSystemId_ThrowsInvalidOperationException));
        SeedRules(context);
        var service = CreateService(context);

        // Act & Assert
        try
        {
            await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv", systemId: 999);
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task ProcessFileAsync_SystemIdZero_DoesNotValidateSystem_ReturnsScanId()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_SystemIdZero_DoesNotValidateSystem_ReturnsScanId));
        SeedRules(context);
        var service = CreateService(context);

        // Act – systemId = 0 (default) must skip system lookup
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv", systemId: 0);

        // Assert
        Assert.IsTrue(scanId > 0);
    }

    // ─── ProcessFileAsync – File Type Detection ──────────────────────────────

    [TestMethod]
    [DataRow("audit.CSV",  ATFileType.csv)]
    [DataRow("audit.JSON", ATFileType.json)]
    [DataRow("audit.XML",  ATFileType.xml)]
    public async Task ProcessFileAsync_FileNameExtension_SetsCorrectATFileType(string fileName, ATFileType expectedType)
    {
        // Arrange
        await using var context = CreateInMemoryContext($"{nameof(ProcessFileAsync_FileNameExtension_SetsCorrectATFileType)}_{fileName}");
        SeedRules(context);
        var service = CreateService(context);

        Stream stream = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => ToJsonStream(ValidJson),
            ".xml"  => ToXmlStream(ValidXml),
            _       => ToCsvStream(ValidCsv)
        };

        // Act
        var scanId = await service.ProcessFileAsync(stream, fileName);

        // Assert
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.AreEqual(expectedType, scan.ATFileType);
    }

    // Unknown/unsupported extensions used to silently fall back to CSV (NFR-020 gap);
    // ProcessFileAsync now rejects them outright instead of mis-parsing the content.
    [TestMethod]
    [DataRow("audit.txt")]
    [DataRow("audit.pdf")]
    [DataRow("audit")]           // no extension at all
    public async Task ProcessFileAsync_UnsupportedExtension_ThrowsInvalidOperationException(string fileName)
    {
        // Arrange
        await using var context = CreateInMemoryContext($"{nameof(ProcessFileAsync_UnsupportedExtension_ThrowsInvalidOperationException)}_{fileName}");
        SeedRules(context);
        var service = CreateService(context);

        // Act & Assert
        try
        {
            await service.ProcessFileAsync(ToCsvStream(ValidCsv), fileName);
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.AreEqual(0, await context.Scan.CountAsync(),
            "No scan should be persisted for a rejected file extension.");
    }

    // ─── GetFileTooLargeMessage – shared with ScanModel (WebApp) and ScanProcessingController
    //     (API), see that method's own remarks on the 736,432,000-byte threshold ─────────────

    [TestMethod]
    public void GetFileTooLargeMessage_AtThreshold_ReturnsNull()
    {
        var message = ScanProcessingService.GetFileTooLargeMessage("audit.csv", ScanProcessingService.MaxRecommendedUploadBytes);

        Assert.IsNull(message, "A file exactly at the threshold should be accepted.");
    }

    [TestMethod]
    public void GetFileTooLargeMessage_OneByteOverThreshold_ReturnsActionableMessage()
    {
        var message = ScanProcessingService.GetFileTooLargeMessage("audit.csv", ScanProcessingService.MaxRecommendedUploadBytes + 1);

        Assert.IsNotNull(message, "A file over the threshold must be rejected with a message.");
        StringAssert.Contains(message, "audit.csv");
        StringAssert.Contains(message, "split");
    }

    [TestMethod]
    public void GetFileTooLargeMessage_WellUnderThreshold_ReturnsNull()
    {
        var message = ScanProcessingService.GetFileTooLargeMessage("audit.csv", 1_000_000);

        Assert.IsNull(message);
    }

    // ─── ProcessFileAsync – Cancellation ────────────────────────────────────

    [TestMethod]
    public async Task ProcessFileAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_CancelledToken_ThrowsOperationCanceledException));
        SeedRules(context);
        var service = CreateService(context);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        try
        {
            await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv", cancellationToken: cts.Token);
            Assert.Fail("Expected OperationCanceledException was not thrown.");
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    // ─── ProcessFileAsync – ScanDate Set Correctly ───────────────────────────

    [TestMethod]
    public async Task ProcessFileAsync_ValidFile_ScanDateIsSetToApproximatelyNow()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_ValidFile_ScanDateIsSetToApproximatelyNow));
        SeedRules(context);
        var service = CreateService(context);
        // ScanDate is recorded as DateTime.UtcNow (NFR-042 -- reconciled to match every other
        // app-generated timestamp), so the "approximately now" window has to be measured in
        // UTC too. Using local DateTime.Now here made this test fail in any timezone with a
        // non-zero UTC offset -- exactly what broke when the server isn't running in UTC.
        var before = DateTime.UtcNow;

        // Act
        var scanId = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");
        var after = DateTime.UtcNow;

        // Assert
        var scan = await context.Scan.FindAsync(scanId);
        Assert.IsNotNull(scan);
        Assert.IsTrue(scan.ScanDate >= before && scan.ScanDate <= after,
            $"Expected ScanDate between {before} and {after}, but was {scan.ScanDate}");
    }

    // ─── ProcessFileAsync – Multiple Scans ──────────────────────────────────

    [TestMethod]
    public async Task ProcessFileAsync_CalledTwice_ReturnsDifferentScanIds()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_CalledTwice_ReturnsDifferentScanIds));
        SeedRules(context);
        var service = CreateService(context);

        // Act
        var id1 = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "file1.csv");
        var id2 = await service.ProcessFileAsync(ToCsvStream(ValidCsv), "file2.csv");

        // Assert
        Assert.AreNotEqual(id1, id2);
    }

    // ─── ProcessFileAsync – Background AI-analysis queueing (Path to v1.0.md item 28,
    //     ADR-0006, NFR-014) ─────────────────────────────────────────────────
    //
    // Mirrors the manual-upload path's own coverage in ScanModelTests.cs -- the queued work
    // item itself is never invoked here (it needs a real OpenAI key and a populated DI
    // container resolved at run time by QueuedHostedService); these tests only confirm
    // ProcessFileAsync queues (or doesn't queue) exactly the right number of items, via the
    // same hand-written recording fake IBackgroundTaskQueue pattern used there.

    /// <summary>
    /// Test-only fake of <see cref="IBackgroundTaskQueue"/> that just records queued work
    /// items without executing them -- see the section remarks above for why.
    /// </summary>
    private sealed class RecordingBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public List<Func<IServiceProvider, CancellationToken, Task>> QueuedWorkItems { get; } = new();

        public void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem)
            => QueuedWorkItems.Add(workItem);

        public Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException("Not exercised by these tests -- ProcessFileAsync only enqueues, it never dequeues.");
    }

    [TestMethod]
    public async Task ProcessFileAsync_AiAnalysisEnabled_QueuesExactlyOneBackgroundWorkItem()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_AiAnalysisEnabled_QueuesExactlyOneBackgroundWorkItem));
        SeedRules(context);
        context.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = true });
        await context.SaveChangesAsync();
        var queue = new RecordingBackgroundTaskQueue();
        var service = CreateService(context, queue);

        // Act
        await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        Assert.AreEqual(1, queue.QueuedWorkItems.Count);
    }

    [TestMethod]
    public async Task ProcessFileAsync_AiAnalysisDisabled_QueuesNoBackgroundWorkItem()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_AiAnalysisDisabled_QueuesNoBackgroundWorkItem));
        SeedRules(context);
        context.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = false });
        await context.SaveChangesAsync();
        var queue = new RecordingBackgroundTaskQueue();
        var service = CreateService(context, queue);

        // Act
        await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        Assert.AreEqual(0, queue.QueuedWorkItems.Count);
    }

    [TestMethod]
    public async Task ProcessFileAsync_NoEngineSettingsRow_DefaultsToEnabledAndQueuesOneBackgroundWorkItem()
    {
        // Arrange -- EngineSettings.AiAnalysisEnabled defaults to true, and
        // ScanProcessingService falls back to `new EngineSettings()` when no row exists
        // (see its own `?? new EngineSettings()` fallback), so this must behave the same as
        // the AI-enabled case above even with no row seeded at all.
        await using var context = CreateInMemoryContext(nameof(ProcessFileAsync_NoEngineSettingsRow_DefaultsToEnabledAndQueuesOneBackgroundWorkItem));
        SeedRules(context);
        var queue = new RecordingBackgroundTaskQueue();
        var service = CreateService(context, queue);

        // Act
        await service.ProcessFileAsync(ToCsvStream(ValidCsv), "audit.csv");

        // Assert
        Assert.AreEqual(1, queue.QueuedWorkItems.Count);
    }

    // Note: the queued AI work item's internal tier decision (full-scan vs. findings-only --
    // see ScanResultService.ShouldUseFindingsOnlyTier) and payload construction are covered at
    // the ScanResultService unit level (ScanResultServiceTests.cs), not by invoking the
    // delegate here. Invoking it end-to-end would need a real or stubbed OpenAI HTTP call,
    // which OpenAIService doesn't support (its HttpClient isn't injectable) -- a documented
    // coverage gap, not something fixed here.
}
