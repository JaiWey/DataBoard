import type { EChartsCoreOption } from 'echarts/core';
import { NumberFormatOptions, formatNumber } from './number-format';

/** The kinds ECharts draws. Table and Number are not charts in this sense and are shown by `ChartViewComponent`. */
export type ChartKind = 'Bar' | 'Line' | 'Pie' | 'Area' | 'BarHorizontal' | 'Donut' | 'Heatmap' | 'Scatter';

export type LegendPosition = 'top' | 'bottom' | 'left' | 'right';
export type PieLabel = 'name' | 'value' | 'percent' | 'name-value' | 'name-percent';

/** How a saved chart is drawn. Every field is optional so a chart without a style still draws. */
export interface ChartStyleOptions {
  legend?: boolean;
  labels?: boolean;
  palette?: string;
  /** Series are piled on top of each other (bars, lines and areas). */
  stack?: boolean;
  /** Stacked, and each category scaled to 100%: the chart shows shares, not amounts. */
  percent?: boolean;

  // Titles and axes
  title?: string | null;
  xAxisTitle?: string | null;
  yAxisTitle?: string | null;
  grid?: boolean;
  yMin?: number | null;
  yMax?: number | null;
  labelRotate?: number;

  // Colours
  /** Own colours, used instead of the palette. */
  colors?: string[] | null;
  /** A colour for a series (or slice) by name. */
  seriesColors?: Record<string, string> | null;

  numberFormat?: NumberFormatOptions | null;

  // The look of series
  legendPosition?: LegendPosition;
  smooth?: boolean;
  lineWidth?: number;
  symbols?: boolean;
  barWidth?: number | null;
  barRadius?: number;
  areaOpacity?: number;

  // Pie and donut
  /** The hole of a donut, as a percentage of the radius. Missing uses the type's own (0 for a pie, 45 for a donut). */
  innerRadius?: number | null;
  rose?: boolean;
  pieLabel?: PieLabel;
}

/** Colour sets a chart can choose from. "default" leaves ECharts' own colours alone. */
export const PALETTES: Record<string, string[] | undefined> = {
  default: undefined,
  warm: ['#e8590c', '#f59f00', '#d6336c', '#c92a2a', '#e67700', '#a61e4d'],
  cool: ['#1971c2', '#0c8599', '#5f3dc4', '#2b8a3e', '#1098ad', '#364fc7'],
  mono: ['#343a40', '#6c757d', '#adb5bd', '#495057', '#868e96', '#ced4da'],
};

/** The colours a chart uses: its own list if it has one, else its palette (undefined leaves ECharts' own). */
export const colorsOf = (style: ChartStyleOptions): string[] | undefined =>
  style.colors && style.colors.length > 0 ? style.colors : PALETTES[style.palette ?? 'default'];

/** The part of a query result that drawing needs. Compatible with the API's ChartResult. */
export interface ResultShape {
  columns: { name: string }[];
  rows: unknown[][];
}

/** More series than this and the legend scrolls instead of growing. */
const LEGEND_SCROLL_FROM = 8;

/** Room a title, an axis title or a side legend takes, in pixels. */
const TITLE_SPACE = 28;
const AXIS_TITLE_SPACE = 24;
const SIDE_LEGEND_SPACE = 110;

const label = (value: unknown): string => (value === null || value === undefined ? '∅' : String(value));

const toNumber = (value: unknown): number | null => {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
};

const hasTitle = (style: ChartStyleOptions): boolean => !!style.title?.trim();
const titleOption = (style: ChartStyleOptions) =>
  hasTitle(style) ? { title: { text: style.title!.trim(), left: 'center', top: 0, textStyle: { fontSize: 14 } } } : {};

/** Writes a number in the chart's format, for labels, axes and tooltips. */
const fmt = (style: ChartStyleOptions) => (value: unknown): string =>
  typeof value === 'number' && Number.isFinite(value) ? formatNumber(value, style.numberFormat) : label(value);

