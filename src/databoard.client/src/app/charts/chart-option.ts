import type { EChartsCoreOption } from 'echarts/core';

/** The kinds ECharts draws. Table and Number are not charts in this sense and are shown by `ChartViewComponent`. */
export type ChartKind = 'Bar' | 'Line' | 'Pie' | 'Area' | 'BarHorizontal' | 'Donut' | 'Heatmap' | 'Scatter';

/** How a saved chart is drawn. Every field is optional so a chart without a style still draws. */
export interface ChartStyleOptions {
  legend?: boolean;
  labels?: boolean;
  palette?: string;
  /** Series are piled on top of each other (bars, lines and areas). */
  stack?: boolean;
  /** Stacked, and each category scaled to 100%: the chart shows shares, not amounts. */
  percent?: boolean;
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

  if (kind === 'Heatmap') return heatmapOption(result, dims, style, common);
  if (kind === 'Scatter') return scatterOption(result, dims, style, common);

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
  const percent = style.percent === true;
  const stacked = style.stack === true || percent;

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

  if (percent) {
    // Each category becomes 100: a series' value is its share of what all the series add up to there.
    const totals = categories.map((_, i) => series.reduce((sum, s) => sum + Math.max(0, s.data[i] ?? 0), 0));
    series = series.map((s) => ({ ...s, data: s.data.map((v, i) => (v === null || totals[i] === 0 ? null : (Math.max(0, v) / totals[i]) * 100)) }));
  }
  const percentLabel = (value: unknown): string => (typeof value === 'number' ? `${Math.round(value * 10) / 10}%` : label(value));

  const chartSeries = series.map((s) => ({
    type,
    name: s.name,
    data: s.data,
    ...(kind === 'Area' ? { areaStyle: {} } : {}),
    ...(stacked ? { stack: 'total' } : {}),
    ...(series.length > 1 ? { emphasis: { focus: 'series' } } : {}),
    label: {
      show: showLabels,
      position: horizontal ? (stacked ? 'inside' : 'right') : stacked ? 'inside' : 'top',
      ...(percent ? { formatter: (p: { value: unknown }) => percentLabel(p.value) } : {}),
    },
  }));
  const valueAxis = percent ? { type: 'value', max: 100, axisLabel: { formatter: '{value}%' } } : { type: 'value' };

  const showLegend = series.length > 1 && style.legend !== false;
  const categoryAxis = { type: 'category', data: categories, axisLabel: { hideOverlap: true }, ...(horizontal ? { inverse: true } : {}) };

  return {
    ...common,
    ...(percent ? { tooltip: { trigger: 'axis', valueFormatter: percentLabel } } : {}),
    grid: { left: 16, right: horizontal && showLabels && !stacked ? 40 : 16, top: 24, bottom: showLegend ? 40 : 16, containLabel: true },
    legend: showLegend ? (series.length > LEGEND_SCROLL_FROM ? { type: 'scroll', bottom: 0 } : { bottom: 0 }) : undefined,
    xAxis: horizontal ? valueAxis : categoryAxis,
    yAxis: horizontal ? categoryAxis : valueAxis,
    series: chartSeries,
  };
}

type Common = Record<string, unknown>;

