import { BarChart, HeatmapChart, LineChart, PieChart, ScatterChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TitleComponent, TooltipComponent, VisualMapComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, ChartStyleOptions, buildChartOption, fontProps, pieParts } from './chart-option';

echarts.use([
  BarChart, LineChart, PieChart, HeatmapChart, ScatterChart,
  GridComponent, TooltipComponent, LegendComponent, AriaComponent, VisualMapComponent, TitleComponent, SVGRenderer,
]);

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });
const option = (kind: ChartKind, r: ReturnType<typeof result>, style: ChartStyleOptions = {}, dimensions?: number) =>
  buildChartOption(kind, r, style, dimensions) as Record<string, any>;
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

describe('fontProps', () => {
  it('writes only what was chosen', () => {
    expect(fontProps(undefined)).toEqual({});
    expect(fontProps(null)).toEqual({});
    expect(fontProps({})).toEqual({});
    expect(fontProps({ family: 'serif', size: 14, weight: 'bold', italic: true, color: '#123456' })).toEqual({
      fontFamily: 'serif', fontSize: 14, fontWeight: 'bold', fontStyle: 'italic', color: '#123456',
    });
    expect(fontProps({ size: 12, italic: false, family: null, color: null })).toEqual({ fontSize: 12 });
  });
});

describe('bars', () => {
  it('draws a border of the chosen style, width and colour on each bar, keeping the corners and series colours', () => {
    const o = option('Bar', byMonth, { barBorderWidth: 2, barBorderStyle: 'dashed', barBorderColor: '#c92a2a', barRadius: 4, seriesColors: { Auckland: '#2b8a3e' } }, 2);

    expect(o['series'][0].itemStyle).toEqual({ borderRadius: [4, 4, 0, 0], borderWidth: 2, borderType: 'dashed', borderColor: '#c92a2a', color: '#2b8a3e' });
    expect(o['series'][1].itemStyle).toEqual({ borderRadius: [4, 4, 0, 0], borderWidth: 2, borderType: 'dashed', borderColor: '#c92a2a' });
  });

  it('draws no border at width 0, and takes the colour of the bar when none is chosen', () => {
    expect(option('Bar', sales, { barBorderWidth: 0, barBorderStyle: 'dotted' })['series'][0].itemStyle).toBeUndefined();
    expect(option('Bar', sales, { barBorderWidth: 3 })['series'][0].itemStyle).toEqual({ borderWidth: 3, borderType: 'solid' });
  });

  it('puts the border on bars only', () => {
    expect(option('Line', sales, { barBorderWidth: 3 })['series'][0].itemStyle).toBeUndefined();
  });

  it('sets the space between the bars of a group and between groups, for vertical and horizontal bars', () => {
    const o = option('Bar', byMonth, { barGap: 10, barCategoryGap: 40 }, 2);
    const horizontal = option('BarHorizontal', byMonth, { barGap: 0, barCategoryGap: 0 }, 2);

    expect(o['series'].map((s: any) => [s.barGap, s.barCategoryGap])).toEqual([['10%', '40%'], ['10%', '40%']]);
    expect(horizontal['series'][0]).toMatchObject({ barGap: '0%', barCategoryGap: '0%' });
  });

  it('leaves the gaps to the chart when none are chosen, and puts none on lines', () => {
    expect(option('Bar', sales)['series'][0].barGap).toBeUndefined();
    expect(option('Bar', sales, { barGap: null, barCategoryGap: null })['series'][0].barCategoryGap).toBeUndefined();
    expect(option('Line', sales, { barGap: 10 })['series'][0].barGap).toBeUndefined();
  });
});

