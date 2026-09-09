import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, ChartStyleOptions, buildChartOption } from './chart-option';

echarts.use([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, AriaComponent, SVGRenderer]);

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });
const option = (kind: ChartKind, r: ReturnType<typeof result>, style: ChartStyleOptions = {}, dimensions?: number) =>
  buildChartOption(kind, r, style, dimensions) as Record<string, any>;

// year, name, total: one line per name, like a "births by name over the years" chart.
const births = result(['year', 'name', 'total'], [
  [1972, 'Michael', 70], [1972, 'David', 50],
  [1979, 'Michael', 60], [1979, 'David', 55], [1979, 'Jennifer', 40],
  [1986, 'Michael', 45],
]);

describe('two dimensions: one series per value of the second', () => {
  const o = option('Line', births, {}, 2);

  it('puts the first dimension on the X axis, in the order it came', () => {
    expect(o['xAxis'].data).toEqual(['1972', '1979', '1986']);
  });

  it('makes a series for every value of the second dimension, named after it', () => {
    expect(o['series'].map((s: any) => s.name)).toEqual(['Michael', 'David', 'Jennifer']);
    expect(o['series'].every((s: any) => s.type === 'line')).toBe(true);
  });

  it('lines the values up with the X axis and leaves gaps where a combination has no row', () => {
    expect(o['series'].map((s: any) => s.data)).toEqual([
      [70, 60, 45],
      [50, 55, null],
      [null, 40, null],
    ]);
  });

  it('shows a legend for the series, and highlights one on hover', () => {
    expect(o['legend']).toBeDefined();
    expect(o['series'][0].emphasis).toEqual({ focus: 'series' });
  });

  it('works for bars and areas too', () => {
    expect(option('Bar', births, {}, 2)['series'].map((s: any) => s.type)).toEqual(['bar', 'bar', 'bar']);
    expect(option('Area', births, {}, 2)['series'].every((s: any) => s.areaStyle)).toBe(true);
  });

  it('draws horizontal bars with the first dimension on the vertical axis', () => {
    const h = option('BarHorizontal', births, {}, 2);

    expect(h['yAxis'].data).toEqual(['1972', '1979', '1986']);
    expect(h['series'].length).toBe(3);
  });
});

describe('two dimensions and several metrics', () => {
  it('names each series after the group and the metric', () => {
    const r = result(['year', 'name', 'total', 'births'], [[1972, 'Michael', 70, 7], [1972, 'David', 50, 5]]);

    const names = option('Bar', r, {}, 2)['series'].map((s: any) => s.name);

    expect(names).toEqual(['Michael · total', 'Michael · births', 'David · total', 'David · births']);
  });
});

describe('three dimensions', () => {
  it('joins the extra dimensions into the series name', () => {
    const r = result(['year', 'gender', 'name', 'total'], [[1972, 'F', 'Ann', 3], [1972, 'M', 'Ann', 1], [1979, 'F', 'Ann', 4]]);

    const o = option('Line', r, {}, 3);

    expect(o['series'].map((s: any) => s.name)).toEqual(['F / Ann', 'M / Ann']);
    expect(o['series'][0].data).toEqual([3, 4]);
    expect(o['series'][1].data).toEqual([1, null]);
  });
});

describe('the number of dimensions', () => {
  it('still treats a result as category plus series when it is not told otherwise', () => {
    const r = result(['region', 'revenue', 'orders'], [['Auckland', 2480, 3]]);

    expect(option('Bar', r)['series'].map((s: any) => s.name)).toEqual(['revenue', 'orders']);
  });

  it('draws a result without dimensions as one value per metric', () => {
    const r = result(['revenue', 'orders'], [[2480, 3]]);

    const o = option('Bar', r, {}, 0);

    expect(o['xAxis'].data).toEqual(['revenue', 'orders']);
    expect(o['series'][0].data).toEqual([2480, 3]);
  });

  it('draws a single dimension with one metric as before, even when told it is one', () => {
    const r = result(['region', 'revenue'], [['Auckland', 30], ['Nelson', 10]]);

    expect(option('Bar', r, {}, 1)['xAxis'].data).toEqual(['Auckland', 'Nelson']);
  });

  it('names pie slices after all the dimensions', () => {
    const r = result(['region', 'product', 'revenue'], [['Auckland', 'Phone', 30], ['Nelson', 'Laptop', 10]]);

    const o = option('Pie', r, {}, 2);

    expect(o['series'][0].data).toEqual([{ name: 'Auckland / Phone', value: 30 }, { name: 'Nelson / Laptop', value: 10 }]);
  });
});

describe('stacking and many series', () => {
  it('piles the series up when asked, and not otherwise', () => {
    expect(option('Bar', births, { stack: true }, 2)['series'].every((s: any) => s.stack === 'total')).toBe(true);
    expect(option('Bar', births, {}, 2)['series'].some((s: any) => s.stack)).toBe(false);
    expect(option('Area', births, { stack: true }, 2)['series'].every((s: any) => s.stack === 'total')).toBe(true);
  });

  it('scrolls the legend once there are many series', () => {
    const names = Array.from({ length: 12 }, (_, i) => `name${i}`);
    const many = result(['year', 'name', 'total'], names.map((n, i) => [2000, n, i]));

    expect(option('Line', many, {}, 2)['legend']).toEqual({ type: 'scroll', bottom: 0 });
    expect(option('Line', births, {}, 2)['legend']).toEqual({ bottom: 0 });
  });

  it('keeps the legend off when it is switched off', () => {
    expect(option('Line', births, { legend: false }, 2)['legend']).toBeUndefined();
  });
});

describe('pivoted charts in real ECharts', () => {
  it.each(['Line', 'Bar', 'Area', 'BarHorizontal'] as ChartKind[])('renders %s with every series in the legend', (kind) => {
    const chart = echarts.init(null, undefined, { renderer: 'svg', ssr: true, width: 500, height: 300 });
    chart.setOption(buildChartOption(kind, births, { stack: true, labels: true }, 2));
    const svg = chart.renderToSVGString();
    chart.dispose();

    expect(svg).toContain('Michael');
    expect(svg).toContain('Jennifer');
    expect(svg).toContain('1979');
  });
});
