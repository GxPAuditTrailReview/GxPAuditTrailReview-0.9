using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuditTrailReview.WebApp.Pages
{
    // Must stay anonymous -- this is the site root, hit before anyone has signed in.
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        public IActionResult OnGet()
        {
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }
    }
}
