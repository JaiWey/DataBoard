import type { EChartsCoreOption } from 'echarts/core';

/** The kinds ECharts draws. Table and Number are not charts in this sense and are shown by `ChartViewComponent`. */
export type ChartKind = 'Bar' | 'Line' | 'Pie' | 'Area' | 'BarHorizontal' | 'Donut';

/** How a saved chart is drawn. Every field is optional so a chart without a style still draws. */
export interface ChartStyleOptions {
  legend?: boolean;
  labels?: boolean;
  palette?: string;
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

const label = (value: unknown): string => (value === null || value === undefined ? '∅' : String(value));

const toNumber = (value: unknown): number | null => {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
};

/**
 * Turns a query result into an ECharts option. By convention the first column is the category (what the data is
 * grouped by) and every further column is a series. A result with a single column is drawn as one value.
 */
export function buildChartOption(kind: ChartKind, result: ResultShape, style: ChartStyleOptions = {}): EChartsCoreOption {
  const { columns, rows } = result;
  if (columns.length === 0 || rows.length === 0) return { series: [] };

  const round = kind === 'Pie' || kind === 'Donut';
  const showLabels = style.labels === true;
  const color = PALETTES[style.palette ?? 'default'];
  const common = {
    aria: { enabled: true },
    tooltip: { trigger: round ? 'item' : 'axis' },
    ...(color ? { color } : {}),
  };

  if (round) {
    // A pie shows one measure, as slices of the whole; negative or empty values cannot be drawn as slices.
    const valueColumn = columns.length > 1 ? 1 : 0;
    const data = rows
      .map((row) => ({ name: columns.length > 1 ? label(row[0]) : columns[0].name, value: toNumber(row[valueColumn]) }))
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
  const oneColumn = columns.length === 1;
  const categories = oneColumn ? [columns[0].name] : rows.map((row) => label(row[0]));
  const seriesColumns = oneColumn ? [0] : columns.map((_, i) => i).slice(1);
  const series = seriesColumns.map((index) => ({
    type,
    name: columns[index].name,
    data: oneColumn ? [toNumber(rows[0][0])] : rows.map((row) => toNumber(row[index])),
    ...(kind === 'Area' ? { areaStyle: {} } : {}),
    label: { show: showLabels, position: horizontal ? 'right' : 'top' },
  }));

  const showLegend = series.length > 1 && style.legend !== false;
  const categoryAxis = { type: 'category', data: categories, axisLabel: { hideOverlap: true }, ...(horizontal ? { inverse: true } : {}) };

  return {
    ...common,
    grid: { left: 16, right: horizontal && showLabels ? 40 : 16, top: 24, bottom: showLegend ? 40 : 16, containLabel: true },
    legend: showLegend ? { bottom: 0 } : undefined,
    xAxis: horizontal ? { type: 'value' } : categoryAxis,
    yAxis: horizontal ? categoryAxis : { type: 'value' },
    series,
  };
}