/** Where the legend goes and how it looks. */
function legendOption(style: ChartStyleOptions, count: number, round = false): Record<string, unknown> {
  const position = style.legendPosition ?? 'bottom';
  const scroll = round || count > LEGEND_SCROLL_FROM ? { type: 'scroll' } : {};
  switch (position) {
    case 'top': return { ...scroll, top: hasTitle(style) ? TITLE_SPACE : 0 };
    case 'left': return { ...scroll, orient: 'vertical', left: 0, top: 'middle' };
    case 'right': return { ...scroll, orient: 'vertical', right: 0, top: 'middle' };
    default: return { ...scroll, bottom: 0 };
  }
}

/** The plot's margins: the base ones plus what the title, the axis titles and the legend take. */
function gridOption(
  style: ChartStyleOptions, showLegend: boolean, base: { left: number; right: number; top: number; bottom: number },
): Record<string, unknown> {
  const position = style.legendPosition ?? 'bottom';
  const grid = { ...base };
  if (hasTitle(style)) grid.top += TITLE_SPACE;
  if (style.yAxisTitle?.trim()) grid.top += AXIS_TITLE_SPACE - 4;
  if (style.xAxisTitle?.trim()) grid.bottom += AXIS_TITLE_SPACE;
  if (showLegend) {
    if (position === 'top') grid.top += TITLE_SPACE;
    else if (position === 'left') grid.left += SIDE_LEGEND_SPACE;
    else if (position === 'right') grid.right += SIDE_LEGEND_SPACE;
  }
  return { ...grid, containLabel: true };
}

