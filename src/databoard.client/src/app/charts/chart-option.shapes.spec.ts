import { BarChart, HeatmapChart, LineChart, PieChart, ScatterChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent, VisualMapComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, ChartStyleOptions, buildChartOption } from './chart-option';

echarts.use([
  BarChart, LineChart, PieChart, HeatmapChart, ScatterChart,
  GridComponent, TooltipComponent, LegendComponent, AriaComponent, VisualMapComponent, SVGRenderer,
]);

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });
const option = (kind: ChartKind, r: ReturnType<typeof result>, style: ChartStyleOptions = {}, dimensions?: number) =>
  buildChartOption(kind, r, style, dimensions) as Record<string, any>;

/** Draws the option for real (no canvas needed) so a mistake that only ECharts notices shows up here. */
const draw = (o: Record<string, any>): string => {
  const chart = echarts.init(null as never, undefined, { renderer: 'svg', ssr: true, width: 480, height: 320 });
  chart.setOption(o as never);
  const svg = chart.renderToSVGString();
  chart.dispose();
  return svg;
};

// region x product, revenue
const grid = result(
  ['region', 'product', 'revenue'],
  [['Auckland', 'Phone', 10], ['Auckland', 'Laptop', 30], ['Wellington', 'Phone', 20], ['Wellington', 'Laptop', 20], ['Dunedin', 'Phone', 5]],
);

describe('100% stacking', () => {
  const series = result(['month', 'region', 'revenue'], [
    ['Jan', 'Auckland', 30], ['Jan', 'Wellington', 10],
    ['Feb', 'Auckland', 5], ['Feb', 'Wellington', 15],
    ['Mar', 'Auckland', 0], ['Mar', 'Wellington', 0],
  ]);

  it('scales every category to 100 and stacks the series', () => {
    const o = option('Bar', series, { percent: true }, 2);

    expect(o['series'].map((s: any) => s.name)).toEqual(['Auckland', 'Wellington']);
    expect(o['series'][0].data).toEqual([75, 25, null]);
    expect(o['series'][1].data).toEqual([25, 75, null]);
    expect(o['series'].every((s: any) => s.stack === 'total')).toBe(true);
  });

  it('shows a percentage axis from 0 to 100 and writes values as percentages', () => {
    const o = option('Bar', series, { percent: true, labels: true }, 2);

    expect(o['yAxis']).toMatchObject({ type: 'value', max: 100, axisLabel: { formatter: '{value}%' } });
    expect(o['tooltip'].valueFormatter(33.333)).toBe('33.3%');
    expect(o['series'][0].label.formatter({ value: 75 })).toBe('75%');
  });

  it('puts the percentage axis on the other side for a horizontal bar, with labels inside', () => {
    const o = option('BarHorizontal', series, { percent: true, labels: true }, 2);

    expect(o['xAxis']).toMatchObject({ type: 'value', max: 100 });
    expect(o['yAxis'].type).toBe('category');
    expect(o['series'][0].label.position).toBe('inside');
  });

  it('works for lines and areas too', () => {
    expect(option('Area', series, { percent: true }, 2)['series'][0]).toMatchObject({ type: 'line', stack: 'total', areaStyle: {} });
    expect(option('Line', series, { percent: true }, 2)['series'][1].data).toEqual([25, 75, null]);
  });

  it('counts a missing or negative value as nothing in the total, and keeps a gap for a missing one', () => {
    const odd = result(['month', 'region', 'revenue'], [['Jan', 'A', 30], ['Jan', 'B', -10], ['Feb', 'A', 10]]);

    const o = option('Bar', odd, { percent: true }, 2);

    expect(o['series'][0].data).toEqual([100, 100]);
    expect(o['series'][1].data).toEqual([0, null]);
  });

  it('does nothing without it', () => {
    const o = option('Bar', series, {}, 2);

    expect(o['series'][0].data).toEqual([30, 5, 0]);
    expect(o['series'][0].stack).toBeUndefined();
    expect(o['yAxis']).toEqual({ type: 'value' });
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Bar', series, { percent: true }, 2))).toContain('<svg');
  });
});

