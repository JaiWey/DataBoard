/** Sorting and paging the rows of a table. Plain functions, so they are tested without a page. */

export type SortDirection = 'asc' | 'desc';
export interface Sort {
  column: number;
  direction: SortDirection;
}

const isMissing = (value: unknown): boolean => value === null || value === undefined || value === '';

/** Numbers by size, anything else as text (digits inside text by their number, so "item 2" comes before "item 10"). */
function compare(a: unknown, b: unknown): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

/**
 * The next sort when a column header is clicked: first ascending, then descending, then back to the order the rows came
 * in. Clicking another column starts that one ascending.
 */
export function nextSort(current: Sort | null, column: number): Sort | null {
  if (current?.column !== column) return { column, direction: 'asc' };
  return current.direction === 'asc' ? { column, direction: 'desc' } : null;
}

/** The rows in the order asked for. Empty cells always come last, whichever way it is sorted. The input is not changed. */
export function sortRows(rows: unknown[][], sort: Sort | null): unknown[][] {
  if (!sort) return rows;
  const sign = sort.direction === 'asc' ? 1 : -1;
  return rows
    .map((row, index) => ({ row, index }))
    .sort((x, y) => {
      const a = x.row[sort.column];
      const b = y.row[sort.column];
      if (isMissing(a) || isMissing(b)) return isMissing(a) === isMissing(b) ? x.index - y.index : isMissing(a) ? 1 : -1;
      return sign * compare(a, b) || x.index - y.index; // equal rows keep the order they came in
    })
    .map((x) => x.row);
}

export const pageCount = (rows: number, pageSize: number | null | undefined): number =>
  !pageSize ? 1 : Math.max(1, Math.ceil(rows / pageSize));

/**
 * The page numbers to show under a table, as in "1 2 3 4 5 … 90": the first pages, the last, and the ones around the
 * current page, with a gap marked by null where pages are left out. Pages are 1-based.
 */
export function pageList(current: number, total: number): (number | null)[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

  const shown = new Set<number>([1, total, current - 1, current, current + 1]);
  if (current <= 3) [2, 3, 4, 5].forEach((p) => shown.add(p));
  if (current >= total - 2) [total - 4, total - 3, total - 2, total - 1].forEach((p) => shown.add(p));

  const pages = [...shown].filter((p) => p >= 1 && p <= total).sort((a, b) => a - b);
  const out: (number | null)[] = [];
  pages.forEach((p, i) => {
    if (i > 0 && p - pages[i - 1] > 1) {
      // A gap of one page is not worth a mark: show the page.
      if (p - pages[i - 1] === 2) out.push(p - 1);
      else out.push(null);
    }
    out.push(p);
  });
  return out;
}
