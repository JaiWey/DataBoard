import { BarChart, HeatmapChart, LineChart, PieChart, ScatterChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TitleComponent, TooltipComponent, VisualMapComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, ChartStyleOptions, buildChartOption, colorsOf, seriesNamesOf } from './chart-option';

echarts.use([
  BarChart, LineChart, PieChart, HeatmapChart, ScatterChart,
  GridComponent, TooltipComponent, LegendComponent, AriaComponent, VisualMapComponent, TitleComponent, SVGRenderer,
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

const sales = result(['region', 'revenue'], [['Auckland', 1200], ['Wellington', 3400.5], ['Dunedin', 560]]);
const byMonth = result(['month', 'region', 'revenue'], [
  ['Jan', 'Auckland', 30], ['Jan', 'Wellington', 10], ['Feb', 'Auckland', 5], ['Feb', 'Wellington', 15],
]);

describe('title and axes', () => {
  it('draws a title and makes room for it', () => {
    const plain = option('Bar', sales);
    const o = option('Bar', sales, { title: ' Revenue ' });

    expect(o['title']).toMatchObject({ text: 'Revenue', left: 'center' });
    expect(o['grid'].top).toBeGreaterThan(plain['grid'].top);
    expect(plain['title']).toBeUndefined();
    expect(option('Bar', sales, { title: '   ' })['title']).toBeUndefined();
  });

  it('names the axes and makes room for the names', () => {
    const plain = option('Bar', sales);
    const o = option('Bar', sales, { xAxisTitle: 'Region', yAxisTitle: 'NZD' });

    expect(o['xAxis']).toMatchObject({ type: 'category', name: 'Region', nameLocation: 'middle' });
    expect(o['yAxis']).toMatchObject({ type: 'value', name: 'NZD' });
    expect(o['grid'].bottom).toBeGreaterThan(plain['grid'].bottom);
    expect(o['grid'].top).toBeGreaterThan(plain['grid'].top);
  });

  it('puts the names on the axes as they lie, also for a horizontal bar chart', () => {
    const o = option('BarHorizontal', sales, { xAxisTitle: 'NZD', yAxisTitle: 'Region' });

    expect(o['xAxis']).toMatchObject({ type: 'value', name: 'NZD' });
    expect(o['yAxis']).toMatchObject({ type: 'category', name: 'Region' });
  });

  it('hides the grid lines on request, and shows them otherwise', () => {
    expect(option('Bar', sales, { grid: false })['yAxis'].splitLine).toEqual({ show: false });
    expect(option('Bar', sales, { grid: true })['yAxis'].splitLine).toBeUndefined();
    expect(option('Bar', sales)['yAxis']).toEqual({ type: 'value' });
  });

  it('fixes where the value axis starts and ends', () => {
    expect(option('Bar', sales, { yMin: 0, yMax: 5000 })['yAxis']).toMatchObject({ min: 0, max: 5000 });
    expect(option('Bar', sales, { yMin: null, yMax: null })['yAxis']).toEqual({ type: 'value' });
    expect(option('Line', sales, { yMin: -10 })['yAxis']).toMatchObject({ min: -10 });
    expect(option('BarHorizontal', sales, { yMax: 100 })['xAxis']).toMatchObject({ max: 100 }); // the value axis lies across
  });

  it('keeps a 100% axis at 100 whatever the limits say', () => {
    const o = option('Bar', byMonth, { percent: true, yMin: 10, yMax: 50 }, 2);

    expect(o['yAxis']).toMatchObject({ max: 100 });
    expect(o['yAxis'].min).toBeUndefined();
  });

  it('turns the category labels', () => {
    expect(option('Bar', sales, { labelRotate: -45 })['xAxis'].axisLabel).toMatchObject({ rotate: -45, hideOverlap: true });
    expect(option('Bar', sales, { labelRotate: 0 })['xAxis'].axisLabel).toEqual({ hideOverlap: true });
  });

  it('puts the same on a heatmap and a scatter chart', () => {
    const grid = result(['a', 'b', 'n'], [['x', 'p', 1], ['y', 'q', 5]]);
    const heat = option('Heatmap', grid, { title: 'Heat', xAxisTitle: 'A', yAxisTitle: 'B', labelRotate: 30 }, 2);
    const points = result(['c', 'x', 'y'], [['a', 1, 2], ['b', 3, 4]]);
    const scatter = option('Scatter', points, { title: 'Dots', xAxisTitle: 'Orders', yAxisTitle: 'Money', grid: false, yMin: 0, yMax: 9 }, 1);

    expect(heat['title'].text).toBe('Heat');
    expect(heat['xAxis']).toMatchObject({ name: 'A', axisLabel: { rotate: 30 } });
    expect(heat['yAxis']).toMatchObject({ name: 'B' });
    expect(scatter['title'].text).toBe('Dots');
    expect(scatter['xAxis']).toMatchObject({ name: 'Orders', splitLine: { show: false } });
    expect(scatter['yAxis']).toMatchObject({ name: 'Money', min: 0, max: 9 });
  });

  it('keeps the names of the scatter axes from the metrics unless renamed', () => {
    const o = option('Scatter', result(['c', 'orders', 'revenue'], [['a', 1, 2]]), {}, 1);

    expect(o['xAxis'].name).toBe('orders');
    expect(o['yAxis'].name).toBe('revenue');
  });

  it('is drawn by ECharts with a title and axis names', () => {
    const svg = draw(option('Bar', sales, { title: 'Revenue by region', xAxisTitle: 'Region', yAxisTitle: 'NZD', labelRotate: 20 }));

    expect(svg).toContain('Revenue by region');
    expect(svg).toContain('Region');
  });
});

describe('colours', () => {
  it('uses your own colours instead of the palette', () => {
    expect(option('Bar', sales, { palette: 'warm', colors: ['#111111', '#222222'] })['color']).toEqual(['#111111', '#222222']);
    expect(option('Bar', sales, { palette: 'warm', colors: [] })['color']).toEqual(option('Bar', sales, { palette: 'warm' })['color']);
    expect(option('Bar', sales, { colors: null })['color']).toBeUndefined();
    expect(colorsOf({ colors: ['#abc'] })).toEqual(['#abc']);
    expect(colorsOf({ palette: 'cool' })?.[0]).toBe('#1971c2');
  });

  it('gives particular series a colour of their own', () => {
    const o = option('Bar', byMonth, { seriesColors: { Wellington: '#2b8a3e' } }, 2);

    expect(o['series'].find((s: any) => s.name === 'Wellington').itemStyle).toEqual({ color: '#2b8a3e' });
    expect(o['series'].find((s: any) => s.name === 'Auckland').itemStyle).toBeUndefined();
  });

  it('colours slices and points by name too', () => {
    const pie = option('Pie', sales, { seriesColors: { Dunedin: '#c92a2a' } }, 1);
    const scatter = option('Scatter', result(['c', 'r', 'x', 'y'], [['a', 'North', 1, 2], ['b', 'South', 3, 4]]), { seriesColors: { South: '#c92a2a' } }, 2);

    expect(pie['series'][0].data.find((d: any) => d.name === 'Dunedin').itemStyle).toEqual({ color: '#c92a2a' });
    expect(pie['series'][0].data.find((d: any) => d.name === 'Auckland').itemStyle).toBeUndefined();
    expect(scatter['series'].find((s: any) => s.name === 'South').itemStyle).toEqual({ color: '#c92a2a' });
  });

  it('uses the first of your colours as the high end of a heatmap', () => {
    const o = option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1], ['y', 'q', 5]]), { colors: ['#123456'] }, 2);

    expect(o['visualMap'].inRange.color[1]).toBe('#123456');
  });

  it('lists what can be coloured: series, slices and points, but not a heatmap', () => {
    expect(seriesNamesOf('Bar', byMonth, 2)).toEqual(['Auckland', 'Wellington']);
    expect(seriesNamesOf('Pie', sales, 1)).toEqual(['Auckland', 'Wellington', 'Dunedin']);
    expect(seriesNamesOf('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1]]), 2)).toEqual([]);
    expect(seriesNamesOf('Bar', result([], []))).toEqual([]);
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Bar', byMonth, { colors: ['#112233', '#445566'], seriesColors: { Wellington: '#2b8a3e' } }, 2))).toContain('#2b8a3e');
  });
});

