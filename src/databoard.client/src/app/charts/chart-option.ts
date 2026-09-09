import type { EChartsCoreOption } from 'echarts/core';

/** The kinds ECharts draws. Table and Number are not charts in this sense and are shown by `ChartViewComponent`. */
export type ChartKind = 'Bar' | 'Line' | 'Pie' | 'Area' | 'BarHorizontal' | 'Donut';

/** How a saved chart is drawn. Every field is optional so a chart without a style still draws. */
export interface ChartStyleOptions {
  legend?: boolean;
  labels?: boolean;
  palette?: string;
  /** Series are piled on top of each other (bars, lines and areas). */
  stack?: boolean;
}

/** Colour sets a chart can choose from. "default" leaves ECharts' own colours alone. */
export const PALETTES: Record<string, string[] | undefined> = {
  default: undefined,
  warm: ['#e8590c', '#f59f00', '#d6336c', '#c92a2a', '#e67700', '#a61e4d'],
  cool: ['#1971c2', '#0c8599', '#5f3dc4', '#2b8a3e', '#1098ad', '#364fc7'],
  mono: ['#343a40', '#6c757d', '#adb5bd', '#495057', '#868e96', '#ced4da'],
};

/** The part of a query result that drawing needs. Compatible with the API's ChartResult. */
export interface ResultShape {
  columns: { name: string }[];
  rows: unknown[][];
}

/** More series than this and the legend scrolls instead of growing. */
const LEGEND_SCROLL_FROM = 8;

const label = (value: unknown): string => (value === null || value === undefined ? '∅' : String(value));

const toNumber = (value: unknown): number | null => {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
};

/**
 * Turns a query result into an ECharts option.
 *
 * A result is its dimension columns first, then its metric columns. `dimensionCount` says how many are dimensions:
 * - 0: one row of metrics, drawn as one value each;
 * - 1: the dimension is the category and every metric is a series;
 * - 2 or more: the first dimension is the X axis and the others split the data, one series per value (per metric).
 * When it is not given, a single column counts as 0 dimensions and anything else as 1, as before.
 */
export function buildChartOption(
  kind: ChartKind,
  result: ResultShape,
  style: ChartStyleOptions = {},
  dimensionCount?: number,
): EChartsCoreOption {
  const { columns, rows } = result;
  if (columns.length === 0 || rows.length === 0) return { series: [] };

  const dims = Math.min(dimensionCount ?? (columns.length === 1 ? 0 : 1), columns.length);
  const round = kind === 'Pie' || kind === 'Donut';
  const showLabels = style.labels === true;
  const color = PALETTES[style.palette ?? 'default'];
  const common = {
    aria: { enabled: true },
    tooltip: { trigger: round ? 'item' : 'axis' },
    ...(color ? { color } : {}),
  };
  const dimensionName = (row: unknown[], from: number, to: number): string => row.slice(from, to).map(label).join(' / ');

  if (round) {
    // A pie shows one metric, as slices of the whole; negative or empty values cannot be drawn as slices.
    const valueColumn = Math.min(dims, columns.length - 1);
    const data = rows
      .map((row) => ({ name: dims > 0 ? dimensionName(row, 0, dims) : columns[valueColumn].name, value: toNumber(row[valueColumn]) }))
      .filter((d): d is { name: string; value: number } => d.value !== null && d.value > 0);
    return {
      ...common,
      legend: style.legend === false ? undefined : { type: 'scroll', bottom: 0 },
      series: [{
        type: 'pie',
        name: columns[valueColumn].name,
        radius: kind === 'Donut' ? ['45%', '70%'] : ['0%', '70%'],
        label: { show: showLabels },
        data,
      }],
    };
  }

  const horizontal = kind === 'BarHorizontal';
  const type = kind === 'Line' || kind === 'Area' ? 'line' : 'bar';
  const stacked = style.stack === true;

  let categories: string[];
  let series: { name: string; data: (number | null)[] }[];

  if (dims === 0) {
    // One row of metrics: each metric is one value along the axis.
    categories = columns.map((c) => c.name);
    series = [{ name: columns.length === 1 ? columns[0].name : 'value', data: rows[0].map(toNumber) }];
  } else if (dims === 1) {
    categories = rows.map((row) => label(row[0]));
    series = columns.slice(1).map((c, i) => ({ name: c.name, data: rows.map((row) => toNumber(row[i + 1])) }));
  } else {
    // Pivot: the first dimension is the X axis; every distinct value of the others becomes a series.
    const xOrder = new Map<string, number>();
    const groups = new Map<string, (number | null)[][]>(); // group name -> per metric, per x position
    const metricColumns = columns.slice(dims);

    for (const row of rows) {
      const x = label(row[0]);
      if (!xOrder.has(x)) xOrder.set(x, xOrder.size);
      const group = dimensionName(row, 1, dims);
      if (!groups.has(group)) groups.set(group, metricColumns.map(() => []));
      const perMetric = groups.get(group)!;
      metricColumns.forEach((_, m) => {
        perMetric[m][xOrder.get(x)!] = toNumber(row[dims + m]);
      });
    }

    categories = [...xOrder.keys()];
    series = [...groups].flatMap(([group, perMetric]) =>
      perMetric.map((values, m) => ({
        name: metricColumns.length === 1 ? group : `${group} · ${metricColumns[m].name}`,
        // A missing combination is a gap (the arrays above are sparse).
        data: categories.map((_, i) => values[i] ?? null),
      })),
    );
  }

  const chartSeries = series.map((s) => ({
    type,
    name: s.name,
    data: s.data,
    ...(kind === 'Area' ? { areaStyle: {} } : {}),
    ...(stacked ? { stack: 'total' } : {}),
    ...(series.length > 1 ? { emphasis: { focus: 'series' } } : {}),
    label: { show: showLabels, position: horizontal ? 'right' : stacked ? 'inside' : 'top' },
  }));

  const showLegend = series.length > 1 && style.legend !== false;
  const categoryAxis = { type: 'category', data: categories, axisLabel: { hideOverlap: true }, ...(horizontal ? { inverse: true } : {}) };

  return {
    ...common,
    grid: { left: 16, right: horizontal && showLabels ? 40 : 16, top: 24, bottom: showLegend ? 40 : 16, containLabel: true },
    legend: showLegend ? (series.length > LEGEND_SCROLL_FROM ? { type: 'scroll', bottom: 0 } : { bottom: 0 }) : undefined,
    xAxis: horizontal ? { type: 'value' } : categoryAxis,
    yAxis: horizontal ? categoryAxis : { type: 'value' },
    series: chartSeries,
  };
}