/** The axis's title, drawn under (horizontal axis) or above (vertical axis) it. */
const axisName = (title: string | null | undefined, horizontal: boolean) =>
  title?.trim()
    ? horizontal
      ? { name: title.trim(), nameLocation: 'middle', nameGap: 30 }
      : { name: title.trim(), nameLocation: 'end', nameTextStyle: { align: 'left' } }
    : {};

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
  const color = colorsOf(style);
  const own = (name: string) => style.seriesColors?.[name];
  const common = {
    aria: { enabled: true },
    tooltip: { trigger: round ? 'item' : 'axis' },
    ...(color ? { color } : {}),
    ...titleOption(style),
  };
  const dimensionName = (row: unknown[], from: number, to: number): string => row.slice(from, to).map(label).join(' / ');

  if (kind === 'Heatmap') return heatmapOption(result, dims, style, common);
  if (kind === 'Scatter') return scatterOption(result, dims, style, common);

  if (round) {
    // A pie shows one metric, as slices of the whole; negative or empty values cannot be drawn as slices.
    const valueColumn = Math.min(dims, columns.length - 1);
    const data = rows
      .map((row) => ({ name: dims > 0 ? dimensionName(row, 0, dims) : columns[valueColumn].name, value: toNumber(row[valueColumn]) }))
      .filter((d): d is { name: string; value: number } => d.value !== null && d.value > 0)
      .map((d) => (own(d.name) ? { ...d, itemStyle: { color: own(d.name) } } : d));
    const inner = style.innerRadius ?? (kind === 'Donut' ? 45 : 0);
    const text = fmt(style);
    const slice = (p: { name: string; value: number; percent: number }): string => {
      const percent = `${Math.round(p.percent * 10) / 10}%`;
      switch (style.pieLabel ?? 'name') {
        case 'value': return text(p.value);
        case 'percent': return percent;
        case 'name-value': return `${p.name}: ${text(p.value)}`;
        case 'name-percent': return `${p.name} (${percent})`;
        default: return p.name;
      }
    };
    return {
      ...common,
      tooltip: {
        trigger: 'item',
        formatter: (p: { name: string; value: number; percent: number }) => `${p.name}: <b>${text(p.value)}</b> (${Math.round(p.percent * 10) / 10}%)`,
      },
      legend: style.legend === false ? undefined : legendOption(style, data.length, true),
      series: [{
        type: 'pie',
        name: columns[valueColumn].name,
        // Labels sit outside the circle, so with labels it is smaller to leave them room.
        radius: [`${inner}%`, `${showLabels ? 54 : 70}%`],
        ...(style.rose ? { roseType: 'radius' } : {}),
        // The legend and title take room from the circle, which is centred in what is left.
        ...(pieCentre(style, style.legend !== false)),
        label: { show: showLabels, formatter: slice },
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
  // What a value reads as: a share, or a number in the chart's format.
  const text = percent ? percentLabel : fmt(style);
  const formatted = percent || !!style.numberFormat;

  const radius = style.barRadius ?? 0;
  const rounded = type === 'bar' && radius > 0 ? { borderRadius: horizontal ? [0, radius, radius, 0] : [radius, radius, 0, 0] } : {};
  const chartSeries = series.map((s) => {
    const fill = own(s.name);
    const itemStyle = { ...rounded, ...(fill ? { color: fill } : {}) };
    return {
      type,
      name: s.name,
      data: s.data,
      ...(Object.keys(itemStyle).length > 0 ? { itemStyle } : {}),
      // The width is what the bars of one slot take together, so side by side they share it.
      ...(type === 'bar' && style.barWidth ? { barWidth: `${Math.round((style.barWidth / (stacked ? 1 : series.length)) * 10) / 10}%` } : {}),
      ...(type === 'line'
        ? {
            ...(style.smooth ? { smooth: true } : {}),
            ...(style.lineWidth !== undefined && style.lineWidth !== 2 ? { lineStyle: { width: style.lineWidth } } : {}),
            ...(style.symbols === false ? { showSymbol: false } : {}),
          }
        : {}),
      ...(kind === 'Area' ? { areaStyle: style.areaOpacity !== undefined && style.areaOpacity !== 70 ? { opacity: style.areaOpacity / 100 } : {} } : {}),
      ...(stacked ? { stack: 'total' } : {}),
      ...(series.length > 1 ? { emphasis: { focus: 'series' } } : {}),
      label: {
        show: showLabels,
        position: horizontal ? (stacked ? 'inside' : 'right') : stacked ? 'inside' : 'top',
        ...(formatted ? { formatter: (p: { value: unknown }) => text(p.value) } : {}),
      },
    };
  });

  const valueAxis = {
    type: 'value',
    ...(percent ? { max: 100 } : { ...(style.yMin !== null && style.yMin !== undefined ? { min: style.yMin } : {}), ...(style.yMax !== null && style.yMax !== undefined ? { max: style.yMax } : {}) }),
    ...(formatted ? { axisLabel: { formatter: percent ? '{value}%' : (v: number) => text(v) } } : {}),
    ...(style.grid === false ? { splitLine: { show: false } } : {}),
  };

  const showLegend = series.length > 1 && style.legend !== false;
  const categoryAxis = {
    type: 'category',
    data: categories,
    axisLabel: { hideOverlap: true, ...(style.labelRotate ? { rotate: style.labelRotate } : {}) },
    ...(horizontal ? { inverse: true } : {}),
  };
  const xAxis = { ...(horizontal ? valueAxis : categoryAxis), ...axisName(style.xAxisTitle, true) };
  const yAxis = { ...(horizontal ? categoryAxis : valueAxis), ...axisName(style.yAxisTitle, false) };

  const bottomLegend = showLegend && (style.legendPosition ?? 'bottom') === 'bottom';
  return {
    ...common,
    ...(formatted ? { tooltip: { trigger: 'axis', valueFormatter: text } } : {}),
    grid: gridOption(style, showLegend, {
      left: 16, right: horizontal && showLabels && !stacked ? 40 : 16, top: 24, bottom: bottomLegend ? 40 : 16,
    }),
    legend: showLegend ? legendOption(style, series.length) : undefined,
    xAxis,
    yAxis,
    series: chartSeries,
  };
}

/** Moves a pie's centre away from a side legend or a top legend and a title, which would otherwise sit on it. */
function pieCentre(style: ChartStyleOptions, showLegend: boolean): Record<string, unknown> {
  const position = style.legendPosition ?? 'bottom';
  let x = 50;
  let y = 50;
  if (hasTitle(style)) y += 6;
  if (showLegend && position === 'left') x += 7;
  if (showLegend && position === 'right') x -= 7;
  if (showLegend && position === 'top') y += 5;
  if (showLegend && position === 'bottom') y -= 3;
  return x === 50 && y === 50 ? {} : { center: [`${x}%`, `${y}%`] };
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
  const high = colorsOf(style)?.[0];
  const text = fmt(style);

  return {
    ...common,
    tooltip: {
      trigger: 'item',
      formatter: (p: { value: [number, number, number] }) =>
        `${across[p.value[0]]} · ${down[p.value[1]]}<br/>${columns[2].name}: <b>${text(p.value[2]).toString()}</b>`,
    },
    grid: gridOption(style, false, { left: 16, right: 16, top: 16, bottom: 56 }),
    xAxis: {
      type: 'category', data: across, splitArea: { show: true },
      axisLabel: { hideOverlap: true, ...(style.labelRotate ? { rotate: style.labelRotate } : {}) },
      ...axisName(style.xAxisTitle, true),
    },
    yAxis: { type: 'category', data: down, inverse: true, splitArea: { show: true }, ...axisName(style.yAxisTitle, false) },
    visualMap: {
      min: Math.min(...values),
      max: Math.max(...values),
      calculable: true,
      orient: 'horizontal',
      left: 'center',
      bottom: 0,
      text: [columns[2].name, ''],
      inRange: { color: ['#e7f0fa', high ?? '#1971c2'] },
    },
    series: [{
      type: 'heatmap',
      name: columns[2].name,
      data: cells,
      label: { show: style.labels === true, ...(style.numberFormat ? { formatter: (p: { value: unknown[] }) => text(p.value[2]) } : {}) },
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

  const text = (v: number | null | undefined): string => (v === null || v === undefined ? '∅' : fmt(style)(v));
  const showLegend = groups.size > 1 && style.legend !== false;
  const grid = style.grid === false ? { splitLine: { show: false } } : {};
  const yLimits = {
    ...(style.yMin !== null && style.yMin !== undefined ? { min: style.yMin } : {}),
    ...(style.yMax !== null && style.yMax !== undefined ? { max: style.yMax } : {}),
  };
  return {
    ...common,
    tooltip: {
      trigger: 'item',
      formatter: (p: { name: string; seriesName: string; value: (number | null)[] }) =>
        [
          `<b>${p.name}</b>${groups.size > 1 ? ` (${p.seriesName})` : ''}`,
          `${columns[x].name}: ${p.value[0] === null ? '' : text(p.value[0])}`,
          `${columns[x + 1].name}: ${p.value[1] === null ? '' : text(p.value[1])}`,
          ...(hasSize ? [`${columns[x + 2].name}: ${text(p.value[2])}`] : []),
        ].join('<br/>'),
    },
    grid: gridOption(style, showLegend, { left: 16, right: 24, top: 24, bottom: showLegend && (style.legendPosition ?? 'bottom') === 'bottom' ? 56 : 32 }),
    legend: showLegend ? legendOption(style, groups.size) : undefined,
    // The axes say what they are (unless named otherwise): there are no categories to read them from.
    xAxis: {
      type: 'value', name: style.xAxisTitle?.trim() || columns[x].name, nameLocation: 'middle', nameGap: 28, scale: true, ...grid,
      ...(style.numberFormat ? { axisLabel: { formatter: (v: number) => text(v) } } : {}),
    },
    yAxis: {
      type: 'value', name: style.yAxisTitle?.trim() || columns[x + 1].name, scale: true, ...grid, ...yLimits,
      ...(style.numberFormat ? { axisLabel: { formatter: (v: number) => text(v) } } : {}),
    },
    series: [...groups].map(([name, data]) => ({
      type: 'scatter',
      name,
      data,
      symbolSize,
      ...(style.seriesColors?.[name] ? { itemStyle: { color: style.seriesColors[name] } } : {}),
      ...(groups.size > 1 ? { emphasis: { focus: 'series' } } : {}),
      label: { show: style.labels === true, formatter: (p: { name: string }) => p.name, position: 'top' },
    })),
  };
}

/**
 * The names of what the chart colours: its series, or a pie's slices. The editor lists them so each can have a colour
 * of its own. Empty when there is nothing to draw.
 */
export function seriesNamesOf(kind: ChartKind, result: ResultShape, dimensionCount?: number): string[] {
  const option = buildChartOption(kind, result, {}, dimensionCount) as { series?: { name?: string; data?: { name?: string }[] }[] };
  const series = option.series ?? [];
  if (kind === 'Pie' || kind === 'Donut') return (series[0]?.data ?? []).map((d) => d.name ?? '');
  if (kind === 'Heatmap') return [];
  return series.map((s) => s.name ?? '');
}
