using AuditTrailReview.Database;

namespace AuditTrailReview.WebApp.Helpers
{
    /// <summary>
    /// Reads an <see cref="ApplicationUser"/>'s Identity lockout fields (LockoutEnabled/LockoutEnd
    /// -- no custom "IsActive" column exists or is needed) as a simple deactivated/active status.
    /// "Deactivated" covers both an administrator checking the Deactivate box on
    /// AdminPanel/User/Edit and the automatic escalation in Areas/Identity/Pages/Account/Login.cshtml.cs
    /// after too many failed sign-in attempts -- both set LockoutEnd to DateTimeOffset.MaxValue,
    /// so there is nothing else to distinguish here.
    /// </summary>
    public static class UserAccountStatus
    {
        public static bool IsDeactivated(ApplicationUser user) =>
            user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
    }

    /// <summary>Minimal audit-friendly projection used to log just the AccountStatus field changing (see Login.cshtml.cs's automatic-deactivation path).</summary>
    public sealed class AccountStatusAuditDto
    {
        public string? AccountStatus { get; init; }
    }
}
