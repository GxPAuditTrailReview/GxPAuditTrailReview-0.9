using System.Reflection;
using AuditTrailReview.WebApp.Areas.Identity.Pages.Account;
using AuditTrailReview.WebApp.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="DisabledIdentityPages"/>, the conventions that switch off the unused
/// scaffolded Identity pages (self-registration above all) and require sign-in for
/// <c>/Account/Manage</c>. The conventions are applied to real <see cref="PageApplicationModel"/>
/// instances, the same objects Razor Pages builds at startup, so no running host or database is
/// needed (Program.cs's startup path queries a real database before the app is built).
/// </summary>
[TestClass]
public class DisabledIdentityPagesTests
{
    private const string PageModelNamespace = "AuditTrailReview.WebApp.Areas.Identity.Pages";

    /// <summary>
    /// Builds the conventions the same way Program.cs does, through AddRazorPages: the
    /// folder-authorization convention reads MvcOptions from DI, which only the collection that
    /// RazorPagesOptions creates has access to.
    /// </summary>
    private static PageConventionCollection CreateConventions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRazorPages(options => DisabledIdentityPages.Apply(options.Conventions));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<RazorPagesOptions>>().Value.Conventions;
    }

    private static PageApplicationModel ApplyConventions(string viewEnginePath)
    {
        var conventions = CreateConventions();

        var descriptor = new PageActionDescriptor
        {
            AreaName = DisabledIdentityPages.AreaName,
            ViewEnginePath = viewEnginePath,
            RelativePath = $"/Areas/Identity/Pages{viewEnginePath}.cshtml",
        };
        var model = new PageApplicationModel(descriptor, typeof(LoginModel).GetTypeInfo(), Array.Empty<object>());

        foreach (var convention in conventions.OfType<IPageApplicationModelConvention>())
        {
            convention.Apply(model);
        }

        return model;
    }

    private static bool RequiresAuthorization(PageApplicationModel model) =>
        model.EndpointMetadata.OfType<IAuthorizeData>().Any()
        || model.Filters.OfType<AuthorizeFilter>().Any();

    [TestMethod]
    [DataRow("/Account/Register")]
    [DataRow("/Account/RegisterConfirmation")]
    [DataRow("/Account/ExternalLogin")]
    [DataRow("/Account/Manage/Email")]
    [DataRow("/Account/Manage/DeletePersonalData")]
    [DataRow("/Account/Manage/DownloadPersonalData")]
    [DataRow("/Account/Manage/EnableAuthenticator")]
    public void Apply_DisabledPage_AddsNotFoundFilter(string path)
    {
        var model = ApplyConventions(path);

        Assert.IsTrue(model.Filters.Contains(DisabledIdentityPages.NotFoundFilter.Instance),
            $"{path} should answer 404.");
    }

    [TestMethod]
    [DataRow("/Account/Login")]
    [DataRow("/Account/Logout")]
    [DataRow("/Account/AccessDenied")]
    [DataRow("/Account/Lockout")]
    [DataRow("/Account/ForgotPassword")]
    [DataRow("/Account/ForgotPasswordConfirmation")]
    [DataRow("/Account/ResetPassword")]
    [DataRow("/Account/ResetPasswordConfirmation")]
    [DataRow("/Account/LoginWith2fa")]
    [DataRow("/Account/LoginWithRecoveryCode")]
    [DataRow("/Account/Manage/Index")]
    [DataRow("/Account/Manage/ChangePassword")]
    [DataRow("/Account/Manage/Disable2fa")]
    public void Apply_PageInUse_DoesNotAddNotFoundFilter(string path)
    {
        var model = ApplyConventions(path);

        Assert.IsFalse(model.Filters.Contains(DisabledIdentityPages.NotFoundFilter.Instance),
            $"{path} is used by the application and must stay reachable.");
    }

    [TestMethod]
    [DataRow("/Account/Manage/Index")]
    [DataRow("/Account/Manage/ChangePassword")]
    [DataRow("/Account/Manage/Disable2fa")]
    public void Apply_ManagePage_RequiresAuthorization(string path)
    {
        var model = ApplyConventions(path);

        Assert.IsTrue(RequiresAuthorization(model), $"{path} should require a signed-in user.");
    }

    [TestMethod]
    [DataRow("/Account/Login")]
    [DataRow("/Account/ForgotPassword")]
    [DataRow("/Account/ResetPassword")]
    public void Apply_AnonymousAccountPage_DoesNotRequireAuthorization(string path)
    {
        var model = ApplyConventions(path);

        Assert.IsFalse(RequiresAuthorization(model), $"{path} must stay reachable before sign-in.");
    }

    [TestMethod]
    public void Apply_PageWithSamePathInAnotherArea_DoesNotAddNotFoundFilter()
    {
        var conventions = CreateConventions();
        var descriptor = new PageActionDescriptor { AreaName = null, ViewEnginePath = "/Account/Register" };
        var model = new PageApplicationModel(descriptor, typeof(LoginModel).GetTypeInfo(), Array.Empty<object>());

        foreach (var convention in conventions.OfType<IPageApplicationModelConvention>())
        {
            convention.Apply(model);
        }

        Assert.IsFalse(model.Filters.Contains(DisabledIdentityPages.NotFoundFilter.Instance));
    }

    /// <summary>
    /// A typo in <see cref="DisabledIdentityPages.Paths"/> would silently match no page and leave
    /// it reachable, so every entry must correspond to a scaffolded page model in the WebApp.
    /// </summary>
    [TestMethod]
    public void Paths_EachEntry_MatchesAScaffoldedPageModel()
    {
        var assembly = typeof(LoginModel).Assembly;

        var unmatched = DisabledIdentityPages.Paths
            .Where(path => assembly.GetType($"{PageModelNamespace}{path.Replace('/', '.')}Model") is null)
            .ToList();

        Assert.IsTrue(unmatched.Count == 0,
            "No scaffolded page model found for: " + string.Join(", ", unmatched));
    }

    [TestMethod]
    public void NotFoundFilter_OnResourceExecuting_SetsNotFoundResult()
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ResourceExecutingContext(actionContext, new List<IFilterMetadata>(), new List<IValueProviderFactory>());

        DisabledIdentityPages.NotFoundFilter.Instance.OnResourceExecuting(context);

        Assert.IsInstanceOfType<NotFoundResult>(context.Result);
    }
}