describe('value labels', () => {
  it('goes where it was put, and where the chart puts it when it was not', () => {
    expect(option('Bar', sales, { labels: true })['series'][0].label.position).toBe('top');
    expect(option('Bar', sales, { labels: true, labelPosition: 'auto' })['series'][0].label.position).toBe('top');
    expect(option('Bar', sales, { labels: true, labelPosition: 'insideTop' })['series'][0].label.position).toBe('insideTop');
    expect(option('BarHorizontal', sales, { labels: true })['series'][0].label.position).toBe('right');
    expect(option('BarHorizontal', sales, { labels: true, labelPosition: 'inside' })['series'][0].label.position).toBe('inside');
    expect(option('Bar', byMonth, { labels: true, stack: true }, 2)['series'][0].label.position).toBe('inside');
    expect(option('Line', sales, { labels: true, labelPosition: 'bottom' })['series'][0].label.position).toBe('bottom');
  });

  it('has the chosen font', () => {
    const o = option('Bar', sales, { labels: true, labelFont: { family: 'monospace', size: 11, weight: 'bold', italic: true, color: '#495057' } });

    expect(o['series'][0].label).toMatchObject({ fontFamily: 'monospace', fontSize: 11, fontWeight: 'bold', fontStyle: 'italic', color: '#495057' });
  });

  it('has no font settings when none are chosen', () => {
    const label = option('Bar', sales, { labels: true })['series'][0].label;

    expect(label.fontFamily).toBeUndefined();
    expect(label.fontSize).toBeUndefined();
  });

  it('applies to scatter points and pie slices too', () => {
    const scatter = option('Scatter', result(['c', 'x', 'y'], [['a', 1, 2]]), { labels: true, labelPosition: 'right', labelFont: { size: 9 } }, 1);
    const pie = option('Pie', sales, { labels: true, labelFont: { color: '#c92a2a' } }, 1);

    expect(scatter['series'][0].label).toMatchObject({ position: 'right', fontSize: 9 });
    expect(pie['series'][0].label).toMatchObject({ color: '#c92a2a' });
  });
});

describe('legend', () => {
  it('pages when asked to, even with few series, and is plain when asked to be, even with many', () => {
    expect(option('Bar', byMonth, { legendType: 'scroll' }, 2)['legend']).toEqual({ type: 'scroll', bottom: 0 });
    const many = result(['m', 'r', 'v'], Array.from({ length: 12 }, (_, i) => ['Jan', `S${i}`, i]));
    expect(option('Bar', many, {}, 2)['legend'].type).toBe('scroll'); // automatic
    expect(option('Bar', many, { legendType: 'plain' }, 2)['legend'].type).toBeUndefined();
    expect(option('Pie', sales, { legendType: 'plain' }, 1)['legend'].type).toBeUndefined(); // a pie pages by default
    expect(option('Pie', sales, { legendType: 'auto' }, 1)['legend'].type).toBe('scroll');
  });

  it('has the select all and invert buttons, a height and a font when they are chosen', () => {
    const legend = option('Bar', byMonth, { legendSelector: true, legendHeight: 120, legendFont: { size: 11, color: '#868e96' } }, 2)['legend'];

    expect(legend).toMatchObject({ selector: true, height: 120, textStyle: { fontSize: 11, color: '#868e96' }, bottom: 0 });
  });

  it('has none of that when it is not chosen', () => {
    const legend = option('Bar', byMonth, {}, 2)['legend'];

    expect(legend).toEqual({ bottom: 0 });
  });

  it('applies to the legend of a scatter chart too', () => {
    const points = result(['c', 'r', 'x', 'y'], [['a', 'N', 1, 2], ['b', 'S', 3, 4]]);

    expect(option('Scatter', points, { legendSelector: true }, 2)['legend']).toMatchObject({ selector: true });
  });
});

