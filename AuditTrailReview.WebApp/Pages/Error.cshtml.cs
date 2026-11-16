using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuditTrailReview.WebApp.Pages
{
    // Target of both UseExceptionHandler("/Error") (unhandled exception -> 500) and
    // UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}") (an empty 4xx/5xx response,
    // for example the 404 for an unknown URL or for a page that has been switched off) in
    // Program.cs. Must stay reachable even when the failure happens before/during login, so it
    // can't require authentication.
    //
    // The reference shown to the user is HttpContext.TraceIdentifier. Program.cs pushes the same
    // value as the CorrelationId log property for every request, so support can find the matching
    // rows in dbo.ApplicationLog by it. The page never shows exception details.
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; private set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        /// <summary>The HTTP status the page describes (400-599).</summary>
        public int StatusCode { get; private set; } = StatusCodes.Status500InternalServerError;

        public string Heading { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        /// <summary>Bootstrap Icons class for the page's icon.</summary>
        public string IconClass { get; private set; } = string.Empty;

        // A failed form post is re-executed against this page with its original method, so every
        // verb needs a handler. Without one Razor Pages renders the view with an empty model.
        public void OnGet(int? statusCode) => Populate(statusCode);

        public void OnPost(int? statusCode) => Populate(statusCode);

        public void OnPut(int? statusCode) => Populate(statusCode);

        public void OnPatch(int? statusCode) => Populate(statusCode);

        public void OnDelete(int? statusCode) => Populate(statusCode);

        private void Populate(int? statusCode)
        {
            RequestId = HttpContext.TraceIdentifier;

            // The status comes from the re-execute query string when a status-code page asked for
            // this page, otherwise from the response (the exception handler has already set 500).
            // Anything outside 400-599 (for example someone opening /Error directly) is shown as
            // the generic server-error message.
            var code = statusCode ?? Response.StatusCode;
            StatusCode = code is >= 400 and <= 599 ? code : StatusCodes.Status500InternalServerError;

            // A status-code re-execute keeps the original status; make sure the response says so
            // even if the pipeline reset it.
            if (statusCode is >= 400 and <= 599)
            {
                Response.StatusCode = StatusCode;
            }

            (Heading, Message, IconClass) = Describe(StatusCode);
        }

        /// <summary>
        /// The user-facing text for a status code. Deliberately says nothing about the cause of a
        /// server error: no exception text, no environment names.
        /// </summary>
        public static (string Heading, string Message, string IconClass) Describe(int statusCode) =>
            statusCode switch
            {
                StatusCodes.Status400BadRequest => (
                    "Request not understood",
                    "The request could not be processed. Go back and try again.",
                    "bi-exclamation-circle"),
                StatusCodes.Status403Forbidden => (
                    "Access denied",
                    "You don't have permission to open this page. If you think you should, ask an administrator to check your role.",
                    "bi-shield-lock"),
                StatusCodes.Status404NotFound => (
                    "Page not found",
                    "The page you asked for doesn't exist or has been moved. Check the address, or go back and use the menu.",
                    "bi-search"),
                StatusCodes.Status413PayloadTooLarge => (
                    "File too large",
                    "The upload is larger than the system accepts (750 MB). Split the file or contact an administrator.",
                    "bi-file-earmark-x"),
                _ when statusCode >= 500 => (
                    "Something went wrong",
                    "The application hit an unexpected problem and couldn't finish your request. Check the page you were working on before repeating the action, and if the problem keeps happening contact support with the reference below.",
                    "bi-exclamation-triangle"),
                _ => (
                    "Request could not be completed",
                    "The request could not be completed. Go back and try again.",
                    "bi-exclamation-circle"),
            };
    }
}
