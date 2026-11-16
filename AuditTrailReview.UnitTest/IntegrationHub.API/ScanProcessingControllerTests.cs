using AuditTrailReview.Database;
using AuditTrailReview.IntegrationHub.API.Controllers;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.IntegrationHub.API;

[TestClass]
public class ScanProcessingControllerTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────
    // The controller only validates and stores (every upload becomes a Tier.Worker
    // ScanUploadJob row for the Worker Service), so it needs nothing beyond an EF Core
    // InMemory database -- the processing itself is covered by ScanUploadJobProcessorTests
    // and ScanProcessingServiceTests.

    private static ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ScanProcessingController CreateController(ApplicationDbContext context)
    {
        return new ScanProcessingController(context, NullLogger<ScanProcessingController>.Instance)
        {
            Url = new StubUrlHelper()
        };
    }

    private static int SeedSystem(ApplicationDbContext context, string name = "Test System")
    {
        var system = new SystemATRInfo { SystemName = name, SystemPrefixID = "TST" };
        context.SystemATR.Add(system);
        context.SaveChanges();
        return system.Id;
    }

    private static IFormFile CreateFormFile(string content, string fileName)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "file", fileName);
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => "/stub";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => "/stub";
        public string? Link(string? routeName, object? values) => "/stub";
    }

    // Minimal valid CSV -- has all fields expected by the mapper.
    private const string ValidCsv =
        "Id,User,Timestamp,Action,Before,After,Reason\r\n" +
        "1,jdoe,2024-01-15T10:00:00Z,Create,,NewValue,Initial creation\r\n";

    // ─── File presence validation ──────────────────────────────────────────

    [TestMethod]
    public async Task ScanProcessingService_NullFile_ReturnsBadRequest()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_NullFile_ReturnsBadRequest));
        var controller = CreateController(context);

        // Act
        var result = await controller.ScanProcessingService(file: null!, systemId: 0);

        // Assert
        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest, "Expected a BadRequestObjectResult for a null file.");
        Assert.AreEqual("File is required.", badRequest!.Value);
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync());
    }

    [TestMethod]
    public async Task ScanProcessingService_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_EmptyFile_ReturnsBadRequest));
        var controller = CreateController(context);
        var emptyFile = CreateFormFile(string.Empty, "empty.csv");

        // Act
        var result = await controller.ScanProcessingService(emptyFile, systemId: 0);

        // Assert
        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest, "Expected a BadRequestObjectResult for a zero-length file.");
        Assert.AreEqual("File is required.", badRequest!.Value);
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync());
    }

    // ─── Happy path: validate, store, return 202 ───────────────────────────

    [TestMethod]
    public async Task ScanProcessingService_ValidCsvNoSystem_ReturnsAcceptedAndStoresWorkerJob()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_ValidCsvNoSystem_ReturnsAcceptedAndStoresWorkerJob));
        var controller = CreateController(context);
        var file = CreateFormFile(ValidCsv, "audit.csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        Assert.IsNotNull(result as AcceptedResult, "Expected 202 Accepted for a valid CSV upload.");
        var job = await context.ScanUploadJob.SingleAsync();
        Assert.AreEqual(ScanUploadTier.Worker, job.Tier);
        Assert.AreEqual(ScanUploadStatus.Queued, job.Status);
        Assert.AreEqual(ScanUploadSource.Api, job.Source);
        Assert.IsNull(job.UserId);
        Assert.AreEqual("audit.csv", job.FileName);
        Assert.AreEqual(ATFileType.csv, job.ATFileType);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(ValidCsv), job.FileContent);
        Assert.AreEqual(0, await context.Scan.CountAsync(), "The API must not process the file itself.");
    }

    [TestMethod]
    public async Task ScanProcessingService_ValidCsvWithKnownSystem_ReturnsAcceptedAndLinksSystem()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_ValidCsvWithKnownSystem_ReturnsAcceptedAndLinksSystem));
        var systemId = SeedSystem(context);
        var controller = CreateController(context);
        var file = CreateFormFile(ValidCsv, "audit.csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId);

        // Assert
        Assert.IsNotNull(result as AcceptedResult, "Expected 202 Accepted for a valid CSV upload.");
        var job = await context.ScanUploadJob.SingleAsync();
        Assert.AreEqual(systemId, job.SystemId, "Expected the job to carry the seeded system.");
    }

    [TestMethod]
    [DataRow("audit.json", ATFileType.json)]
    [DataRow("audit.XML", ATFileType.xml)]
    public async Task ScanProcessingService_JsonOrXml_StoresMatchingFileType(string fileName, ATFileType expected)
    {
        // Arrange
        await using var context = CreateInMemoryContext($"{nameof(ScanProcessingService_JsonOrXml_StoresMatchingFileType)}-{fileName}");
        var controller = CreateController(context);
        var file = CreateFormFile("{}", fileName);

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        Assert.IsNotNull(result as AcceptedResult);
        Assert.AreEqual(expected, (await context.ScanUploadJob.SingleAsync()).ATFileType);
    }

    [TestMethod]
    public async Task ScanProcessingService_LargeFile_IsStillQueuedForTheWorker()
    {
        // Arrange -- there is no size-based routing any more: a multi-MB file is handled exactly
        // like a tiny one (the controller reads exactly file.Length bytes, so the stream must
        // really contain them; 2 MB keeps the test cheap).
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_LargeFile_IsStillQueuedForTheWorker));
        var controller = CreateController(context);
        var bytes = new byte[2_000_000];
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "big.csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        Assert.IsNotNull(result as AcceptedResult);
        var job = await context.ScanUploadJob.SingleAsync();
        Assert.AreEqual(ScanUploadTier.Worker, job.Tier);
        Assert.AreEqual(bytes.Length, job.FileContent!.Length);
    }

    // ─── Submit-time validation mapped to 400 (messages shared with ScanProcessingService) ──

    [TestMethod]
    public async Task ScanProcessingService_UnknownSystemId_ReturnsBadRequestAndStoresNothing()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_UnknownSystemId_ReturnsBadRequestAndStoresNothing));
        var controller = CreateController(context);
        var file = CreateFormFile(ValidCsv, "audit.csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 999);

        // Assert
        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest, "Expected a BadRequestObjectResult when systemId does not exist.");
        StringAssert.Contains((string)badRequest!.Value!, "999");
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync(), "No job should be stored when the system does not exist.");
    }

    [TestMethod]
    public async Task ScanProcessingService_UnsupportedFileExtension_ReturnsBadRequestAndStoresNothing()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_UnsupportedFileExtension_ReturnsBadRequestAndStoresNothing));
        var controller = CreateController(context);
        var file = CreateFormFile("not a supported format", "audit.txt");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest, "Expected a BadRequestObjectResult for an unsupported file extension.");
        StringAssert.Contains((string)badRequest!.Value!, ".txt");
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync(), "No job should be stored for a rejected file extension.");
    }

    [TestMethod]
    public async Task ScanProcessingService_UnsupportedFileExtensionWithKnownSystem_ReturnsBadRequest()
    {
        // Arrange -- systemId is valid here, so this isolates the extension check.
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_UnsupportedFileExtensionWithKnownSystem_ReturnsBadRequest));
        var systemId = SeedSystem(context);
        var controller = CreateController(context);
        var file = CreateFormFile("not a supported format", "audit.pdf");

        // Act
        var result = await controller.ScanProcessingService(file, systemId);

        // Assert
        Assert.IsNotNull(result as BadRequestObjectResult, "Expected a BadRequestObjectResult for an unsupported file extension.");
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync());
    }

    [TestMethod]
    public async Task ScanProcessingService_FileExceedsMaxRecommendedSize_ReturnsBadRequestAndStoresNothing()
    {
        // Arrange -- FormFile's Length is reported independently of the backing stream's real
        // size, so this doesn't actually allocate ~950 MB (same trick used by
        // ScanProcessingServiceTests' direct GetFileTooLargeMessage coverage).
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_FileExceedsMaxRecommendedSize_ReturnsBadRequestAndStoresNothing));
        var controller = CreateController(context);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidCsv));
        var file = new FormFile(stream, 0, AuditTrailReview.Engine.ScanProcessingService.MaxRecommendedUploadBytes + 1, "file", "audit.csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest, "Expected a BadRequestObjectResult for a file over the recommended upload size.");
        StringAssert.Contains((string)badRequest!.Value!, "split");
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync(), "No job should be stored for an oversized file.");
    }

    [TestMethod]
    public async Task ScanProcessingService_FileNameTooLong_ReturnsBadRequestAndStoresNothing()
    {
        // Arrange -- ScanUploadJob.FileName is a 450-character column.
        await using var context = CreateInMemoryContext(nameof(ScanProcessingService_FileNameTooLong_ReturnsBadRequestAndStoresNothing));
        var controller = CreateController(context);
        var file = CreateFormFile(ValidCsv, new string('a', 447) + ".csv");

        // Act
        var result = await controller.ScanProcessingService(file, systemId: 0);

        // Assert
        Assert.IsNotNull(result as BadRequestObjectResult);
        Assert.AreEqual(0, await context.ScanUploadJob.CountAsync());
    }

    // ─── GetScanStatus ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetScanStatus_UnknownJob_ReturnsNotFound()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(GetScanStatus_UnknownJob_ReturnsNotFound));
        var controller = CreateController(context);

        // Act
        var result = await controller.GetScanStatus(42, CancellationToken.None);

        // Assert
        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task GetScanStatus_CompletedJob_ReturnsStatusAndScanId()
    {
        // Arrange
        await using var context = CreateInMemoryContext(nameof(GetScanStatus_CompletedJob_ReturnsStatusAndScanId));
        var job = new ScanUploadJob
        {
            FileName = "audit.csv",
            Tier = ScanUploadTier.Worker,
            Status = ScanUploadStatus.Completed,
            Source = ScanUploadSource.Api,
            ScanId = 7
        };
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetScanStatus(job.Id, CancellationToken.None);

        // Assert
        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var json = System.Text.Json.JsonSerializer.Serialize(ok!.Value);
        StringAssert.Contains(json, "\"status\":\"Completed\"");
        StringAssert.Contains(json, "\"scanId\":7");
    }
}
