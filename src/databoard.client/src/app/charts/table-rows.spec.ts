import { nextSort, pageCount, pageList, sortRows } from './table-rows';

describe('nextSort', () => {
  it('goes ascending, then descending, then back to the order the rows came in', () => {
    const first = nextSort(null, 1);
    const second = nextSort(first, 1);

    expect(first).toEqual({ column: 1, direction: 'asc' });
    expect(second).toEqual({ column: 1, direction: 'desc' });
    expect(nextSort(second, 1)).toBeNull();
  });

  it('starts another column ascending', () => {
    expect(nextSort({ column: 1, direction: 'desc' }, 2)).toEqual({ column: 2, direction: 'asc' });
  });
});

describe('sortRows', () => {
  const rows: unknown[][] = [['b', 10], ['a', 2], ['c', null], ['d', 30]];

  it('returns the rows as they are when there is no sort, and never changes the rows it is given', () => {
    expect(sortRows(rows, null)).toBe(rows);

    sortRows(rows, { column: 1, direction: 'desc' });

    expect(rows.map((r) => r[0])).toEqual(['b', 'a', 'c', 'd']);
  });

  it('sorts numbers by size, not as text', () => {
    expect(sortRows(rows, { column: 1, direction: 'asc' }).map((r) => r[1])).toEqual([2, 10, 30, null]);
    expect(sortRows(rows, { column: 1, direction: 'desc' }).map((r) => r[1])).toEqual([30, 10, 2, null]);
  });

  it('sorts text ignoring case, with numbers inside it by their value', () => {
    const text: unknown[][] = [['item 10'], ['Item 2'], ['item 1'], ['apple']];

    expect(sortRows(text, { column: 0, direction: 'asc' }).map((r) => r[0])).toEqual(['apple', 'item 1', 'Item 2', 'item 10']);
  });

  it('puts empty cells last whichever way it is sorted', () => {
    const empty: unknown[][] = [[null], ['b'], [''], ['a'], [undefined]];

    expect(sortRows(empty, { column: 0, direction: 'asc' }).map((r) => r[0])).toEqual(['a', 'b', null, '', undefined]);
    expect(sortRows(empty, { column: 0, direction: 'desc' }).map((r) => r[0])).toEqual(['b', 'a', null, '', undefined]);
  });

  it('keeps rows with equal values in the order they came in', () => {
    const ties: unknown[][] = [['x', 1, 'first'], ['x', 1, 'second'], ['x', 1, 'third']];

    expect(sortRows(ties, { column: 0, direction: 'asc' }).map((r) => r[2])).toEqual(['first', 'second', 'third']);
    expect(sortRows(ties, { column: 0, direction: 'desc' }).map((r) => r[2])).toEqual(['first', 'second', 'third']);
  });

  it('sorts dates written as text by the text, which is their order when written year first', () => {
    const dates: unknown[][] = [['2026-03-01'], ['2025-12-31'], ['2026-01-15']];

    expect(sortRows(dates, { column: 0, direction: 'asc' }).map((r) => r[0])).toEqual(['2025-12-31', '2026-01-15', '2026-03-01']);
  });

  it('sorts true and false', () => {
    expect(sortRows([[true], [false], [true]], { column: 0, direction: 'asc' }).map((r) => r[0])).toEqual([false, true, true]);
  });
});

describe('pageCount', () => {
  it('is one page when there is no page size, however many rows', () => {
    expect(pageCount(1000, null)).toBe(1);
    expect(pageCount(1000, undefined)).toBe(1);
  });

  it('rounds up, and is at least one page', () => {
    expect(pageCount(100, 20)).toBe(5);
    expect(pageCount(101, 20)).toBe(6);
    expect(pageCount(0, 20)).toBe(1);
  });
});

describe('pageList', () => {
  it('lists every page when there are few', () => {
    expect(pageList(1, 1)).toEqual([1]);
    expect(pageList(3, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(pageList(4, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  it('shows the first pages, a gap and the last one when near the start', () => {
    expect(pageList(1, 90)).toEqual([1, 2, 3, 4, 5, null, 90]);
    expect(pageList(3, 90)).toEqual([1, 2, 3, 4, 5, null, 90]);
  });

  it('shows the first page, a gap, the pages around the current one, a gap and the last', () => {
    expect(pageList(10, 90)).toEqual([1, null, 9, 10, 11, null, 90]);
  });

  it('shows the last pages when near the end', () => {
    expect(pageList(90, 90)).toEqual([1, null, 86, 87, 88, 89, 90]);
    expect(pageList(88, 90)).toEqual([1, null, 86, 87, 88, 89, 90]);
  });

  it('has no gap where the pages are next to each other', () => {
    expect(pageList(5, 9)).toEqual([1, null, 4, 5, 6, null, 9]);
    expect(pageList(4, 9)).toEqual([1, 2, 3, 4, 5, null, 9]);
    expect(pageList(6, 9)).toEqual([1, null, 5, 6, 7, 8, 9]);
  });
});
