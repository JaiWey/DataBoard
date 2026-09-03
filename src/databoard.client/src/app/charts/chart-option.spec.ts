import { buildChartOption } from './chart-option';

const result = (columns: string[], rows: unknown[][]) => ({ columns: columns.map((name) => ({ name })), rows });

// The options are plain objects; reading them back as records keeps the tests free of ECharts' own types.
const option = (kind: 'Bar' | 'Line' | 'Pie', r: ReturnType<typeof result>) =>
  buildChartOption(kind, r) as Record<string, any>;

describe('buildChartOption', () => {
  const byRegion = result(['region', 'revenue', 'orders'], [['Auckland', 2480, 3], ['Wellington', 900.5, 2], [null, 10, 1]]);

  it('draws the first column as categories and every other column as a series', () => {
    const o = option('Bar', byRegion);

    expect(o['xAxis'].data).toEqual(['Auckland', 'Wellington', '∅']);
    expect(o['series'].map((s: any) => [s.type, s.name, s.data])).toEqual([
      ['bar', 'revenue', [2480, 900.5, 10]],
      ['bar', 'orders', [3, 2, 1]],
    ]);
    expect(o['legend']).toBeDefined(); // two series need a legend
  });

  it('uses line series for line charts and omits the legend for one series', () => {
    const o = option('Line', result(['month', 'revenue'], [['2026-01', 1], ['2026-02', 2]]));

    expect(o['series'][0].type).toBe('line');
    expect(o['legend']).toBeUndefined();
  });

  it('turns numeric text and missing values into numbers and gaps', () => {
    // numeric columns can arrive as strings; nulls and non-numbers become gaps instead of zeros
    const o = option('Bar', result(['k', 'v'], [['a', '12.5'], ['b', null], ['c', 'n/a']]));

    expect(o['series'][0].data).toEqual([12.5, null, null]);
  });

  it('draws a pie from the first measure and skips slices that cannot be drawn', () => {
    const o = option('Pie', result(['region', 'revenue'], [['Auckland', 30], ['Dunedin', 0], ['Wellington', -5], ['Nelson', 10]]));

    expect(o['series'][0].type).toBe('pie');
    expect(o['series'][0].data).toEqual([{ name: 'Auckland', value: 30 }, { name: 'Nelson', value: 10 }]);
  });

  it('draws a single-column result as one value', () => {
    const o = option('Bar', result(['orders'], [[8]]));

    expect(o['xAxis'].data).toEqual(['orders']);
    expect(o['series'][0].data).toEqual([8]);
  });

  it('returns an empty chart for an empty result', () => {
    expect(option('Bar', result(['a', 'b'], []))['series']).toEqual([]);
    expect(option('Pie', result([], []))['series']).toEqual([]);
  });

  it('enables the accessibility description', () => {
    expect(option('Bar', byRegion)['aria'].enabled).toBe(true);
  });
});
