using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuditTrailReview.WebApp.Pages.Help
{
    /// <summary>
    /// Landing page for new users: a sample audit trail file to upload and try the Scan feature
    /// immediately (see wwwroot/samples), a link to the in-app User Manual (see ./UserManual and
    /// Product/User manual.md), and a placeholder for video walkthroughs once they exist. Linked
    /// from the main nav (see Pages/Shared/_Layout.cshtml).
    /// </summary>
    [Authorize]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
