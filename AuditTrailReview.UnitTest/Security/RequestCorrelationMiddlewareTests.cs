using AuditTrailReview.WebApp.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="RequestCorrelationMiddleware"/>: every log event written while a
/// request runs must carry <c>CorrelationId = TraceIdentifier</c>, the "Reference" the Error page
/// shows, so support can find the rows in dbo.ApplicationLog.
/// </summary>
[TestClass]
public class RequestCorrelationMiddlewareTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (Logger Logger, CollectingSink Sink) CreateLogger()
    {
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        return (logger, sink);
    }

    private static string? CorrelationIdOf(LogEvent e) =>
        e.Properties.TryGetValue(RequestCorrelationMiddleware.PropertyName, out var value)
            ? ((ScalarValue)value).Value?.ToString()
            : null;

    [TestMethod]
    public async Task InvokeAsync_LogInsideRequest_CarriesTraceIdentifierAsCorrelationId()
    {
        var (logger, sink) = CreateLogger();
        var middleware = new RequestCorrelationMiddleware(_ =>
        {
            logger.Warning("inside the request");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext { TraceIdentifier = "trace-abc" });

        Assert.AreEqual(1, sink.Events.Count);
        Assert.AreEqual("trace-abc", CorrelationIdOf(sink.Events[0]));
    }

    [TestMethod]
    public async Task InvokeAsync_LogAfterRequestCompletes_DoesNotCarryCorrelationId()
    {
        var (logger, sink) = CreateLogger();
        var middleware = new RequestCorrelationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(new DefaultHttpContext { TraceIdentifier = "trace-abc" });
        logger.Warning("after the request");

        Assert.IsNull(CorrelationIdOf(sink.Events.Single()));
    }

    [TestMethod]
    public async Task InvokeAsync_InnerCorrelationId_OverridesForItsOwnEventsOnly()
    {
        var (logger, sink) = CreateLogger();
        var middleware = new RequestCorrelationMiddleware(_ =>
        {
            using (LogContext.PushProperty(RequestCorrelationMiddleware.PropertyName, "42"))
            {
                logger.Warning("scan work");
            }
            logger.Warning("rest of the request");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext { TraceIdentifier = "trace-abc" });

        Assert.AreEqual("42", CorrelationIdOf(sink.Events[0]), "A scan upload's own CorrelationId (the scan id) must win for its events.");
        Assert.AreEqual("trace-abc", CorrelationIdOf(sink.Events[1]));
    }

    [TestMethod]
    public async Task InvokeAsync_NextThrows_ExceptionPropagatesAndScopeIsReleased()
    {
        var (logger, sink) = CreateLogger();
        var middleware = new RequestCorrelationMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(new DefaultHttpContext { TraceIdentifier = "trace-abc" }));
        logger.Warning("after the failed request");

        Assert.IsNull(CorrelationIdOf(sink.Events.Single()));
    }
}
