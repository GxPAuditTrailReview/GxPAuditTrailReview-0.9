// Lazily loads a table's rows from the server, one small page at a time, instead of rendering
// every row into the page up front. Built for Scan/Details, which uses it for both the Scan
// results tab and the Potential findings tab (see the two initServerPagedTable(...) calls in
// Details.cshtml, and DetailsModel.OnGetScanResultsPageAsync / OnGetPotentialFindingsPageAsync
// in Details.cshtml.cs) -- a scan can have thousands of rows, and shipping/rendering all of them
// into the initial page load, even though only 8-15 are ever visible at once, is what made large
// scans slow to open. Filtering and column sorting are server-side round-trips here too (unlike
// table-filter.js/table-sort.js, which only ever operate on rows already sitting in the DOM),
// since the whole point is that not every row is loaded on the client at any given time.
//
// Reusable across any table whose server endpoint returns this same JSON shape ({ rows,
// totalCount, page, pageSize, totalPages, scanClosed }, each row shaped like
// OnGetScanResultsPageAsync's). This is intentionally a separate, opt-in script -- it does not
// touch table-filter.js, table-sort.js, or table-pagination.js, and does not run automatically
// on any table, so it can never change the behavior of any other page.
function initServerPagedTable(tableId, pageSize) {
    const table = document.getElementById(tableId);
    if (!table) return;

    const endpointUrl = table.dataset.pageUrl;
    if (!endpointUrl) return;

    const emptyMessage = table.dataset.emptyMessage || 'No rows found.';

    const tbody = table.querySelector('tbody');
    const headers = Array.from(table.querySelectorAll('thead th[data-sort-key]'));

    let currentPage = 1;
    let filterText = '';
    let sortKey = null;
    let sortDir = 'asc';
    let scanClosed = false;
    let requestSeq = 0; // guards against an older, slower request overwriting a newer one

    // ── Filter box (mirrors table-filter.js's UI so this table still looks/feels the same) ──
    const filterWrapper = document.createElement('div');
    filterWrapper.className = 'table-filter mb-3';
    filterWrapper.innerHTML = `
        <div class="input-group">
            <span class="input-group-text"><i class="bi bi-search"></i></span>
            <input type="text" class="form-control filter-input" placeholder="Filter table..." autocomplete="off">
            <button class="btn btn-outline-secondary clear-filter" type="button" title="Clear filter">
                <i class="bi bi-x-lg"></i>
            </button>
        </div>
        <small class="text-muted filter-info"></small>`;
    table.parentNode.insertBefore(filterWrapper, table);

    const filterInput = filterWrapper.querySelector('.filter-input');
    const filterInfo = filterWrapper.querySelector('.filter-info');
    const clearBtn = filterWrapper.querySelector('.clear-filter');

    let filterDebounceTimer = null;
    filterInput.addEventListener('input', () => {
        clearTimeout(filterDebounceTimer);
        // Debounced so a fast typist doesn't fire a request per keystroke -- table-filter.js's
        // in-memory version could afford to run on every keystroke, this one can't.
        filterDebounceTimer = setTimeout(() => {
            filterText = filterInput.value.trim();
            loadPage(1);
        }, 350);
    });
    clearBtn.addEventListener('click', () => {
        filterInput.value = '';
        clearTimeout(filterDebounceTimer);
        filterText = '';
        loadPage(1);
        filterInput.focus();
    });

    // ── Pager (Prev/Next + "showing x-y of n") ──
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
    prevBtn.addEventListener('click', () => loadPage(currentPage - 1));
    nextBtn.addEventListener('click', () => loadPage(currentPage + 1));

    // ── Sortable column headers ──
    headers.forEach(header => {
        header.style.cursor = 'pointer';
        header.style.userSelect = 'none';
        header.innerHTML += ' <i class="bi bi-arrow-down-up sort-icon"></i>';
        header.addEventListener('click', () => {
            const key = header.dataset.sortKey;
            sortDir = (sortKey === key && sortDir === 'asc') ? 'desc' : 'asc';
            sortKey = key;

            headers.forEach(h => {
                const icon = h.querySelector('.sort-icon');
                if (icon) icon.className = 'bi bi-arrow-down-up sort-icon';
            });
            const icon = header.querySelector('.sort-icon');
            if (icon) icon.className = sortDir === 'asc' ? 'bi bi-sort-down sort-icon' : 'bi bi-sort-up sort-icon';

            loadPage(1);
        });
    });

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    function renderRow(row) {
        const findingCell = escapeHtml(row.finding) +
            (row.showLiveD2 ? `<div>${escapeHtml(row.liveD2Text)}</div>` : '');

        let reviewStatusCell = '';
        if (row.reviewStatus !== null && row.reviewStatus !== undefined) {
            reviewStatusCell = `<span class="badge ${row.reviewBadgeClass}" title="${escapeHtml(row.reviewedBy)} – ${escapeHtml(row.reviewedAt)}">${row.reviewIcon} ${escapeHtml(row.reviewLabel)}</span>`;
            if (row.reviewComment) {
                reviewStatusCell += `<br /><small class="text-muted">${escapeHtml(row.reviewComment)}</small>`;
            }
        }

        let actionCell = '';
        if (!scanClosed) {
            actionCell = '<td>' + (row.canReview ? `
                    <button type="button" class="btn btn-sm btn-outline-secondary"
                            data-bs-toggle="modal" data-bs-target="#reviewModal"
                            data-id="${row.id}"
                            data-finding="${escapeHtml(row.finding)}"
                            data-status="${row.reviewStatus ?? -1}"
                            data-comment="${escapeHtml(row.reviewComment)}">
                        Review
                    </button>` : '') + '</td>';
        }

        return `<tr class="${row.rowClass || ''}">
            <td>${escapeHtml(row.uniqueIdentifier)}</td>
            <td>${escapeHtml(row.timestamp)}</td>
            <td>${escapeHtml(row.userId)}</td>
            <td>${escapeHtml(row.objectType)}</td>
            <td>${escapeHtml(row.object)}</td>
            <td>${escapeHtml(row.field)}</td>
            <td>${escapeHtml(row.operation)}</td>
            <td>${escapeHtml(row.oldValue)}</td>
            <td>${escapeHtml(row.newValue)}</td>
            <td>${escapeHtml(row.reason)}</td>
            <td>${escapeHtml(row.source)}</td>
            <td>${findingCell}</td>
            <td>${reviewStatusCell}</td>
            ${actionCell}
        </tr>`;
    }

    function setLoading(isLoading) {
        table.style.opacity = isLoading ? '0.5' : '';
    }

    function loadPage(page) {
        const mySeq = ++requestSeq;
        setLoading(true);

        const url = new URL(endpointUrl, window.location.origin);
        url.searchParams.set('page', page);
        url.searchParams.set('pageSize', pageSize);
        if (filterText) url.searchParams.set('filter', filterText);
        if (sortKey) {
            url.searchParams.set('sortBy', sortKey);
            url.searchParams.set('sortDir', sortDir);
        }

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(r => r.ok ? r.json() : Promise.reject(r.status))
            .then(data => {
                if (mySeq !== requestSeq) return; // a newer request has already superseded this one

                currentPage = data.page;
                scanClosed = data.scanClosed;

                if (!data.rows || data.rows.length === 0) {
                    const message = filterText
                        ? `<i class="bi bi-inbox"></i> No results found for "${escapeHtml(filterText)}"`
                        : emptyMessage;
                    tbody.innerHTML = `<tr><td colspan="100" class="text-center text-muted py-4">${message}</td></tr>`;
                } else {
                    tbody.innerHTML = data.rows.map(renderRow).join('');
                }

                if (filterText) {
                    filterInfo.textContent = `Showing ${data.totalCount === 0 ? 0 : 1}-${data.totalCount} matching row${data.totalCount === 1 ? '' : 's'}`;
                } else {
                    filterInfo.textContent = '';
                }

                if (data.totalCount === 0) {
                    infoEl.textContent = '';
                } else {
                    const start = (data.page - 1) * data.pageSize + 1;
                    const end = Math.min(data.page * data.pageSize, data.totalCount);
                    infoEl.textContent = `Showing ${start}-${end} of ${data.totalCount} row${data.totalCount === 1 ? '' : 's'} (page ${data.page} of ${data.totalPages})`;
                }
                prevBtn.disabled = data.page <= 1;
                nextBtn.disabled = data.page >= data.totalPages;
                pager.style.display = data.totalCount <= data.pageSize ? 'none' : '';
            })
            .catch(() => {
                if (mySeq !== requestSeq) return;
                tbody.innerHTML = `<tr><td colspan="100" class="text-center text-danger py-4">
                    Couldn't load scan results. Please try refreshing the page.
                </td></tr>`;
            })
            .finally(() => {
                if (mySeq === requestSeq) setLoading(false);
            });
    }

    loadPage(1);
}