describe('number format', () => {
  const format = { decimals: 1, prefix: '$', compact: true };

  it('writes the value axis, the labels and the tooltip in the format', () => {
    const o = option('Bar', sales, { numberFormat: format, labels: true });

    expect(o['yAxis'].axisLabel.formatter(1200)).toBe('$1.2K');
    expect(o['series'][0].label.formatter({ value: 3400.5 })).toBe('$3.4K');
    expect(o['tooltip'].valueFormatter(560)).toBe('$560'); // short forms do not pad decimals
  });

  it('leaves missing values as a hollow circle', () => {
    const o = option('Bar', sales, { numberFormat: format });

    expect(o['tooltip'].valueFormatter(null)).toBe('∅');
  });

  it('changes nothing without a format', () => {
    const o = option('Bar', sales);

    expect(o['yAxis'].axisLabel).toBeUndefined();
    expect(o['series'][0].label.formatter).toBeUndefined();
    expect(o['tooltip']).toEqual({ trigger: 'axis' });
  });

  it('keeps percentages as percentages', () => {
    const o = option('Bar', byMonth, { percent: true, numberFormat: { prefix: '$' } }, 2);

    expect(o['yAxis'].axisLabel.formatter).toBe('{value}%');
    expect(o['tooltip'].valueFormatter(33.333)).toBe('33.3%');
  });

  it('formats a horizontal bar chart along its value axis', () => {
    const o = option('BarHorizontal', sales, { numberFormat: { suffix: ' NZD', thousands: true } });

    expect(o['xAxis'].axisLabel.formatter(1200)).toBe('1,200 NZD');
  });

  it('formats heatmap and scatter tooltips and axes', () => {
    const heat = option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1234]]), { numberFormat: { prefix: '$' } }, 2);
    const scatter = option('Scatter', result(['c', 'orders', 'revenue', 'margin'], [['a', 1200, 3400, 10]]), { numberFormat: { compact: true } }, 1);

    expect(heat['tooltip'].formatter({ value: [0, 0, 1234] })).toContain('$1,234');
    expect(scatter['tooltip'].formatter({ name: 'a', seriesName: 'revenue', value: [1200, 3400, 10] })).toContain('orders: 1.2K');
    expect(scatter['xAxis'].axisLabel.formatter(3400)).toBe('3.4K');
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Bar', sales, { numberFormat: format, labels: true }))).toContain('<svg');
  });
});

