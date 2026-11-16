using Serilog.Context;

namespace AuditTrailReview.WebApp.Security
{
    /// <summary>
    /// Tags every log event written while a request runs with
    /// <c>CorrelationId = HttpContext.TraceIdentifier</c>. That is the same "Reference" the Error
    /// page shows the user (Pages/Error.cshtml), so support can find the matching rows in
    /// <c>dbo.ApplicationLog</c> by it.
    ///
    /// Registered in Program.cs before <c>UseSerilogRequestLogging</c> and the exception handler, so
    /// the request log line and the "unhandled exception" event are both covered. Code deeper in the
    /// request that pushes its own <c>CorrelationId</c> (a scan upload uses the scan id) overrides
    /// this value for its own events, which keeps the existing "find everything about scan N" lookup
    /// working.
    /// </summary>
    public sealed class RequestCorrelationMiddleware
    {
        public const string PropertyName = "CorrelationId";

        private readonly RequestDelegate _next;

        public RequestCorrelationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            using (LogContext.PushProperty(PropertyName, context.TraceIdentifier))
            {
                await _next(context);
            }
        }
    }
}
