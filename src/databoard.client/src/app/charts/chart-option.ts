import type { EChartsCoreOption } from 'echarts/core';

export type ChartKind = 'Bar' | 'Line' | 'Pie';

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
export function buildChartOption(kind: ChartKind, result: ResultShape): EChartsCoreOption {
  const { columns, rows } = result;
  if (columns.length === 0 || rows.length === 0) return { series: [] };

  const common = { aria: { enabled: true }, tooltip: { trigger: kind === 'Pie' ? 'item' : 'axis' } };

  if (kind === 'Pie') {
    // A pie shows one measure, as slices of the whole; negative or empty values cannot be drawn as slices.
    const valueColumn = columns.length > 1 ? 1 : 0;
    const data = rows
      .map((row) => ({ name: columns.length > 1 ? label(row[0]) : columns[0].name, value: toNumber(row[valueColumn]) }))
      .filter((d): d is { name: string; value: number } => d.value !== null && d.value > 0);
    return {
      ...common,
      legend: { type: 'scroll', bottom: 0 },
      series: [{ type: 'pie', name: columns[valueColumn].name, radius: ['35%', '65%'], data }],
    };
  }

  const type = kind === 'Bar' ? 'bar' : 'line';
  const type1 = columns.length === 1;
  const categories = type1 ? [columns[0].name] : rows.map((row) => label(row[0]));
  const seriesColumns = type1 ? [0] : columns.map((_, i) => i).slice(1);
  const series = seriesColumns.map((index) => ({
    type,
    name: columns[index].name,
    data: type1 ? [toNumber(rows[0][0])] : rows.map((row) => toNumber(row[index])),
  }));

  return {
    ...common,
    grid: { left: 16, right: 16, top: 24, bottom: series.length > 1 ? 40 : 16, containLabel: true },
    legend: series.length > 1 ? { bottom: 0 } : undefined,
    xAxis: { type: 'category', data: categories, axisLabel: { hideOverlap: true } },
    yAxis: { type: 'value' },
    series,
  };
}