describe('legend', () => {
  it('goes at the bottom unless put elsewhere, and makes room beside the chart', () => {
    expect(option('Bar', byMonth, {}, 2)['legend']).toEqual({ bottom: 0 });
    expect(option('Bar', byMonth, { legendPosition: 'top' }, 2)['legend']).toEqual({ top: 0 });
    expect(option('Bar', byMonth, { legendPosition: 'top', title: 'T' }, 2)['legend']).toEqual({ top: 28 });
    expect(option('Bar', byMonth, { legendPosition: 'left' }, 2)['legend']).toMatchObject({ orient: 'vertical', left: 0 });
    expect(option('Bar', byMonth, { legendPosition: 'right' }, 2)['legend']).toMatchObject({ orient: 'vertical', right: 0 });
    expect(option('Bar', byMonth, { legendPosition: 'right' }, 2)['grid'].right).toBeGreaterThan(option('Bar', byMonth, {}, 2)['grid'].right);
    expect(option('Bar', byMonth, { legendPosition: 'left' }, 2)['grid'].left).toBeGreaterThan(16);
  });

  it('only takes room at the bottom when it is there', () => {
    expect(option('Bar', byMonth, {}, 2)['grid'].bottom).toBe(40);
    expect(option('Bar', byMonth, { legendPosition: 'top' }, 2)['grid'].bottom).toBe(16);
  });

  it('is placed on pie, donut and scatter charts too, and hidden when turned off', () => {
    expect(option('Pie', sales, { legendPosition: 'right' }, 1)['legend']).toMatchObject({ type: 'scroll', right: 0 });
    expect(option('Pie', sales, { legend: false }, 1)['legend']).toBeUndefined();
    const points = result(['c', 'r', 'x', 'y'], [['a', 'N', 1, 2], ['b', 'S', 3, 4]]);
    expect(option('Scatter', points, { legendPosition: 'top' }, 2)['legend']).toEqual({ top: 0 });
  });

  it('is drawn by ECharts at each position', () => {
    for (const legendPosition of ['top', 'bottom', 'left', 'right'] as const)
      expect(draw(option('Bar', byMonth, { legendPosition, title: 'T' }, 2))).toContain('<svg');
  });
});

