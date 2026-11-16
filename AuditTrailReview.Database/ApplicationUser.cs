using Microsoft.AspNetCore.Identity;

namespace AuditTrailReview.Database
{
    /// <summary>
    /// The app's Identity user type. Adds FirstName/LastName so a user can enter a name on
    /// their Profile page (Areas/Identity/Pages/Account/Manage/Index.cshtml.cs) and an Admin
    /// can set/view one when managing another account (Pages/AdminPanel/User/*.cshtml.cs).
    /// This is ASP.NET Core Identity's own supported extension point for custom profile data
    /// (every scaffolded Identity template generates exactly this ApplicationUser : IdentityUser
    /// pattern) -- replacing the framework's default, bare IdentityUser everywhere it was
    /// previously used as the Identity generic type parameter (UserManager&lt;T&gt;,
    /// SignInManager&lt;T&gt;, IdentityDbContext&lt;T&gt;, etc.) across the WebApp project.
    /// Both new columns are added via a raw-SQL schema addendum script per ADR-0004, not an
    /// EF Core migration -- see "Database schema/ATR 0.10 ... - AspNetUsers Add FirstName
    /// LastName.sql".
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
