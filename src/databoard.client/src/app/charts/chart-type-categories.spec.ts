import {
  ALL_CATEGORY, CHART_CATEGORIES, MAX_RECENT, RECENT_CATEGORY, readRecent, readTab, typesIn, withRecent, writeRecent, writeTab,
} from './chart-type-categories';
import { CHART_TYPES, ChartType } from './charts.service';

describe('chart categories', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('starts with All and Recent, then the kinds of chart', () => {
    expect(CHART_CATEGORIES.map((c) => c.label)).toEqual([
      'All', 'Recent', 'Table', 'Bar Chart', 'Line Chart', 'Pie Chart', 'Scatter Chart', 'Heatmap', 'Score Card',
    ]);
  });

  it('puts every chart type in exactly one kind of chart, and all of them under All', () => {
    const kinds = CHART_CATEGORIES.filter((c) => c.id !== ALL_CATEGORY && c.id !== RECENT_CATEGORY);
    const listed = kinds.flatMap((c) => c.types);

    expect([...listed].sort()).toEqual(CHART_TYPES.map((t) => t.value).sort());
    expect(new Set(listed).size).toBe(listed.length);
    expect(typesIn(ALL_CATEGORY, [])).toEqual(CHART_TYPES.map((t) => t.value));
  });

  it('groups the types the way a person thinks of them', () => {
    expect(typesIn('bar', [])).toEqual(['Bar', 'BarHorizontal']);
    expect(typesIn('line', [])).toEqual(['Line', 'Area']);
    expect(typesIn('pie', [])).toEqual(['Pie', 'Donut']);
    expect(typesIn('scatter', [])).toEqual(['Scatter']);
    expect(typesIn('score', [])).toEqual(['Number']);
    expect(typesIn('table', [])).toEqual(['Table']);
    expect(typesIn('heatmap', [])).toEqual(['Heatmap']);
  });

  it('shows what was picked lately under Recent, newest first, and shows All for a tab it does not know', () => {
    expect(typesIn(RECENT_CATEGORY, ['Pie', 'Bar'])).toEqual(['Pie', 'Bar']);
    expect(typesIn(RECENT_CATEGORY, [])).toEqual([]);
    expect(typesIn('nonsense', [])).toEqual(typesIn(ALL_CATEGORY, []));
  });

  it('drops from Recent a type that no longer exists', () => {
    expect(typesIn(RECENT_CATEGORY, ['Pie', 'Radar' as ChartType])).toEqual(['Pie']);
  });

  describe('withRecent', () => {
    it('puts the type first and lists it once', () => {
      expect(withRecent(['Bar', 'Pie'], 'Line')).toEqual(['Line', 'Bar', 'Pie']);
      expect(withRecent(['Bar', 'Pie', 'Line'], 'Pie')).toEqual(['Pie', 'Bar', 'Line']);
    });

    it('stays short', () => {
      const many: ChartType[] = ['Bar', 'Line', 'Pie', 'Donut', 'Area', 'Table'];

      const next = withRecent(many, 'Scatter');

      expect(next.length).toBe(MAX_RECENT);
      expect(next[0]).toBe('Scatter');
      expect(next).not.toContain('Table'); // the oldest goes
    });
  });

  describe('what is remembered in the browser', () => {
    it('is nothing to begin with', () => {
      expect(readRecent()).toEqual([]);
      expect(readTab()).toBe(ALL_CATEGORY);
    });

    it('is kept and read back', () => {
      writeRecent(['Pie', 'Bar']);
      writeTab('pie');

      expect(readRecent()).toEqual(['Pie', 'Bar']);
      expect(readTab()).toBe('pie');
    });

    it('ignores what is not usable: bad JSON, the wrong shape, unknown types and tabs', () => {
      localStorage.setItem('databoard.recentChartTypes', '{ nope');
      expect(readRecent()).toEqual([]);
      localStorage.setItem('databoard.recentChartTypes', '{"a":1}');
      expect(readRecent()).toEqual([]);
      localStorage.setItem('databoard.recentChartTypes', '["Pie","Radar",7,"Bar"]');
      expect(readRecent()).toEqual(['Pie', 'Bar']);
      localStorage.setItem('databoard.chartTypeTab', 'gone');
      expect(readTab()).toBe(ALL_CATEGORY);
    });

    it('never remembers more than the limit, even if more was stored', () => {
      localStorage.setItem('databoard.recentChartTypes', JSON.stringify(CHART_TYPES.map((t) => t.value)));

      expect(readRecent().length).toBe(MAX_RECENT);
    });

    it('copes when the browser will not store anything', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });

      expect(readRecent()).toEqual([]);
      expect(readTab()).toBe(ALL_CATEGORY);
      expect(() => writeRecent(['Pie'])).not.toThrow();
      expect(() => writeTab('pie')).not.toThrow();
    });
  });
});
