// Universal Table Filtering - Works on any table with class "filterable"
document.addEventListener('DOMContentLoaded', function () {
    const tables = document.querySelectorAll('table.filterable');

    tables.forEach(table => {
        // Create filter input if it doesn't exist
        let filterInput = table.previousElementSibling;
        if (!filterInput || !filterInput.classList.contains('table-filter')) {
            filterInput = createFilterInput();
            table.parentNode.insertBefore(filterInput, table);
        }

        const tbody = table.querySelector('tbody');
        const rows = tbody.querySelectorAll('tr');

        filterInput.addEventListener('input', function (e) {
            const filterText = e.target.value.toLowerCase().trim();

            rows.forEach(row => {
                if (filterText === '') {
                    row.style.display = '';
                    // See table-pagination.js: it reads this attribute (rather than
                    // row.style.display, which it also needs to write for its own paging
                    // purposes) to know whether a row currently matches the filter.
                    row.dataset.filterMatch = 'true';
                    return;
                }

                const cells = row.querySelectorAll('td');
                let found = false;

                cells.forEach(cell => {
                    const cellText = cell.textContent.toLowerCase();
                    if (cellText.includes(filterText)) {
                        found = true;
                    }
                });

                row.style.display = found ? '' : 'none';
                row.dataset.filterMatch = found ? 'true' : 'false';
            });

            updateNoResultsMessage(table, filterText);
        });
    });
});

function createFilterInput() {
    const wrapper = document.createElement('div');
    wrapper.className = 'table-filter mb-3';
    wrapper.innerHTML = `
        <div class="input-group">
            <span class="input-group-text">
                <i class="bi bi-search"></i>
            </span>
            <input type="text" 
                   class="form-control filter-input" 
                   placeholder="Filter table..." 
                   autocomplete="off">
            <button class="btn btn-outline-secondary clear-filter" type="button" title="Clear filter">
                <i class="bi bi-x-lg"></i>
            </button>
        </div>
        <small class="text-muted filter-info"></small>
    `;

    // Clear filter button
    const clearBtn = wrapper.querySelector('.clear-filter');
    const input = wrapper.querySelector('.filter-input');
    clearBtn.addEventListener('click', () => {
        input.value = '';
        input.dispatchEvent(new Event('input'));
        input.focus();
    });

    return wrapper;
}

function updateNoResultsMessage(table, filterText) {
    const tbody = table.querySelector('tbody');
    const visibleRows = Array.from(tbody.querySelectorAll('tr')).filter(row => row.style.display !== 'none');
    const totalRows = tbody.querySelectorAll('tr').length;
    
    const filterInfo = table.previousElementSibling.querySelector('.filter-info');
    
    if (filterText && visibleRows.length === 0) {
        if (!tbody.querySelector('.no-results-row')) {
            const noResultsRow = document.createElement('tr');
            noResultsRow.className = 'no-results-row';
            noResultsRow.innerHTML = `
                <td colspan="100" class="text-center text-muted py-4">
                    <i class="bi bi-inbox"></i> No results found for "${filterText}"
                </td>
            `;
            tbody.appendChild(noResultsRow);
        }
        filterInfo.textContent = '';
    } else {
        const noResultsRow = tbody.querySelector('.no-results-row');
        if (noResultsRow) {
            noResultsRow.remove();
        }
        
        if (filterText) {
            filterInfo.textContent = `Showing ${visibleRows.length} of ${totalRows} rows`;
        } else {
            filterInfo.textContent = '';
        }
    }
}