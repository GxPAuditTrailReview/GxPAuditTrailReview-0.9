using AuditTrailReview.WebApp.Pages.Log;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Regression guard for the role restriction on the two system-wide log pages. A plain "User" must
/// not be able to read the login history (user names, IP addresses, failed attempts) or the full
/// change history, so both pages require Admin or Super User. Pure reflection over the real
/// compiled page models, the same approach as <see cref="PageAuthorizationConventionTests"/>.
/// The Logs menu in Pages/Shared/_Layout.cshtml is hidden for the same roles.
/// </summary>
[TestClass]
public class LogPagesAuthorizationTests
{
    private static string[] RolesOf(Type pageModel)
    {
        var attributes = pageModel.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        Assert.IsTrue(attributes.Count > 0, $"{pageModel.Name} must declare [Authorize].");

        return attributes
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }

    [TestMethod]
    public void UserLoginModel_Authorize_RequiresAdminOrSuperUser()
    {
        CollectionAssert.AreEqual(new[] { "Admin", "Super User" }, RolesOf(typeof(UserLoginModel)));
    }

    [TestMethod]
    public void AuditTrail_Authorize_RequiresAdminOrSuperUser()
    {
        CollectionAssert.AreEqual(new[] { "Admin", "Super User" }, RolesOf(typeof(AuditTrail)));
    }

    [TestMethod]
    public void LogPages_Authorize_DoNotAllowPlainUserRole()
    {
        foreach (var pageModel in new[] { typeof(UserLoginModel), typeof(AuditTrail) })
        {
            CollectionAssert.DoesNotContain(RolesOf(pageModel), "User", $"{pageModel.Name} must not be open to the plain User role.");
        }
    }
}
