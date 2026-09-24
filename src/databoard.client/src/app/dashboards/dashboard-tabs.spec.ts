import { arrangeTab, bottomOf, placeAll, tabOf, widgetsOn } from './dashboard-tabs';
import { DashboardTab, DashboardWidget } from './dashboards.service';

const tabs: DashboardTab[] = [{ id: 't1', name: 'One' }, { id: 't2', name: 'Two' }];
const widget = (id: string, tabId?: string | null, layout?: DashboardWidget['layout']): DashboardWidget => ({ id, chartId: `c-${id}`, tabId, layout });

describe('tabOf', () => {
  it('is nothing when there are no tabs, whatever a widget says', () => {
    expect(tabOf({ tabId: 't1' }, [])).toBeNull();
    expect(tabOf({}, [])).toBeNull();
  });

  it('is the tab a widget is on, if there is one such tab', () => {
    expect(tabOf({ tabId: 't2' }, tabs)).toBe('t2');
  });

  it('is the first tab for a widget with no tab or a tab that is gone', () => {
    expect(tabOf({}, tabs)).toBe('t1');
    expect(tabOf({ tabId: null }, tabs)).toBe('t1');
    expect(tabOf({ tabId: 'gone' }, tabs)).toBe('t1');
  });
});

describe('widgetsOn', () => {
  const widgets = [widget('a', 't1'), widget('b', 't2'), widget('c'), widget('d', 'gone')];

  it('lists the widgets of a tab in the dashboard order, with the homeless ones on the first', () => {
    expect(widgetsOn(widgets, tabs, 't1').map((w) => w.id)).toEqual(['a', 'c', 'd']);
    expect(widgetsOn(widgets, tabs, 't2').map((w) => w.id)).toEqual(['b']);
  });

  it('lists all of them when there are no tabs', () => {
    expect(widgetsOn(widgets, [], null).map((w) => w.id)).toEqual(['a', 'b', 'c', 'd']);
  });
});

describe('arrangeTab and bottomOf', () => {
  it('lays out one tab on its own, so another tab using the same cells does not push it', () => {
    const widgets = [widget('a', 't1', { x: 0, y: 0, w: 6, h: 5 }), widget('b', 't2', { x: 0, y: 0, w: 6, h: 5 })];

    expect(arrangeTab(widgets, tabs, 't1')).toEqual([{ id: 'a', x: 0, y: 0, w: 6, h: 5 }]);
    expect(arrangeTab(widgets, tabs, 't2')).toEqual([{ id: 'b', x: 0, y: 0, w: 6, h: 5 }]);
  });

  it('finds the row under everything on a tab', () => {
    const widgets = [widget('a', 't1', { x: 0, y: 0, w: 6, h: 5 }), widget('b', 't1', { x: 6, y: 2, w: 6, h: 7 })];

    expect(bottomOf(arrangeTab(widgets, tabs, 't1'))).toBe(7); // the second rises to the top beside the first: it is 7 high
    expect(bottomOf([])).toBe(0);
  });
});

describe('placeAll', () => {
  it('writes a place into every widget, tab by tab, without letting one tab push another', () => {
    const widgets = [
      widget('a', 't1', { x: 0, y: 0, w: 6, h: 5 }),
      widget('b', 't2', { x: 0, y: 0, w: 6, h: 5 }),
      widget('c', 't1'), // no place yet
    ];

    const placed = placeAll(widgets, tabs);

    expect(placed.map((w) => [w.id, w.layout])).toEqual([
      ['a', { x: 0, y: 0, w: 6, h: 5 }],
      ['b', { x: 0, y: 0, w: 6, h: 5 }],
      ['c', { x: 6, y: 0, w: 6, h: 5 }],
    ]);
  });

  it('keeps what else a widget carries', () => {
    const placed = placeAll([{ id: 'x', chartId: 'c', kind: 'Text', text: 'Hi', tabId: 't2' }], tabs);

    expect(placed[0]).toMatchObject({ kind: 'Text', text: 'Hi', tabId: 't2', layout: { x: 0, y: 0 } });
  });

  it('arranges everything together when there are no tabs', () => {
    const placed = placeAll([widget('a', null, { x: 0, y: 0, w: 6, h: 5 }), widget('b', null, { x: 0, y: 0, w: 6, h: 5 })], []);

    expect(placed[1].layout!.y).toBe(5); // the second is pushed below the first
  });
});