describe('axes', () => {
  it('hides the axis line, ticks and labels, but not the title, when the axis is turned off', () => {
    const o = option('Bar', sales, { xAxisTitle: 'Region', xAxis: { show: false } });

    expect(o['xAxis']).toMatchObject({ axisLine: { show: false }, axisTick: { show: false }, axisLabel: { show: false }, name: 'Region' });
  });

  it('hides just the parts that were turned off', () => {
    const o = option('Bar', sales, { yAxis: { line: false, ticks: false } });

    expect(o['yAxis']).toMatchObject({ axisLine: { show: false }, axisTick: { show: false } });
    expect(o['yAxis'].axisLabel).toBeUndefined();
    expect(option('Bar', sales, { xAxis: { labels: false } })['xAxis'].axisLabel).toMatchObject({ show: false, hideOverlap: true });
  });

  it('colours the axis line', () => {
    expect(option('Bar', sales, { xAxis: { lineColor: '#c92a2a' } })['xAxis'].axisLine).toEqual({ lineStyle: { color: '#c92a2a' } });
  });

  it('turns the line colour off with the line', () => {
    expect(option('Bar', sales, { xAxis: { line: false, lineColor: '#c92a2a' } })['xAxis'].axisLine).toEqual({ show: false });
  });

  it('gives the labels and the title their own fonts, keeping the rotation, the formatter and the title position', () => {
    const o = option('Bar', sales, {
      xAxisTitle: 'Region', yAxisTitle: 'NZD', labelRotate: 30, numberFormat: { prefix: '$' },
      xAxis: { labelFont: { size: 10, color: '#868e96' }, titleFont: { weight: 'bold' } },
      yAxis: { labelFont: { family: 'serif' }, titleFont: { size: 13 } },
    });

    expect(o['xAxis'].axisLabel).toMatchObject({ fontSize: 10, color: '#868e96', rotate: 30, hideOverlap: true });
    expect(o['xAxis'].nameTextStyle).toMatchObject({ fontWeight: 'bold' });
    expect(o['yAxis'].axisLabel.fontFamily).toBe('serif');
    expect(typeof o['yAxis'].axisLabel.formatter).toBe('function');
    expect(o['yAxis'].nameTextStyle).toMatchObject({ fontSize: 13, align: 'left' });
  });

  it('is the horizontal axis for X and the vertical axis for Y, also on a horizontal bar chart', () => {
    const o = option('BarHorizontal', sales, { xAxis: { lineColor: '#111111' }, yAxis: { lineColor: '#222222' } });

    expect(o['xAxis']).toMatchObject({ type: 'value', axisLine: { lineStyle: { color: '#111111' } } });
    expect(o['yAxis']).toMatchObject({ type: 'category', axisLine: { lineStyle: { color: '#222222' } } });
  });

  it('changes nothing with no choices', () => {
    expect(option('Bar', sales, { xAxis: {}, yAxis: null })['yAxis']).toEqual({ type: 'value' });
    expect(option('Bar', sales)['xAxis'].axisLine).toBeUndefined();
  });

  it('applies to the axes of scatter and heatmap charts', () => {
    const scatter = option('Scatter', result(['c', 'x', 'y'], [['a', 1, 2]]), { xAxis: { show: false }, yAxis: { lineColor: '#222222' } }, 1);
    const heat = option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1]]), { xAxis: { ticks: false }, yAxis: { labelFont: { size: 9 } } }, 2);

    expect(scatter['xAxis']).toMatchObject({ axisLine: { show: false }, axisLabel: { show: false } });
    expect(scatter['yAxis'].axisLine).toEqual({ lineStyle: { color: '#222222' } });
    expect(heat['xAxis'].axisTick).toEqual({ show: false });
    expect(heat['yAxis'].axisLabel).toMatchObject({ fontSize: 9 });
  });
});

describe('split lines', () => {
  it('are on the value axis only, to begin with', () => {
    expect(option('Bar', sales)['yAxis'].splitLine).toBeUndefined();
    expect(option('Bar', sales)['xAxis'].splitLine).toBeUndefined();
  });

  it('can be turned on for the category axis and off for the value axis', () => {
    const o = option('Bar', sales, { xSplitLine: { show: true }, ySplitLine: { show: false } });

    expect(o['xAxis'].splitLine).toEqual({ show: true });
    expect(o['yAxis'].splitLine).toEqual({ show: false });
  });

  it('are the value axis on the other side for a horizontal bar chart', () => {
    const o = option('BarHorizontal', sales, { xSplitLine: { show: false }, ySplitLine: { show: true } });

    expect(o['xAxis'].splitLine).toEqual({ show: false });
    expect(o['yAxis'].splitLine).toEqual({ show: true });
  });

  it('take a colour, a width and a style', () => {
    const o = option('Bar', sales, { ySplitLine: { color: '#dee2e6', width: 2, lineType: 'dashed' } });

    expect(o['yAxis'].splitLine).toEqual({ show: true, lineStyle: { color: '#dee2e6', width: 2, type: 'dashed' } });
  });

  it('keep the old Grid lines choice, which turns off the lines of the value axes', () => {
    expect(option('Bar', sales, { grid: false })['yAxis'].splitLine).toEqual({ show: false });
    expect(option('BarHorizontal', sales, { grid: false })['xAxis'].splitLine).toEqual({ show: false });
    expect(option('Scatter', result(['c', 'x', 'y'], [['a', 1, 2]]), { grid: false }, 1)['xAxis'].splitLine).toEqual({ show: false });
  });

  it('can be turned on again for one axis when Grid lines is off, and styled while it is off', () => {
    const o = option('Bar', sales, { grid: false, ySplitLine: { show: true, color: '#111111' } });

    expect(o['yAxis'].splitLine).toEqual({ show: true, lineStyle: { color: '#111111' } });
  });

  it('apply to scatter charts (both axes are value axes) and to the category axes of a heatmap', () => {
    const scatter = option('Scatter', result(['c', 'x', 'y'], [['a', 1, 2]]), { xSplitLine: { lineType: 'dotted' }, ySplitLine: { show: false } }, 1);
    const heat = option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1]]), { xSplitLine: { show: true } }, 2);

    expect(scatter['xAxis'].splitLine).toEqual({ show: true, lineStyle: { type: 'dotted' } });
    expect(scatter['yAxis'].splitLine).toEqual({ show: false });
    expect(heat['xAxis'].splitLine).toEqual({ show: true });
  });
});

