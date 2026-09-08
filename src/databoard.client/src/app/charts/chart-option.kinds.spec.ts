import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, ChartStyleOptions, PALETTES, buildChartOption } from './chart-option';

echarts.use([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, AriaComponent, SVGRenderer]);

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });
const option = (kind: ChartKind, r: ReturnType<typeof result>, style: ChartStyleOptions = {}) =>
  buildChartOption(kind, r, style) as Record<string, any>;

const byRegion = result(['region', 'revenue', 'orders'], [['Auckland', 2480, 3], ['Wellington', 900, 2]]);
const sales = result(['region', 'revenue'], [['Auckland', 30], ['Nelson', 10]]);

describe('more chart kinds', () => {
  it('draws an area chart as a line with the area filled', () => {
    const o = option('Area', byRegion);

    expect(o['series'].map((s: any) => s.type)).toEqual(['line', 'line']);
    expect(o['series'].every((s: any) => s.areaStyle)).toBe(true);
    expect(o['xAxis'].type).toBe('category');
  });

  it('draws a horizontal bar with the categories on the vertical axis, first one at the top', () => {
    const o = option('BarHorizontal', byRegion);

    expect(o['series'][0].type).toBe('bar');
    expect(o['yAxis'].type).toBe('category');
    expect(o['yAxis'].data).toEqual(['Auckland', 'Wellington']);
    expect(o['yAxis'].inverse).toBe(true);
    expect(o['xAxis'].type).toBe('value');
  });

  it('draws a donut as a pie with a hole, and a pie without one', () => {
    expect(option('Donut', sales)['series'][0].radius[0]).not.toBe('0%');
    expect(option('Pie', sales)['series'][0].radius[0]).toBe('0%');
    expect(option('Donut', sales)['series'][0].data).toEqual([{ name: 'Auckland', value: 30 }, { name: 'Nelson', value: 10 }]);
  });

  it('uses item tooltips for round charts and axis tooltips for the rest', () => {
    expect(option('Donut', sales)['tooltip'].trigger).toBe('item');
    expect(option('Area', byRegion)['tooltip'].trigger).toBe('axis');
  });
});

describe('chart style', () => {
  it('shows value labels only when asked', () => {
    expect(option('Bar', byRegion)['series'][0].label.show).toBe(false);
    expect(option('Bar', byRegion, { labels: true })['series'][0].label.show).toBe(true);
    expect(option('Pie', sales, { labels: true })['series'][0].label.show).toBe(true);
  });

  it('hides the legend when it is switched off, and shows it for several series otherwise', () => {
    expect(option('Bar', byRegion)['legend']).toBeDefined();
    expect(option('Bar', byRegion, { legend: false })['legend']).toBeUndefined();
    expect(option('Pie', sales)['legend']).toBeDefined();
    expect(option('Pie', sales, { legend: false })['legend']).toBeUndefined();
  });

  it('applies a palette, and leaves the default colours alone', () => {
    expect(option('Bar', byRegion, { palette: 'warm' })['color']).toEqual(PALETTES['warm']);
    expect(option('Bar', byRegion, { palette: 'default' })['color']).toBeUndefined();
    expect(option('Bar', byRegion, { palette: 'unknown' })['color']).toBeUndefined();
    expect(option('Donut', sales, { palette: 'mono' })['color']).toEqual(PALETTES['mono']);
  });

  it('every named palette has several distinct colours', () => {
    for (const [name, colours] of Object.entries(PALETTES)) {
      if (name === 'default') continue;
      expect(colours!.length).toBeGreaterThanOrEqual(5);
      expect(new Set(colours).size).toBe(colours!.length);
    }
  });
});

describe('new kinds in real ECharts', () => {
  const render = (kind: ChartKind, style: ChartStyleOptions = {}): string => {
    const chart = echarts.init(null, undefined, { renderer: 'svg', ssr: true, width: 400, height: 300 });
    chart.setOption(buildChartOption(kind, byRegion, style));
    const svg = chart.renderToSVGString();
    chart.dispose();
    return svg;
  };

  it.each(['Area', 'BarHorizontal', 'Donut', 'Pie', 'Bar', 'Line'] as ChartKind[])('renders %s with its categories', (kind) => {
    const svg = render(kind, { labels: true, palette: 'cool', legend: true });

    expect(svg).toContain('<svg');
    expect(svg).toContain('Auckland');
  });
});
