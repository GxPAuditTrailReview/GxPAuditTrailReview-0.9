using System.Reflection;
using AuditTrailReview.WebApp.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Regression guard for Program.cs's `AuthorizeFolder("/")` convention: every PageModel under
/// the plain Pages/ tree (not Areas/Identity/Pages, which AddDefaultIdentity's own AddDefaultUI()
/// governs independently) must carry either [Authorize] or an explicit [AllowAnonymous] --
/// nothing should be left "undecorated" and relying only on the runtime convention, which a
/// future refactor could remove without this test noticing. Catches the exact class of bug the
/// convention itself closes: a new admin/config page added without remembering the attribute.
///
/// Pure reflection over the real compiled assembly, no mocking library, consistent with this
/// project's "real objects over mocks" style.
/// </summary>
[TestClass]
public class PageAuthorizationConventionTests
{
    [TestMethod]
    public void AllPagesUnderPagesFolder_WhenInspected_DeclareAuthorizeOrAllowAnonymous()
    {
        var pageModelTypes = typeof(IndexModel).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(PageModel).IsAssignableFrom(t))
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("AuditTrailReview.WebApp.Pages", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(pageModelTypes.Count > 10, "Expected to find a substantial number of PageModel types -- if this is low, the discovery filter above is probably wrong, not the app.");

        var undecorated = pageModelTypes
            .Where(t => !t.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Any()
                     && !t.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Any())
            .Select(t => t.FullName)
            .OrderBy(name => name)
            .ToList();

        Assert.IsTrue(undecorated.Count == 0,
            "The following PageModel types have neither [Authorize] nor [AllowAnonymous] -- " +
            "every page under Pages/ must declare one explicitly: " + string.Join(", ", undecorated));
    }
}