describe('look of series', () => {
  it('curves and thickens lines', () => {
    const o = option('Line', sales, { smooth: true, lineWidth: 5, symbols: true });

    expect(o['series'][0]).toMatchObject({ type: 'line', smooth: true, lineStyle: { width: 5 } });
  });

  it('leaves lines as ECharts draws them without choices, and with the defaults', () => {
    const o = option('Line', sales, { smooth: false, lineWidth: 2 });

    expect(o['series'][0].smooth).toBeUndefined();
    expect(o['series'][0].lineStyle).toBeUndefined();
  });

  it('hides the points of lines unless they are asked for', () => {
    expect(option('Line', sales, {})['series'][0].showSymbol).toBe(false);
    expect(option('Line', sales, { symbols: false })['series'][0].showSymbol).toBe(false);
    expect(option('Line', sales, { symbols: true })['series'][0].showSymbol).toBeUndefined();
    expect(option('Area', sales, {})['series'][0].showSymbol).toBe(false);
  });

  it('sets the width of bars and rounds their ends the right way round', () => {
    const vertical = option('Bar', sales, { barWidth: 40, barRadius: 6 });
    const horizontal = option('BarHorizontal', sales, { barRadius: 6 });

    expect(vertical['series'][0]).toMatchObject({ barWidth: '40%', itemStyle: { borderRadius: [6, 6, 0, 0] } });
    expect(horizontal['series'][0].itemStyle).toEqual({ borderRadius: [0, 6, 6, 0] });
    expect(option('Bar', sales, { barRadius: 0 })['series'][0].itemStyle).toBeUndefined();
    expect(option('Bar', sales, { barWidth: null })['series'][0].barWidth).toBeUndefined();
  });

  it('shares the width of a slot between bars side by side, and gives it whole to stacked ones', () => {
    expect(option('Bar', byMonth, { barWidth: 60 }, 2)['series'][0].barWidth).toBe('30%');
    expect(option('Bar', byMonth, { barWidth: 60, stack: true }, 2)['series'][0].barWidth).toBe('60%');
  });

  it('does not give lines a bar width or bars a smooth curve', () => {
    expect(option('Line', sales, { barWidth: 40, barRadius: 6 })['series'][0].barWidth).toBeUndefined();
    expect(option('Line', sales, { barRadius: 6 })['series'][0].itemStyle).toBeUndefined();
    expect(option('Bar', sales, { smooth: true })['series'][0].smooth).toBeUndefined();
  });

  it('makes an area more or less solid', () => {
    expect(option('Area', sales, { areaOpacity: 30 })['series'][0].areaStyle).toEqual({ opacity: 0.3 });
    expect(option('Area', sales, { areaOpacity: 70 })['series'][0].areaStyle).toEqual({});
    expect(option('Area', sales, {})['series'][0].areaStyle).toEqual({});
    expect(option('Line', sales, { areaOpacity: 30 })['series'][0].areaStyle).toBeUndefined();
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Area', byMonth, { smooth: true, lineWidth: 4, areaOpacity: 20, symbols: false }, 2))).toContain('<svg');
    expect(draw(option('Bar', byMonth, { barWidth: 50, barRadius: 8 }, 2))).toContain('<svg');
  });
});

