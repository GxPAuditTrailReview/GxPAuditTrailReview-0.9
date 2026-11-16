// Opt-in client-side pagination for a single table. Unlike table-sort.js/table-filter.js this
// does NOT run automatically on every "sortable"/"filterable" table -- a page must explicitly
// call initTablePagination(tableId, pageSize) once its table exists in the DOM, so adding
// pagination here can never change the behavior of any other page's tables.
//
// Cooperates with the other two universal table scripts instead of fighting them:
//   - table-filter.js stamps each row with data-filter-match="true"/"false" (in addition to
//     the row.style.display it already sets). This script reads that attribute to know which
//     rows are currently eligible for paging, and owns row.style.display itself from then on
//     to slice out just the current page -- the two scripts never need to run at the same time
//     for the same purpose, they just hand off through that one data attribute.
//   - table-sort.js physically reorders <tr> elements in the tbody and doesn't touch
//     display/dataset at all, so after a sort this script just needs to recompute pages from
//     the rows' new DOM order -- nothing needs to be undone.
//
// Call this AFTER the DOMContentLoaded handlers in table-filter.js/table-sort.js have already
// run (e.g. from your own DOMContentLoaded listener, registered after those scripts are
// included on the page), so the filter box and sort headers already exist.
function initTablePagination(tableId, pageSize) {
    const table = document.getElementById(tableId);
    if (!table) return;

    const tbody = table.querySelector('tbody');
    if (!tbody) return;

    let currentPage = 1;

    function dataRows() {
        return Array.from(tbody.querySelectorAll('tr')).filter(r => !r.classList.contains('no-results-row'));
    }

    // Pager UI, inserted right after the table.
    const pager = document.createElement('div');
    pager.className = 'd-flex justify-content-between align-items-center mt-2 table-pagination';
    pager.innerHTML = `
        <small class="text-muted pagination-info"></small>
        <div class="btn-group btn-group-sm" role="group">
            <button type="button" class="btn btn-outline-secondary pagination-prev">
                <i class="bi bi-chevron-left"></i> Prev
            </button>
            <button type="button" class="btn btn-outline-secondary pagination-next">
                Next <i class="bi bi-chevron-right"></i>
            </button>
        </div>`;
    table.parentNode.insertBefore(pager, table.nextSibling);

    const infoEl = pager.querySelector('.pagination-info');
    const prevBtn = pager.querySelector('.pagination-prev');
    const nextBtn = pager.querySelector('.pagination-next');

    function applyPagination() {
        const all = dataRows();

        // A row counts as "matching" unless table-filter.js explicitly marked it as not
        // matching the current filter text. Before any filter input has happened (page just
        // loaded, or this table has no filter box), the attribute is simply unset, which is
        // treated as a match -- every row is visible by default in that state.
        const totalRows = all.reduce((n, row) => n + (row.dataset.filterMatch === 'false' ? 0 : 1), 0);
        const totalPages = Math.max(1, Math.ceil(totalRows / pageSize));

        if (currentPage > totalPages) currentPage = totalPages;
        if (currentPage < 1) currentPage = 1;

        const start = (currentPage - 1) * pageSize;
        const end = start + pageSize;

        let matchIndex = 0;
        all.forEach(row => {
            if (row.dataset.filterMatch === 'false') {
                row.style.display = 'none';
                return;
            }
            const idx = matchIndex++;
            row.style.display = (idx >= start && idx < end) ? '' : 'none';
        });

        if (totalRows === 0) {
            infoEl.textContent = '';
        } else {
            infoEl.textContent =
                `Showing ${start + 1}-${Math.min(end, totalRows)} of ${totalRows} row${totalRows === 1 ? '' : 's'} ` +
                `(page ${currentPage} of ${totalPages})`;
        }

        prevBtn.disabled = currentPage <= 1;
        nextBtn.disabled = currentPage >= totalPages;
        pager.style.display = totalRows <= pageSize ? 'none' : '';
    }

    prevBtn.addEventListener('click', () => {
        currentPage--;
        applyPagination();
    });
    nextBtn.addEventListener('click', () => {
        currentPage++;
        applyPagination();
    });

    // Re-paginate (back to page 1) whenever the filter text changes. table-filter.js's own
    // 'input' listener on this same box runs first -- this script is included after it on the
    // page, and DOMContentLoaded/'input' listeners fire in registration order -- so
    // row.dataset.filterMatch is already up to date by the time this handler runs.
    const filterWrapper = table.previousElementSibling;
    const filterInput = filterWrapper && filterWrapper.classList.contains('table-filter')
        ? filterWrapper.querySelector('.filter-input')
        : null;
    if (filterInput) {
        filterInput.addEventListener('input', () => {
            currentPage = 1;
            applyPagination();
        });
    }

    // Re-paginate after a column sort. table-sort.js's own click listener on each header
    // (registered first, since that script is included before this one) does the actual
    // reordering; this just recomputes which rows land on the current page afterward. Filter
    // state lives in a data attribute rather than in style.display, so it survives the
    // reordering untouched -- nothing needs to be re-checked against the filter text itself.
    table.querySelectorAll('thead th:not(.no-sort)').forEach(header => {
        header.addEventListener('click', () => applyPagination());
    });

    applyPagination();
}
