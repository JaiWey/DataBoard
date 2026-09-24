import { LayoutItem, arrange } from './dashboard-layout';
import { DashboardTab, DashboardWidget } from './dashboards.service';

/** A dashboard can have this many tabs. Mirrors the server's limit. */
export const MAX_TABS = 10;
export const MAX_TAB_NAME_LENGTH = 60;

/**
 * The tab a widget is on: its own if that tab exists, else the first (a widget saved before tabs, or on a tab that was
 * taken away). Null when the dashboard has no tabs, which is one page.
 */
export function tabOf(widget: Pick<DashboardWidget, 'tabId'>, tabs: DashboardTab[]): string | null {
  if (tabs.length === 0) return null;
  return tabs.some((t) => t.id === widget.tabId) ? (widget.tabId as string) : tabs[0].id;
}

/** The widgets on one tab (all of them, when there are no tabs), in the dashboard's order. */
export const widgetsOn = (widgets: DashboardWidget[], tabs: DashboardTab[], tabId: string | null): DashboardWidget[] =>
  widgets.filter((w) => tabOf(w, tabs) === tabId);

/** The places of the widgets of one tab, with places made up for those that have none. Each tab has its own grid. */
export const arrangeTab = (widgets: DashboardWidget[], tabs: DashboardTab[], tabId: string | null): LayoutItem[] =>
  arrange(widgetsOn(widgets, tabs, tabId));

/** The row under everything on a tab: where something new goes. */
export const bottomOf = (items: LayoutItem[]): number => items.reduce((max, i) => Math.max(max, i.y + i.h), 0);

/**
 * Writes the places the page worked out into the widgets, tab by tab (the grids of two tabs may use the same cells, so
 * they must never be arranged together), so they stay where they are when others come and go.
 */
export function placeAll(widgets: DashboardWidget[], tabs: DashboardTab[]): DashboardWidget[] {
  const groups: (string | null)[] = tabs.length === 0 ? [null] : tabs.map((t) => t.id);
  const places = new Map<string, LayoutItem>();
  for (const group of groups) for (const item of arrangeTab(widgets, tabs, group)) places.set(item.id, item);

  return widgets.map((w) => {
    const place = places.get(w.id);
    return place ? { ...w, layout: { x: place.x, y: place.y, w: place.w, h: place.h } } : w;
  });
}
