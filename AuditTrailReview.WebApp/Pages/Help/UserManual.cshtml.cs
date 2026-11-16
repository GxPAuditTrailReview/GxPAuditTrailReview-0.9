using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuditTrailReview.WebApp.Pages.Help
{
    /// <summary>
    /// Renders Product/User manual.md as HTML. The file itself is never duplicated here -- see
    /// AuditTrailReview.WebApp.csproj's Content Include with
    /// Link="DocsCorpus\Product\User manual.md", which copies it into this project's own
    /// build/publish output so it's always the same file that ships in the Docs project, kept
    /// in sync automatically rather than by hand.
    /// </summary>
    [Authorize]
    public class UserManualModel : PageModel
    {
        private readonly IWebHostEnvironment _environment;

        public UserManualModel(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public string ManualHtml { get; private set; } = string.Empty;
        public bool ManualMissing { get; private set; }

        public void OnGet()
        {
            // Fully qualified with "global::" deliberately, not plain File/Path and not
            // System.IO.File/System.IO.Path -- two different name collisions are stacked here:
            // (1) this project has a Pages/System folder, which creates a sibling namespace
            // literally named AuditTrailReview.WebApp.Pages.System, so an explicitly qualified
            // "System.IO.File" written here in Pages.Help resolves "System" to that sibling
            // namespace first (C# searches enclosing namespaces before the global one) instead of
            // the real BCL System namespace, failing to compile; and (2) PageModel itself (this
            // class's base) declares an instance method literally called File(...) (used to
            // return a FileResult from a handler), so even the plain unqualified name "File"
            // resolves to that inherited method instead of the System.IO.File type -- inherited
            // members win over namespace-imported types in C#'s lookup order. "global::" sidesteps
            // both by forcing resolution to start at the true root namespace.
            var manualPath = global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, "DocsCorpus", "Product", "User manual.md");

            if (!global::System.IO.File.Exists(manualPath))
            {
                ManualMissing = true;
                return;
            }

            var markdown = global::System.IO.File.ReadAllText(manualPath);

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            ManualHtml = Markdown.ToHtml(markdown, pipeline);
        }
    }
}
