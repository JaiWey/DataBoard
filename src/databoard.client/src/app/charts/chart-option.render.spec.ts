import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import { ChartKind, buildChartOption } from './chart-option';

echarts.use([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, AriaComponent, SVGRenderer]);

/** Renders the options with the real ECharts (server-side SVG, no canvas needed) to prove it accepts them. */
const render = (kind: ChartKind, columns: string[], rows: unknown[][]): string => {
  const chart = echarts.init(null, undefined, { renderer: 'svg', ssr: true, width: 400, height: 300 });
  chart.setOption(buildChartOption(kind, { columns: columns.map((name) => ({ name })), rows }));
  const svg = chart.renderToSVGString();
  chart.dispose();
  return svg;
};

describe('chart options in real ECharts', () => {
  const rows = [['Auckland', 2480], ['Wellington', 900]];

  it('renders a bar chart with its category labels', () => {
    const svg = render('Bar', ['region', 'revenue'], rows);

    expect(svg).toContain('<svg');
    expect(svg).toContain('Auckland');
    expect(svg).toContain('Wellington');
  });

  it('renders a line chart', () => {
    expect(render('Line', ['region', 'revenue'], rows)).toContain('Auckland');
  });

  it('renders a pie chart with a legend of its slices', () => {
    const svg = render('Pie', ['region', 'revenue'], rows);

    expect(svg).toContain('Auckland');
    expect(svg).toContain('Wellington');
  });

  it('renders several series with a legend', () => {
    const svg = render('Bar', ['region', 'revenue', 'orders'], [['Auckland', 2480, 3], ['Wellington', 900, 2]]);

    expect(svg).toContain('revenue');
    expect(svg).toContain('orders');
  });

  it('renders an empty result without throwing', () => {
    expect(render('Bar', ['region', 'revenue'], [])).toContain('<svg');
  });
});