describe('pie and donut', () => {
  it('sets the size of the hole', () => {
    expect(option('Pie', sales, {}, 1)['series'][0].radius).toEqual(['0%', '70%']);
    expect(option('Donut', sales, {}, 1)['series'][0].radius).toEqual(['45%', '70%']);
    expect(option('Donut', sales, { innerRadius: 60 }, 1)['series'][0].radius).toEqual(['60%', '70%']);
    expect(option('Pie', sales, { innerRadius: 30 }, 1)['series'][0].radius).toEqual(['30%', '70%']);
    expect(option('Donut', sales, { innerRadius: null }, 1)['series'][0].radius).toEqual(['45%', '70%']);
  });

  it('makes the radius follow the value for a rose chart', () => {
    expect(option('Pie', sales, { rose: true }, 1)['series'][0].roseType).toBe('radius');
    expect(option('Pie', sales, {}, 1)['series'][0].roseType).toBeUndefined();
  });

  it('says what a slice label shows', () => {
    const label = (pieLabel: any) => option('Pie', sales, { labels: true, pieLabel, numberFormat: { prefix: '$' } }, 1)['series'][0].label.formatter;
    const slice = { name: 'Auckland', value: 1200, percent: 25.04 };

    expect(label('name')(slice)).toBe('Auckland');
    expect(label('value')(slice)).toBe('$1,200');
    expect(label('percent')(slice)).toBe('25%');
    expect(label('name-value')(slice)).toBe('Auckland: $1,200');
    expect(label('name-percent')(slice)).toBe('Auckland (25%)');
    expect(option('Pie', sales, { labels: true }, 1)['series'][0].label.formatter(slice)).toBe('Auckland');
  });

  it('leaves room for the labels by drawing the circle smaller when they show', () => {
    expect(option('Pie', sales, {}, 1)['series'][0].radius).toEqual(['0%', '70%']);
    expect(option('Donut', sales, { labels: true }, 1)['series'][0].radius).toEqual(['45%', '54%']);
  });

  it('shows labels only on request, and tells the amount and the share in the tooltip', () => {
    const o = option('Pie', sales, { numberFormat: { prefix: '$' } }, 1);

    expect(o['series'][0].label.show).toBe(false);
    expect(o['tooltip'].formatter({ name: 'Auckland', value: 1200, percent: 25.04 })).toBe('Auckland: <b>$1,200</b> (25%)');
  });

  it('moves the circle away from a side legend and a title', () => {
    expect(option('Pie', sales, { legend: false }, 1)['series'][0].center).toBeUndefined();
    expect(option('Pie', sales, { legendPosition: 'right' }, 1)['series'][0].center[0]).toBe('43%');
    expect(option('Pie', sales, { legendPosition: 'left' }, 1)['series'][0].center[0]).toBe('57%');
    expect(option('Pie', sales, { title: 'T', legendPosition: 'right' }, 1)['series'][0].center[1]).toBe('56%');
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Donut', sales, { rose: true, innerRadius: 40, labels: true, pieLabel: 'name-percent', title: 'Share' }, 1))).toContain('Share');
    expect(draw(option('Pie', sales, { seriesColors: { Dunedin: '#c92a2a' }, legendPosition: 'left' }, 1))).toContain('#c92a2a');
  });
});
