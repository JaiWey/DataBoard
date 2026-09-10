import { LayoutItem, arrange, compact, fit, move, readingOrder, resize, sameLayout } from './dashboard-layout';

const item = (id: string, x: number, y: number, w: number, h: number): LayoutItem => ({ id, x, y, w, h });
const byId = (items: LayoutItem[], id: string) => items.find((i) => i.id === id)!;
const overlaps = (items: LayoutItem[]) =>
  items.some((a, i) => items.slice(i + 1).some((b) => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h));

describe('fit', () => {
  it('keeps a rectangle inside the 12 columns and the allowed sizes', () => {
    expect(fit({ x: 10, y: -3, w: 6, h: 99 })).toEqual({ x: 6, y: 0, w: 6, h: 20 });
    expect(fit({ x: -2, y: 4, w: 0, h: 0 })).toEqual({ x: 0, y: 4, w: 1, h: 1 });
    expect(fit({ x: 1.4, y: 2.6, w: 3.2, h: 2.5 })).toEqual({ x: 1, y: 3, w: 3, h: 3 });
  });
});

describe('arrange', () => {
  it('puts widgets without a place side by side, then on the next row', () => {
    const items = arrange([{ id: 'a' }, { id: 'b' }, { id: 'c' }]);

    expect(items.map((i) => [i.id, i.x, i.y, i.w, i.h])).toEqual([['a', 0, 0, 6, 5], ['b', 6, 0, 6, 5], ['c', 0, 5, 6, 5]]);
  });

  it('keeps the places widgets already have, and fills the gaps with the rest', () => {
    const items = arrange([{ id: 'new' }, { id: 'old', layout: { x: 0, y: 0, w: 8, h: 4 } }]);

    expect(byId(items, 'old')).toEqual(item('old', 0, 0, 8, 4));
    expect(byId(items, 'new')).toEqual(item('new', 0, 4, 6, 5)); // no room beside 'old', so under it
    expect(items.map((i) => i.id)).toEqual(['new', 'old']); // the order of the dashboard is kept
  });

  it('separates stored places that overlap', () => {
    const items = arrange([
      { id: 'a', layout: { x: 0, y: 0, w: 6, h: 4 } },
      { id: 'b', layout: { x: 3, y: 2, w: 6, h: 4 } },
    ]);

    expect(overlaps(items)).toBe(false);
  });

  it('copes with an empty dashboard', () => {
    expect(arrange([])).toEqual([]);
  });
});

describe('compact', () => {
  it('lets widgets rise into empty space', () => {
    const items = compact([item('a', 0, 3, 4, 2), item('b', 4, 7, 4, 2)]);

    expect(items.map((i) => i.y)).toEqual([0, 0]);
  });

  it('stops a widget under another one', () => {
    const items = compact([item('a', 0, 0, 4, 3), item('b', 0, 9, 4, 2)]);

    expect(byId(items, 'b').y).toBe(3);
  });
});

describe('move', () => {
  const start = () => [item('a', 0, 0, 6, 4), item('b', 6, 0, 6, 4), item('c', 0, 4, 12, 3)];

  it('moves a widget to a free place', () => {
    const next = move([item('a', 0, 0, 4, 3), item('b', 4, 0, 4, 3)], 'b', 8, 0);

    expect(byId(next, 'b')).toEqual(item('b', 8, 0, 4, 3));
  });

  it('keeps a widget inside the grid', () => {
    expect(byId(move(start(), 'a', 40, 0), 'a').x).toBe(6);
    expect(byId(move(start(), 'a', -5, 0), 'a').x).toBe(0);
  });

  it('pushes the widgets in the way down instead of overlapping them', () => {
    const next = move(start(), 'c', 0, 0);

    expect(overlaps(next)).toBe(false);
    expect(byId(next, 'c').y).toBe(0);
    expect(byId(next, 'a').y).toBeGreaterThanOrEqual(3);
    expect(byId(next, 'b').y).toBeGreaterThanOrEqual(3);
  });

  it('does not leave a hole where the widget was', () => {
    const next = move(start(), 'c', 0, 20); // far below: it can only go as low as the others allow

    expect(byId(next, 'c').y).toBe(4);
    expect(overlaps(next)).toBe(false);
  });

  it('does not change the list it was given, and keeps its order', () => {
    const before = start();
    const next = move(before, 'c', 0, 0);

    expect(before).toEqual(start());
    expect(next.map((i) => i.id)).toEqual(['a', 'b', 'c']);
  });

  it('ignores a widget it does not know', () => {
    expect(move(start(), 'zzz', 3, 3)).toEqual(start());
  });
});

describe('resize', () => {
  const start = () => [item('a', 0, 0, 4, 3), item('b', 4, 0, 4, 3), item('c', 0, 3, 4, 2)];

  it('gives a widget its new size', () => {
    expect(byId(resize(start(), 'b', 6, 5), 'b')).toEqual(item('b', 4, 0, 6, 5));
  });

  it('stops at the right edge, a minimum size and a maximum height', () => {
    expect(byId(resize(start(), 'b', 50, 3), 'b').w).toBe(8); // from column 4 to the edge
    expect(byId(resize(start(), 'b', 0, 0), 'b')).toMatchObject({ w: 2, h: 2 });
    expect(byId(resize(start(), 'b', 4, 99), 'b').h).toBe(20);
  });

  it('pushes the widgets below down when one grows over them', () => {
    const next = resize(start(), 'a', 4, 6);

    expect(overlaps(next)).toBe(false);
    expect(byId(next, 'c').y).toBeGreaterThanOrEqual(6);
  });

  it('pulls the widgets below up again when one shrinks', () => {
    const next = resize([item('a', 0, 0, 4, 6), item('c', 0, 6, 4, 2)], 'a', 4, 2);

    expect(byId(next, 'c').y).toBe(2);
  });
});

describe('readingOrder and sameLayout', () => {
  it('orders widgets top to bottom, then left to right', () => {
    const order = readingOrder([item('c', 0, 4, 12, 3), item('b', 6, 0, 6, 4), item('a', 0, 0, 6, 4)]);

    expect([order.get('a'), order.get('b'), order.get('c')]).toEqual([0, 1, 2]);
  });

  it('compares layouts by place, not by order', () => {
    const a = [item('a', 0, 0, 4, 3), item('b', 4, 0, 4, 3)];

    expect(sameLayout(a, [a[1], a[0]])).toBe(true);
    expect(sameLayout(a, [a[0], item('b', 5, 0, 4, 3)])).toBe(false);
    expect(sameLayout(a, [a[0]])).toBe(false);
  });
});
