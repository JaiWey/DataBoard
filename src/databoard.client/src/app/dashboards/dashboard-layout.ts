/**
 * The grid a dashboard is laid out on: 12 columns, rows of a fixed height. Everything here is plain functions over
 * lists of rectangles, so moving and resizing can be tested without a browser. Rectangles are in grid cells.
 */
export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface LayoutItem extends Rect {
  id: string;
}

export const GRID_COLUMNS = 12;
export const DEFAULT_WIDTH = 6;
export const DEFAULT_HEIGHT = 5;
export const MIN_WIDTH = 2;
export const MIN_HEIGHT = 2;
export const MAX_HEIGHT = 20;

const clamp = (value: number, min: number, max: number): number => Math.min(Math.max(value, min), max);

const collides = (a: Rect, b: Rect): boolean => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

const copy = (items: LayoutItem[]): LayoutItem[] => items.map((i) => ({ ...i }));

/** Keeps a rectangle inside the grid and within the sizes the server accepts. */
export function fit(rect: Rect): Rect {
  const w = clamp(Math.round(rect.w), 1, GRID_COLUMNS);
  const h = clamp(Math.round(rect.h), 1, MAX_HEIGHT);
  return { x: clamp(Math.round(rect.x), 0, GRID_COLUMNS - w), y: Math.max(0, Math.round(rect.y)), w, h };
}

/**
 * Gives every widget a place. Widgets that already have one keep it; the others (dashboards saved before layouts
 * existed, or a widget that was just added) go into the first free spot, scanning row by row. The result has no overlaps.
 */
export function arrange(widgets: { id: string; layout?: Rect | null }[]): LayoutItem[] {
  const placed: LayoutItem[] = [];
  const unplaced: string[] = [];

  for (const w of widgets) {
    if (w.layout) placed.push({ id: w.id, ...fit(w.layout) });
    else unplaced.push(w.id);
  }

  for (const id of unplaced) {
    const size = { w: DEFAULT_WIDTH, h: DEFAULT_HEIGHT };
    let spot: Rect | undefined;
    for (let y = 0; !spot; y++) {
      for (let x = 0; x + size.w <= GRID_COLUMNS && !spot; x++) {
        const candidate = { x, y, ...size };
        if (!placed.some((p) => collides(p, candidate))) spot = candidate;
      }
    }
    placed.push({ id, ...spot });
  }

  // Keep the order of the dashboard's widgets, and make sure stored overlaps cannot survive.
  const order = new Map(widgets.map((w, i) => [w.id, i]));
  return compact(resolveAll(placed)).sort((a, b) => order.get(a.id)! - order.get(b.id)!);
}

/** Pushes whatever overlaps the item with this id down, and what that pushes down in turn. */
function pushApart(items: LayoutItem[], id: string): void {
  const queue = [id];
  while (queue.length > 0) {
    const currentId = queue.pop();
    const current = items.find((i) => i.id === currentId)!;
    for (const other of items) {
      if (other === current || !collides(current, other)) continue;
      other.y = current.y + current.h;
      queue.push(other.id);
    }
  }
}

function resolveAll(items: LayoutItem[]): LayoutItem[] {
  const result = copy(items).sort((a, b) => a.y - b.y || a.x - b.x);
  for (const item of result) pushApart(result, item.id);
  return result;
}

/** Lets every widget rise as far as it can, so a dashboard has no holes above its widgets. */
export function compact(items: LayoutItem[]): LayoutItem[] {
  const sorted = copy(items).sort((a, b) => a.y - b.y || a.x - b.x);
  const settled: LayoutItem[] = [];
  for (const item of sorted) {
    while (item.y > 0 && !settled.some((s) => collides(s, { ...item, y: item.y - 1 }))) item.y--;
    settled.push(item);
  }
  return settled;
}

/** Moves a widget so its top-left corner is at the given cell; the widgets in its way are pushed down. */
export function move(items: LayoutItem[], id: string, x: number, y: number): LayoutItem[] {
  const next = copy(items);
  const item = next.find((i) => i.id === id);
  if (!item) return next;
  Object.assign(item, fit({ ...item, x, y }));
  pushApart(next, id);
  return keepOrder(compact(next), items);
}

/** Gives a widget a new size (its corner stays where it is); the widgets in its way are pushed down. */
export function resize(items: LayoutItem[], id: string, w: number, h: number): LayoutItem[] {
  const next = copy(items);
  const item = next.find((i) => i.id === id);
  if (!item) return next;
  Object.assign(item, fit({ ...item, w: clamp(w, MIN_WIDTH, GRID_COLUMNS - item.x), h: clamp(h, MIN_HEIGHT, MAX_HEIGHT) }));
  pushApart(next, id);
  return keepOrder(compact(next), items);
}

const keepOrder = (result: LayoutItem[], original: LayoutItem[]): LayoutItem[] => {
  const order = new Map(original.map((i, n) => [i.id, n]));
  return result.sort((a, b) => order.get(a.id)! - order.get(b.id)!);
};

/** Reading order (top to bottom, then left to right): how a narrow screen stacks the widgets. */
export const readingOrder = (items: LayoutItem[]): Map<string, number> =>
  new Map([...items].sort((a, b) => a.y - b.y || a.x - b.x).map((item, index) => [item.id, index]));

export const sameLayout = (a: LayoutItem[], b: LayoutItem[]): boolean =>
  a.length === b.length && a.every((i) => {
    const o = b.find((x) => x.id === i.id);
    return !!o && o.x === i.x && o.y === i.y && o.w === i.w && o.h === i.h;
  });
