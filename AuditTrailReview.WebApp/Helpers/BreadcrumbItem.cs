namespace AuditTrailReview.WebApp.Helpers;

/// <summary>
/// One entry of the breadcrumb trail rendered by Pages/Shared/_Breadcrumb.cshtml.
/// An item with a <see cref="Page"/> is a link (unless it is the last item, which is always the
/// current page); an item without one is plain text (e.g. the "Admin Panel" group, which has no landing page).
/// </summary>
public sealed record BreadcrumbItem(string Text, string? Page = null);
