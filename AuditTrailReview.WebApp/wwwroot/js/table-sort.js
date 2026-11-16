// Universal Table Sorting - Works on any table with class "sortable"
document.addEventListener('DOMContentLoaded', function () {
    const tables = document.querySelectorAll('table.sortable');

    tables.forEach(table => {
        const headers = table.querySelectorAll('thead th');
        const tbody = table.querySelector('tbody');

        headers.forEach((header, index) => {
            // Skip columns with 'no-sort' class
            if (header.classList.contains('no-sort')) return;

            // Add sort icon
            header.style.cursor = 'pointer';
            header.style.userSelect = 'none';
            header.innerHTML += ' <i class="bi bi-arrow-down-up sort-icon"></i>';

            header.addEventListener('click', () => {
                sortTable(table, index);
            });
        });
    });
});

function sortTable(table, columnIndex) {
    const tbody = table.querySelector('tbody');
    const rows = Array.from(tbody.querySelectorAll('tr'));
    const header = table.querySelectorAll('thead th')[columnIndex];
    const currentOrder = header.dataset.order || 'asc';
    const newOrder = currentOrder === 'asc' ? 'desc' : 'asc';

    // Reset all headers
    table.querySelectorAll('thead th').forEach(th => {
        th.dataset.order = '';
        const icon = th.querySelector('.sort-icon');
        if (icon) icon.className = 'bi bi-arrow-down-up sort-icon';
    });

    // Set new order
    header.dataset.order = newOrder;
    const icon = header.querySelector('.sort-icon');
    if (icon) {
        icon.className = newOrder === 'asc' ? 'bi bi-sort-down sort-icon' : 'bi bi-sort-up sort-icon';
    }

    // Sort rows
    rows.sort((a, b) => {
        // <utc-datetime> cells carry the raw UTC instant in data-sort-value (ISO 8601), since
        // the app's configurable display format (AdminPanel/Settings/EditGeneral) isn't
        // guaranteed to be parseable by `new Date(...)` below -- prefer it when present.
        const aSortValue = a.cells[columnIndex]?.querySelector('[data-sort-value]')?.dataset.sortValue;
        const bSortValue = b.cells[columnIndex]?.querySelector('[data-sort-value]')?.dataset.sortValue;
        if (aSortValue && bSortValue) {
            const aDate = new Date(aSortValue);
            const bDate = new Date(bSortValue);
            if (!isNaN(aDate) && !isNaN(bDate)) {
                return newOrder === 'asc' ? aDate - bDate : bDate - aDate;
            }
        }

        const aCell = a.cells[columnIndex]?.textContent.trim() || '';
        const bCell = b.cells[columnIndex]?.textContent.trim() || '';

        // Try to parse as number
        const aNum = parseFloat(aCell.replace(/[^\d.-]/g, ''));
        const bNum = parseFloat(bCell.replace(/[^\d.-]/g, ''));

        if (!isNaN(aNum) && !isNaN(bNum)) {
            return newOrder === 'asc' ? aNum - bNum : bNum - aNum;
        }

        // Try to parse as date
        const aDate = new Date(aCell);
        const bDate = new Date(bCell);
        if (!isNaN(aDate) && !isNaN(bDate)) {
            return newOrder === 'asc' ? aDate - bDate : bDate - aDate;
        }

        // String comparison
        return newOrder === 'asc'
            ? aCell.localeCompare(bCell)
            : bCell.localeCompare(aCell);
    });

    // Re-append sorted rows
    rows.forEach(row => tbody.appendChild(row));
}