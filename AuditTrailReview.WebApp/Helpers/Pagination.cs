using System.Collections.Generic;

namespace AuditTrailReview.WebApp.Helpers
{
    /// <summary>
    /// Shared helper for server-side list paging (see NFR-018). Used by any list page that
    /// previously loaded its full result set into memory -- Scan/Index, System/Index,
    /// Log/AuditTrail, Log/UserLogin, AdminPanel/User/Index, AdminPanel/Role/Index, and
    /// ConfigPanel/SystemType/Index -- to compute a short, windowed set of page-number links
    /// instead of rendering one link per page, which would not scale with a large history.
    /// </summary>
    public static class Pagination
    {
        /// <summary>Always includes page 1, the last page, and up to 2 pages either side of the current page.</summary>
        public static List<int?> BuildPageWindow(int currentPage, int totalPages, int neighbors = 2)
        {
            var pages = new SortedSet<int> { 1, totalPages };

            for (var p = currentPage - neighbors; p <= currentPage + neighbors; p++)
                if (p >= 1 && p <= totalPages)
                    pages.Add(p);

            var result = new List<int?>();
            int? previous = null;
            foreach (var p in pages)
            {
                if (previous.HasValue && p - previous.Value > 1)
                    result.Add(null); // ellipsis gap
                result.Add(p);
                previous = p;
            }

            return result;
        }
    }
}