describe('margin', () => {
  it('takes the space that was chosen in place of the one worked out, side by side', () => {
    const o = option('Bar', byMonth, { title: 'T', margin: { left: 40, bottom: 5 } }, 2);

    expect(o['grid']).toMatchObject({ left: 40, bottom: 5, containLabel: true });
    expect(o['grid'].top).toBe(option('Bar', byMonth, { title: 'T' }, 2)['grid'].top); // the others stay as worked out
  });

  it('can be nothing at all', () => {
    expect(option('Bar', sales, { margin: { left: 0, top: 0, right: 0, bottom: 0 } })['grid']).toMatchObject({ left: 0, top: 0, right: 0, bottom: 0 });
  });

  it('leaves it worked out with no choices', () => {
    expect(option('Bar', sales, { margin: null })['grid']).toEqual(option('Bar', sales)['grid']);
    expect(option('Bar', sales, { margin: { left: null } })['grid']).toEqual(option('Bar', sales)['grid']);
  });

  it('applies to scatter and heatmap charts too', () => {
    expect(option('Scatter', result(['c', 'x', 'y'], [['a', 1, 2]]), { margin: { right: 70 } }, 1)['grid'].right).toBe(70);
    expect(option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1]]), { margin: { top: 60 } }, 2)['grid'].top).toBe(60);
  });
});

describe('drawn by ECharts', () => {
  const everything: ChartStyleOptions = {
    labels: true, barBorderWidth: 2, barBorderStyle: 'dotted', barBorderColor: '#c92a2a', barGap: 5, barCategoryGap: 30,
    labelPosition: 'insideTop', labelFont: { family: 'serif', size: 12, weight: 'bold', italic: true, color: '#fff' },
    legendType: 'scroll', legendSelector: true, legendHeight: 100, legendFont: { size: 11 }, legendPosition: 'right',
    xAxis: { line: false, ticks: false, lineColor: '#868e96', labelFont: { size: 10 }, titleFont: { weight: 'bold' } }, xAxisTitle: 'Month',
    yAxis: { labels: true, lineColor: '#868e96' }, yAxisTitle: 'NZD',
    xSplitLine: { show: true, color: '#dee2e6', width: 1, lineType: 'dashed' }, ySplitLine: { color: '#e9ecef', lineType: 'dotted' },
    margin: { left: 50, top: 40, right: 30, bottom: 60 }, title: 'Revenue',
  };

  it.each(['Bar', 'BarHorizontal', 'Line', 'Area'] as ChartKind[])('%s with every choice made', (kind) => {
    expect(draw(option(kind, byMonth, everything, 2))).toContain('<svg');
  });

  it('a scatter and a heatmap with axis, split line and margin choices', () => {
    expect(draw(option('Scatter', result(['c', 'r', 'x', 'y'], [['a', 'N', 1, 2], ['b', 'S', 3, 4]]), everything, 2))).toContain('<svg');
    expect(draw(option('Heatmap', result(['a', 'b', 'n'], [['x', 'p', 1], ['y', 'q', 4]]), everything, 2))).toContain('<svg');
  });
});