describe('heatmap', () => {
  it('puts the first dimension across, the second down and the metric as the colour', () => {
    const o = option('Heatmap', grid, {}, 2);

    expect(o['xAxis'].data).toEqual(['Auckland', 'Wellington', 'Dunedin']);
    expect(o['yAxis'].data).toEqual(['Phone', 'Laptop']);
    expect(o['yAxis'].inverse).toBe(true); // the first row at the top
    expect(o['series'][0].type).toBe('heatmap');
    expect(o['series'][0].data).toEqual([[0, 0, 10], [0, 1, 30], [1, 0, 20], [1, 1, 20], [2, 0, 5]]);
    expect(o['visualMap']).toMatchObject({ min: 5, max: 30 });
  });

  it('leaves a combination that has no row empty', () => {
    const cells = option('Heatmap', grid, {}, 2)['series'][0].data;

    expect(cells.some((c: number[]) => c[0] === 2 && c[1] === 1)).toBe(false); // Dunedin has no Laptop
  });

  it('skips a cell with no value but keeps its place on the axes', () => {
    const o = option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1], ['y', 'q', null], ['y', 'p', 3]]), {}, 2);

    expect(o['xAxis'].data).toEqual(['x', 'y']);
    expect(o['yAxis'].data).toEqual(['p', 'q']);
    expect(o['series'][0].data).toEqual([[0, 0, 1], [1, 0, 3]]);
  });

  it('uses the first colour of the palette for the high end, and shows values on request', () => {
    const o = option('Heatmap', grid, { palette: 'warm', labels: true }, 2);

    expect(o['visualMap'].inRange.color[1]).toBe('#e8590c');
    expect(o['series'][0].label.show).toBe(true);
  });

  it('names the cell and the value in the tooltip', () => {
    const o = option('Heatmap', grid, {}, 2);

    expect(o['tooltip'].formatter({ value: [1, 1, 20] })).toBe('Wellington · Laptop<br/>revenue: <b>20</b>');
  });

  it('draws nothing without two dimensions and a metric, or without any values', () => {
    expect(option('Heatmap', result(['a', 'n'], [['x', 1]]), {}, 1)['series']).toEqual([]);
    expect(option('Heatmap', result(['a', 'b'], [['x', 'y']]), {}, 2)['series']).toEqual([]);
    expect(option('Heatmap', result(['a', 'b', 'n'], [['x', 'y', null]]), {}, 2)['series']).toEqual([]);
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Heatmap', grid, {}, 2))).toContain('<svg');
  });
});

describe('scatter', () => {
  // customer, orders (X), revenue (Y), margin (size)
  const points = result(
    ['customer', 'orders', 'revenue', 'margin'],
    [['Ann', 3, 300, 10], ['Bo', 8, 120, 40], ['Cy', 5, 500, null], ['Di', null, 200, 5]],
  );

  it('is one point per row, with the first metric on X and the second on Y', () => {
    const o = option('Scatter', result(['customer', 'orders', 'revenue'], [['Ann', 3, 300], ['Bo', 8, 120]]), {}, 1);

    expect(o['series']).toHaveLength(1);
    expect(o['series'][0].type).toBe('scatter');
    expect(o['series'][0].name).toBe('revenue');
    expect(o['series'][0].data).toEqual([{ name: 'Ann', value: [3, 300] }, { name: 'Bo', value: [8, 120] }]);
    expect(o['xAxis']).toMatchObject({ type: 'value', name: 'orders' });
    expect(o['yAxis']).toMatchObject({ type: 'value', name: 'revenue' });
    expect(o['series'][0].symbolSize).toBe(10);
    expect(o['legend']).toBeUndefined();
  });

  it('skips a point that has no X or no Y', () => {
    const o = option('Scatter', points, {}, 1);

    expect(o['series'][0].data.map((d: any) => d.name)).toEqual(['Ann', 'Bo', 'Cy']);
  });

  it('is a bubble chart with a third metric: the area follows the value, a missing value gets the smallest', () => {
    const size = option('Scatter', points, {}, 1)['series'][0].symbolSize as (v: (number | null)[]) => number;

    expect(size([3, 300, 40])).toBe(48); // the biggest
    expect(size([3, 300, 10])).toBeCloseTo(8 + 40 * Math.sqrt(10 / 40), 5);
    expect(size([3, 300, null])).toBe(8);
    expect(size([3, 300, 0])).toBe(8);
    expect(size([3, 300, 20])).toBeLessThan(size([3, 300, 40]));
  });

  it('splits points into series by a second dimension, with a legend and the series name in the tooltip', () => {
    const r = result(['customer', 'region', 'orders', 'revenue'], [['Ann', 'North', 3, 300], ['Bo', 'South', 8, 120], ['Cy', 'North', 5, 500]]);

    const o = option('Scatter', r, {}, 2);

    expect(o['series'].map((s: any) => [s.name, s.data.length])).toEqual([['North', 2], ['South', 1]]);
    expect(o['legend']).toBeDefined();
    expect(o['xAxis'].name).toBe('orders');
    expect(o['tooltip'].formatter({ name: 'Ann', seriesName: 'North', value: [3, 300] })).toBe('<b>Ann</b> (North)<br/>orders: 3<br/>revenue: 300');
    expect(option('Scatter', r, { legend: false }, 2)['legend']).toBeUndefined();
  });

  it('shows the size in the tooltip, and the point names as labels on request', () => {
    const o = option('Scatter', points, { labels: true }, 1);

    expect(o['tooltip'].formatter({ name: 'Ann', seriesName: 'revenue', value: [3, 300, 10] })).toBe('<b>Ann</b><br/>orders: 3<br/>revenue: 300<br/>margin: 10');
    expect(o['series'][0].label.show).toBe(true);
    expect(o['series'][0].label.formatter({ name: 'Ann' })).toBe('Ann');
  });

  it('draws nothing without two metrics or without any complete point', () => {
    expect(option('Scatter', result(['c', 'x'], [['a', 1]]), {}, 1)['series']).toEqual([]);
    expect(option('Scatter', result(['c', 'x', 'y'], [['a', null, 1], ['b', 1, null]]), {}, 1)['series']).toEqual([]);
  });

  it('is drawn by ECharts, with and without sizes and series', () => {
    expect(draw(option('Scatter', points, {}, 1))).toContain('<svg');
    expect(draw(option('Scatter', result(['c', 'r', 'x', 'y'], [['a', 'n', 1, 2], ['b', 's', 3, 4]]), {}, 2))).toContain('<svg');
  });
});