/** Cells of a grid: the first dimension across, the second down, the metric as the colour. */
function heatmapOption(result: ResultShape, dims: number, style: ChartStyleOptions, common: Common): EChartsCoreOption {
  const { columns, rows } = result;
  if (dims < 2 || columns.length < 3) return { series: [] };

  const across: string[] = [];
  const down: string[] = [];
  const position = (list: string[], name: string): number => {
    const found = list.indexOf(name);
    if (found >= 0) return found;
    list.push(name);
    return list.length - 1;
  };

  const cells: [number, number, number][] = [];
  for (const row of rows) {
    const value = toNumber(row[2]);
    // A missing combination is an empty cell; the axes still list the value.
    const x = position(across, label(row[0]));
    const y = position(down, label(row[1]));
    if (value !== null) cells.push([x, y, value]);
  }
  if (cells.length === 0) return { series: [] };

  const values = cells.map((c) => c[2]);
  const color = PALETTES[style.palette ?? 'default'];

  return {
    ...common,
    tooltip: {
      trigger: 'item',
      formatter: (p: { value: [number, number, number] }) =>
        `${across[p.value[0]]} · ${down[p.value[1]]}<br/>${columns[2].name}: <b>${p.value[2].toLocaleString()}</b>`,
    },
    grid: { left: 16, right: 16, top: 16, bottom: 56, containLabel: true },
    xAxis: { type: 'category', data: across, splitArea: { show: true }, axisLabel: { hideOverlap: true } },
    yAxis: { type: 'category', data: down, inverse: true, splitArea: { show: true } },
    visualMap: {
      min: Math.min(...values),
      max: Math.max(...values),
      calculable: true,
      orient: 'horizontal',
      left: 'center',
      bottom: 0,
      text: [columns[2].name, ''],
      inRange: { color: ['#e7f0fa', color?.[0] ?? '#1971c2'] },
    },
    series: [{
      type: 'heatmap',
      name: columns[2].name,
      data: cells,
      label: { show: style.labels === true },
      emphasis: { itemStyle: { borderColor: '#333', borderWidth: 1 } },
    }],
  };
}

/** Points: one dimension names each point (a second splits them into series), then X, Y and an optional size. */
function scatterOption(result: ResultShape, dims: number, style: ChartStyleOptions, common: Common): EChartsCoreOption {
  const { columns, rows } = result;
  const nameDims = Math.min(Math.max(dims, 1), 2);
  const x = nameDims;
  if (columns.length < x + 2) return { series: [] };
  const hasSize = columns.length > x + 2;

  const sizes = hasSize ? rows.map((r) => toNumber(r[x + 2])).filter((s): s is number => s !== null && s > 0) : [];
  const biggest = sizes.length > 0 ? Math.max(...sizes) : 0;
  // Area, not width, grows with the value, so a point twice as big does not look four times as big.
  const symbolSize = hasSize
    ? (value: (number | null)[]) => (value[2] && biggest > 0 ? 8 + 40 * Math.sqrt(Math.max(0, value[2]) / biggest) : 8)
    : 10;

  const groups = new Map<string, { name: string; value: (number | null)[] }[]>();
  for (const row of rows) {
    const px = toNumber(row[x]);
    const py = toNumber(row[x + 1]);
    if (px === null || py === null) continue; // a point needs both
    const group = nameDims === 2 ? label(row[1]) : columns[x + 1].name;
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group)!.push({ name: label(row[0]), value: hasSize ? [px, py, toNumber(row[x + 2])] : [px, py] });
  }
  if (groups.size === 0) return { series: [] };

  const showLegend = groups.size > 1 && style.legend !== false;
  return {
    ...common,
    tooltip: {
      trigger: 'item',
      formatter: (p: { name: string; seriesName: string; value: (number | null)[] }) =>
        [
          `<b>${p.name}</b>${groups.size > 1 ? ` (${p.seriesName})` : ''}`,
          `${columns[x].name}: ${p.value[0]?.toLocaleString()}`,
          `${columns[x + 1].name}: ${p.value[1]?.toLocaleString()}`,
          ...(hasSize ? [`${columns[x + 2].name}: ${p.value[2]?.toLocaleString() ?? '∅'}`] : []),
        ].join('<br/>'),
    },
    grid: { left: 16, right: 24, top: 24, bottom: showLegend ? 56 : 32, containLabel: true },
    legend: showLegend ? (groups.size > LEGEND_SCROLL_FROM ? { type: 'scroll', bottom: 0 } : { bottom: 0 }) : undefined,
    // The axes say what they are: there are no categories to read them from.
    xAxis: { type: 'value', name: columns[x].name, nameLocation: 'middle', nameGap: 28, scale: true },
    yAxis: { type: 'value', name: columns[x + 1].name, scale: true },
    series: [...groups].map(([name, data]) => ({
      type: 'scatter',
      name,
      data,
      symbolSize,
      ...(groups.size > 1 ? { emphasis: { focus: 'series' } } : {}),
      label: { show: style.labels === true, formatter: (p: { name: string }) => p.name, position: 'top' },
    })),
  };
}