describe('pie labels', () => {
  const slice = { name: 'Auckland', value: 1200, percent: 25.04 };
  const label = (style: ChartStyleOptions) => option('Pie', sales, { labels: true, ...style }, 1)['series'][0].label.formatter as (p: typeof slice) => string;

  it('reads the parts from the older single choice while none of the three was chosen', () => {
    expect(pieParts({})).toEqual({ name: true, value: false, percent: false });
    expect(pieParts({ pieLabel: 'value' })).toEqual({ name: false, value: true, percent: false });
    expect(pieParts({ pieLabel: 'percent' })).toEqual({ name: false, value: false, percent: true });
    expect(pieParts({ pieLabel: 'name-value' })).toEqual({ name: true, value: true, percent: false });
    expect(pieParts({ pieLabel: 'name-percent' })).toEqual({ name: true, value: false, percent: true });
  });

  it('takes the three choices instead once any was made, a missing one counting as off', () => {
    expect(pieParts({ pieLabel: 'name-percent', pieShowValue: true })).toEqual({ name: false, value: true, percent: false });
    expect(pieParts({ pieShowName: true, pieShowValue: null, pieShowPercent: true })).toEqual({ name: true, value: false, percent: true });
  });

  it('writes the parts that are on: name: value (percent)', () => {
    const on = (name: boolean, value: boolean, percent: boolean) => label({ pieShowName: name, pieShowValue: value, pieShowPercent: percent, numberFormat: { prefix: '$' } })(slice);

    expect(on(true, false, false)).toBe('Auckland');
    expect(on(false, true, false)).toBe('$1,200');
    expect(on(false, false, true)).toBe('25%');
    expect(on(true, true, false)).toBe('Auckland: $1,200');
    expect(on(true, false, true)).toBe('Auckland (25%)');
    expect(on(false, true, true)).toBe('$1,200 (25%)');
    expect(on(true, true, true)).toBe('Auckland: $1,200 (25%)');
    expect(on(false, false, false)).toBe('');
  });

  it('puts the label where it was told and leaves it to the chart when not', () => {
    expect(option('Pie', sales, { labelPosition: 'inside' }, 1)['series'][0].label.position).toBe('inside');
    expect(option('Donut', sales, { labelPosition: 'center' }, 1)['series'][0].label.position).toBe('center');
    expect(option('Pie', sales, { labelPosition: 'outside' }, 1)['series'][0].label.position).toBe('outside');
    expect(option('Pie', sales, { labelPosition: 'auto' }, 1)['series'][0].label.position).toBeUndefined();
    expect(option('Pie', sales, { labelPosition: 'top' }, 1)['series'][0].label.position).toBeUndefined(); // that is for charts with axes
  });

  it('shows every label, even where they overlap, when forced', () => {
    expect(option('Pie', sales, { pieForceLabels: true }, 1)['series'][0].avoidLabelOverlap).toBe(false);
    expect(option('Pie', sales, {}, 1)['series'][0].avoidLabelOverlap).toBeUndefined();
  });
});

describe('margin units', () => {
  it('writes percent as text, which ECharts reads as a share of the chart', () => {
    const o = option('Bar', sales, { margin: { left: 5, right: 5, top: 5, bottom: 5, unit: '%' } });

    expect(o['grid']).toMatchObject({ left: '5%', right: '5%', top: '5%', bottom: '5%' });
  });

  it('writes pixels as numbers, also when the unit is spelt out', () => {
    expect(option('Bar', sales, { margin: { left: 5, unit: 'px' } })['grid'].left).toBe(5);
  });

  it('keeps labels inside the plot unless told otherwise', () => {
    expect(option('Bar', sales)['grid'].containLabel).toBe(true);
    expect(option('Bar', sales, { margin: { left: 5 } })['grid'].containLabel).toBe(true);
    expect(option('Bar', sales, { margin: { containLabel: false } })['grid'].containLabel).toBe(false);
  });

  it('is the box a pie sits in, in either unit, in place of the centring', () => {
    const percent = option('Pie', sales, { margin: { left: 5, right: 5, top: 10, bottom: 10, unit: '%' }, legendPosition: 'right' }, 1)['series'][0];
    const pixels = option('Pie', sales, { margin: { left: 20 } }, 1)['series'][0];

    expect(percent).toMatchObject({ left: '5%', right: '5%', top: '10%', bottom: '10%' });
    expect(percent.center).toBeUndefined();
    expect(pixels.left).toBe(20);
    expect(pixels.top).toBeUndefined();
  });

  it('leaves a pie centred away from the legend when no margin is chosen', () => {
    expect(option('Pie', sales, { legendPosition: 'right' }, 1)['series'][0].center).toBeDefined();
  });

  it('is drawn by ECharts', () => {
    expect(draw(option('Pie', sales, { margin: { left: 5, right: 5, top: 5, bottom: 5, unit: '%' }, labels: true, labelPosition: 'outside', pieShowName: true, pieShowPercent: true, pieForceLabels: true }, 1))).toContain('<svg');
    expect(draw(option('Bar', sales, { margin: { left: 10, unit: '%', containLabel: false } }))).toContain('<svg');
  });
});