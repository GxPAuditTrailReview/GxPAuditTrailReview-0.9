using AuditTrailReview.WebApp.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Pages;

/// <summary>
/// Unit tests for <see cref="ErrorModel"/> (Pages/Error.cshtml.cs), the shared target of the
/// unhandled-exception handler and the status-code re-execute in Program.cs.
/// </summary>
[TestClass]
public class ErrorModelTests
{
    private static (ErrorModel Model, DefaultHttpContext Http) CreateModel(int responseStatus = StatusCodes.Status200OK)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        http.Response.StatusCode = responseStatus;
        var model = new ErrorModel
        {
            PageContext = new PageContext { HttpContext = http },
        };
        return (model, http);
    }

    [TestMethod]
    public void OnGet_Always_ExposesTraceIdentifierAsReference()
    {
        var (model, _) = CreateModel();

        model.OnGet(null);

        Assert.IsTrue(model.ShowRequestId);
        Assert.AreEqual("trace-123", model.RequestId);
    }

    [TestMethod]
    [DataRow(404, "Page not found")]
    [DataRow(403, "Access denied")]
    [DataRow(400, "Request not understood")]
    [DataRow(413, "File too large")]
    [DataRow(500, "Something went wrong")]
    [DataRow(503, "Something went wrong")]
    [DataRow(418, "Request could not be completed")]
    public void OnGet_StatusCodeQuery_ShowsMatchingHeading(int statusCode, string expectedHeading)
    {
        var (model, _) = CreateModel();

        model.OnGet(statusCode);

        Assert.AreEqual(statusCode, model.StatusCode);
        Assert.AreEqual(expectedHeading, model.Heading);
        Assert.IsFalse(string.IsNullOrWhiteSpace(model.Message));
        Assert.IsFalse(string.IsNullOrWhiteSpace(model.IconClass));
    }

    [TestMethod]
    public void OnPost_FailedFormPost_ShowsSameContentAsGet()
    {
        var (model, _) = CreateModel();

        model.OnPost(400);

        Assert.AreEqual(400, model.StatusCode);
        Assert.AreEqual("Request not understood", model.Heading);
        Assert.AreEqual("trace-123", model.RequestId);
    }

    [TestMethod]
    public void OnGet_StatusCodeQuery_SetsResponseStatusToMatch()
    {
        var (model, http) = CreateModel(StatusCodes.Status200OK);

        model.OnGet(404);

        Assert.AreEqual(404, http.Response.StatusCode);
    }

    [TestMethod]
    public void OnGet_NoQueryAndExceptionHandlerStatus_UsesResponseStatus()
    {
        var (model, _) = CreateModel(StatusCodes.Status500InternalServerError);

        model.OnGet(null);

        Assert.AreEqual(500, model.StatusCode);
        Assert.AreEqual("Something went wrong", model.Heading);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(0)]
    [DataRow(200)]
    [DataRow(302)]
    [DataRow(600)]
    [DataRow(-5)]
    public void OnGet_OutOfRangeOrMissingStatus_FallsBackToGenericServerError(int? statusCode)
    {
        var (model, http) = CreateModel(StatusCodes.Status200OK);

        model.OnGet(statusCode);

        Assert.AreEqual(500, model.StatusCode);
        Assert.AreEqual("Something went wrong", model.Heading);
        Assert.AreEqual(200, http.Response.StatusCode, "Opening /Error directly must not turn the response into an error status.");
    }

    [TestMethod]
    public void Describe_ServerError_DoesNotMentionEnvironmentOrExceptions()
    {
        var (_, message, _) = ErrorModel.Describe(500);

        Assert.IsFalse(message.Contains("Development", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(message.Contains("ASPNETCORE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(message.Contains("exception", StringComparison.OrdinalIgnoreCase));
    }
}
