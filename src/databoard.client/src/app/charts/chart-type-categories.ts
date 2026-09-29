import { CHART_TYPES, ChartType } from './charts.service';

/** A group of chart types shown together under one tab of the type picker. */
export interface ChartCategory {
  id: string;
  label: string;
  types: ChartType[];
}

export const ALL_CATEGORY = 'all';
export const RECENT_CATEGORY = 'recent';

/**
 * The tabs of the type picker, in order: everything, what was used lately, then the kinds of chart. Each kind lists the
 * types that belong to it (a donut is a pie chart, an area chart a line chart).
 */
export const CHART_CATEGORIES: ChartCategory[] = [
  { id: ALL_CATEGORY, label: 'All', types: CHART_TYPES.map((t) => t.value) },
  { id: RECENT_CATEGORY, label: 'Recent', types: [] }, // filled from what was picked
  { id: 'table', label: 'Table', types: ['Table'] },
  { id: 'bar', label: 'Bar Chart', types: ['Bar', 'BarHorizontal'] },
  { id: 'line', label: 'Line Chart', types: ['Line', 'Area'] },
  { id: 'pie', label: 'Pie Chart', types: ['Pie', 'Donut'] },
  { id: 'scatter', label: 'Scatter Chart', types: ['Scatter'] },
  { id: 'heatmap', label: 'Heatmap', types: ['Heatmap'] },
  { id: 'score', label: 'Score Card', types: ['Number'] },
  { id: 'wordcloud', label: 'Word Cloud', types: ['WordCloud'] },
];

/** How many recent types are remembered. */
export const MAX_RECENT = 6;

const RECENT_KEY = 'databoard.recentChartTypes';
const TAB_KEY = 'databoard.chartTypeTab';

const known = (value: unknown): value is ChartType => CHART_TYPES.some((t) => t.value === value);

/** The types a tab shows: its own, or for Recent what was picked lately (newest first). */
export function typesIn(categoryId: string, recent: ChartType[]): ChartType[] {
  if (categoryId === RECENT_CATEGORY) return recent.filter(known);
  return CHART_CATEGORIES.find((c) => c.id === categoryId)?.types ?? CHART_CATEGORIES[0].types;
}

/** The recent list after picking a type: it goes first, is not listed twice, and the list stays short. */
export function withRecent(recent: ChartType[], picked: ChartType): ChartType[] {
  return [picked, ...recent.filter((t) => t !== picked)].slice(0, MAX_RECENT);
}

// What is remembered lives in the browser, per person. It may be unavailable (private windows, blocked site data), so
// every read and write guards against that and the picker simply starts empty.

export function readRecent(): ChartType[] {
  try {
    const saved = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]') as unknown;
    return Array.isArray(saved) ? saved.filter(known).slice(0, MAX_RECENT) : [];
  } catch {
    return [];
  }
}

export function writeRecent(recent: ChartType[]): void {
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent));
  } catch {
    // Not remembered.
  }
}

export function readTab(): string {
  try {
    const saved = localStorage.getItem(TAB_KEY);
    return CHART_CATEGORIES.some((c) => c.id === saved) ? (saved as string) : ALL_CATEGORY;
  } catch {
    return ALL_CATEGORY;
  }
}

export function writeTab(id: string): void {
  try {
    localStorage.setItem(TAB_KEY, id);
  } catch {
    // Not remembered.
  }
}
