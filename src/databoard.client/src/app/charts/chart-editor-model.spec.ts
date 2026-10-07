import { Dataset, DatasetColumn } from '../organizations/datasets.service';
import {
  EditorState,
  aggregationsFor,
  defaultAggregation,
  defaultZone,
  dimensionFor,
  emptyEditorState,
  fieldBadge,
  filterFor,
  fromChart,
  metricFor,
  problemsOf,
  resultNames,
  rowCountMetric,
  toQuery,
  toSaveChart,
} from './chart-editor-model';
import { Chart, defaultChartStyle } from './charts.service';

const region: DatasetColumn = { name: 'region', dataType: 'text', role: 'Dimension' };
const price: DatasetColumn = { name: 'price', dataType: 'numeric(10,2)', role: 'Measure' };
const ordered: DatasetColumn = { name: 'ordered_on', dataType: 'date', role: 'Dimension' };

const dataset = (): Dataset => ({
  id: 'ds-1', dataSourceId: 'src', name: 'Orders', description: null, sql: null,
  columns: [region, price, ordered], kind: 'Sql', definition: null, createdUtc: '', updatedUtc: '',
});

const state = (changes: Partial<EditorState> = {}): EditorState => ({ ...emptyEditorState('ds-1'), name: 'Revenue', ...changes });

describe('fields', () => {
  it('badges a field as text, number or date', () => {
    expect([region, price, ordered].map(fieldBadge)).toEqual(['Str', '123', 'Date']);
  });

  it('sends numbers to the metrics and everything else to the dimensions when clicked', () => {
    expect([region, price, ordered].map(defaultZone)).toEqual(['dimensions', 'metrics', 'dimensions']);
  });

  it('starts a number as a sum and anything else as a count', () => {
    expect(defaultAggregation(price)).toBe('Sum');
    expect(defaultAggregation(region)).toBe('Count');
  });

  it('only offers aggregations that make sense for the field', () => {
    expect(aggregationsFor('numeric', 'Measure')).toContain('Sum');
    expect(aggregationsFor('text', 'Dimension')).toEqual(['Count', 'CountDistinct']);
    expect(aggregationsFor('date', 'Dimension')).toEqual(['Min', 'Max', 'Count', 'CountDistinct']);
    expect(aggregationsFor(null, null)).toEqual(['Count']);
  });

  it('groups a date by month to begin with, and other fields by their value', () => {
    expect(dimensionFor(ordered).grain).toBe('Month');
    expect(dimensionFor(region).grain).toBe('');
  });
});

describe('result names', () => {
  it('names metrics after their aggregation and field, and counts rows as "count"', () => {
    const names = resultNames({ dimensions: [dimensionFor(region)], metrics: [metricFor(price), rowCountMetric()] });

    expect(names).toEqual({ dimensions: ['region'], metrics: ['sum_price', 'count'] });
  });

  it('keeps names unique, ignoring case', () => {
    const names = resultNames({
      dimensions: [dimensionFor(region)],
      metrics: [metricFor(price), metricFor(price), { ...rowCountMetric(), column: 'REGION', aggregation: 'Count' }],
    });

    expect(names.metrics).toEqual(['sum_price', 'sum_price_2', 'count_REGION']);
  });
});

describe('toQuery', () => {
  it('turns dimensions, metrics, sorting and the limit into a chart query', () => {
    const query = toQuery(state({
      dimensions: [dimensionFor(region), dimensionFor(ordered)],
      metrics: [metricFor(price), rowCountMetric()],
      sortBy: 'sum_price',
      sortDirection: 'Desc',
      limit: 20,
    }));

    expect(query).toEqual({
      dimensions: [{ column: 'region', alias: 'region' }, { column: 'ordered_on', alias: 'ordered_on', grain: 'Month' }],
      measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }, { aggregation: 'Count', alias: 'count' }],
      orderBy: [{ field: 'sum_price', direction: 'Desc' }],
      limit: 20,
    });
  });

  it('leaves out the sorting and the limit when they are not set', () => {
    const query = toQuery(state({ metrics: [rowCountMetric()] }));

    expect(query.orderBy).toBeUndefined();
    expect(query.limit).toBeUndefined();
    expect(query.filters).toBeUndefined();
  });

  it('sends single values as text, lists as lists, and no value for empty checks', () => {
    const query = toQuery(state({
      metrics: [rowCountMetric()],
      filters: [
        { ...filterFor(region), operator: 'Eq', value: ' Auckland ' },
        { ...filterFor(region), operator: 'In', value: 'Auckland, Dunedin,, ' },
        { ...filterFor(price), operator: 'Between', value: '10, 20' },
        { ...filterFor(region), operator: 'IsNull', value: 'ignored' },
      ],
    }));

    expect(query.filters).toEqual([
      { column: 'region', operator: 'Eq', value: 'Auckland' },
      { column: 'region', operator: 'In', values: ['Auckland', 'Dunedin'] },
      { column: 'price', operator: 'Between', values: ['10', '20'] },
      { column: 'region', operator: 'IsNull' },
    ]);
  });

  it('sends a series limit only for a chart with an axis and a second dimension', () => {
    const two = { dimensions: [dimensionFor(ordered), dimensionFor(region)], metrics: [metricFor(price)], seriesLimit: 5 };

    expect(toQuery(state({ ...two, type: 'Line' })).seriesLimit).toBe(5);
    expect(toQuery(state({ ...two, type: 'Pie' })).seriesLimit).toBeUndefined();
    expect(toQuery(state({ ...two, type: 'Line', dimensions: [dimensionFor(ordered)] })).seriesLimit).toBeUndefined();
    expect(toQuery(state({ ...two, type: 'Line', seriesLimit: null })).seriesLimit).toBeUndefined();
  });

  it('sends the values chosen for the dataset parameters, and nothing when none were chosen', () => {
    const chosen = toQuery(state({ metrics: [rowCountMetric()], parameters: { region: 'Wellington', min_price: 250, paid: false } }));

    expect(chosen.parameters).toEqual({ region: 'Wellington', min_price: 250, paid: false });
    expect(toQuery(state({ metrics: [rowCountMetric()] })).parameters).toBeUndefined();
  });

  it('skips filters that were added but not filled in yet', () => {
    const query = toQuery(state({ metrics: [rowCountMetric()], filters: [{ ...filterFor(region), value: '  ' }] }));

    expect(query.filters).toBeUndefined();
  });
});

describe('toSaveChart', () => {
  it('trims the name, nulls a blank description and carries the style', () => {
    const save = toSaveChart(state({ name: '  Revenue  ', description: '   ', type: 'Donut', metrics: [rowCountMetric()] }));

    expect(save.name).toBe('Revenue');
    expect(save.description).toBeNull();
    expect(save.type).toBe('Donut');
    expect(save.datasetId).toBe('ds-1');
    expect(save.definition.style).toEqual({ legend: true, labels: false, palette: 'default', stack: false, percent: false });
  });

  it('keeps 100% only for charts that stack, so a leftover from another type is not sent', () => {
    const percent = { ...emptyEditorState('ds-1'), name: 'x', style: { ...defaultChartStyle(), stack: true, percent: true } };

    expect(toSaveChart({ ...percent, type: 'Area' }).definition.style).toMatchObject({ stack: true, percent: true });
    expect(toSaveChart({ ...percent, type: 'BarHorizontal' }).definition.style.percent).toBe(true);
    expect(toSaveChart({ ...percent, type: 'Pie' }).definition.style.percent).toBe(false);
    expect(toSaveChart({ ...percent, type: 'Heatmap' }).definition.style.percent).toBe(false);
    expect(toSaveChart({ ...percent, type: 'Table' }).definition.style.percent).toBe(false);
  });
});

describe('fromChart', () => {
  const saved = (): Chart => ({
    id: 'c1', name: 'Revenue', description: 'Per region', datasetId: 'ds-1', type: 'Line',
    createdUtc: '', updatedUtc: '',
    definition: {
      style: { legend: false, labels: true, palette: 'cool', stack: false, percent: false },
      query: {
        dimensions: [{ column: 'region', alias: 'region', grain: null }, { column: 'ordered_on', alias: 'ordered_on', grain: 'Year' }],
        measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }, { aggregation: 'Count', column: null, alias: 'count' }],
        filters: [{ column: 'region', operator: 'In', values: ['Auckland', 'Dunedin'] }, { column: 'price', operator: 'Gte', value: 10 }],
        orderBy: [{ field: 'count', direction: 'Asc' }],
        limit: 50,
      },
    },
  });

  it('rebuilds what the chart was made from', () => {
    const s = fromChart(saved(), dataset());

    expect(s.name).toBe('Revenue');
    expect(s.type).toBe('Line');
    expect(s.dimensions.map((d) => [d.column, d.grain])).toEqual([['region', ''], ['ordered_on', 'Year']]);
    expect(s.metrics.map((m) => [m.column, m.aggregation])).toEqual([['price', 'Sum'], [null, 'Count']]);
    expect(s.filters.map((f) => [f.column, f.operator, f.value])).toEqual([['region', 'In', 'Auckland, Dunedin'], ['price', 'Gte', '10']]);
    expect(s.sortBy).toBe('count');
    expect(s.sortDirection).toBe('Asc');
    expect(s.limit).toBe(50);
    expect(s.style).toEqual({ legend: false, labels: true, palette: 'cool', stack: false, percent: false });
  });

  it('brings back the parameter values, and does not share them with the saved chart', () => {
    const chart = saved();
    chart.definition.query.parameters = { region: 'Dunedin', min_price: 10 };

    const s = fromChart(chart, dataset());
    s.parameters['region'] = 'changed';

    expect(s.parameters).toEqual({ region: 'changed', min_price: 10 });
    expect(chart.definition.query.parameters).toEqual({ region: 'Dunedin', min_price: 10 });
    expect(toQuery(fromChart(chart, dataset())).parameters).toEqual({ region: 'Dunedin', min_price: 10 });
    expect(fromChart(saved(), dataset()).parameters).toEqual({}); // a chart without any
  });

  it('brings back the series limit', () => {
    const chart = saved();
    chart.definition.query.seriesLimit = 7;

    expect(fromChart(chart, dataset()).seriesLimit).toBe(7);
    expect(toQuery(fromChart(chart, dataset())).seriesLimit).toBe(7);
  });

  it('round-trips: saving what was loaded gives the same query', () => {
    const chart = saved();

    expect(toQuery(fromChart(chart, dataset()))).toEqual({
      ...chart.definition.query,
      dimensions: [{ column: 'region', alias: 'region' }, { column: 'ordered_on', alias: 'ordered_on', grain: 'Year' }],
      measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }, { aggregation: 'Count', alias: 'count' }],
      filters: [{ column: 'region', operator: 'In', values: ['Auckland', 'Dunedin'] }, { column: 'price', operator: 'Gte', value: '10' }],
    });
  });

  it('leaves out fields the dataset no longer has, and a sort that no longer names anything', () => {
    const smaller: Dataset = { ...dataset(), columns: [region] };

    const s = fromChart(saved(), smaller);

    expect(s.dimensions.map((d) => d.column)).toEqual(['region']);
    expect(s.metrics.map((m) => m.column)).toEqual([null]); // the row count has no field to lose
    expect(s.filters.map((f) => f.column)).toEqual(['region']);
    expect(s.sortBy).toBe('count'); // still a name in the result
  });

  it('copes with a chart whose dataset could not be found', () => {
    const s = fromChart(saved(), undefined);

    expect(s.dimensions).toEqual([]);
    expect(s.sortBy).toBe('count');
  });

  it('fills in a style the chart does not have', () => {
    const chart = saved();
    (chart.definition as { style: unknown }).style = {};

    expect(fromChart(chart, dataset()).style).toEqual({ legend: true, labels: false, palette: 'default', stack: false, percent: false });
  });
});

describe('problemsOf', () => {
  it('asks for something to show before anything else', () => {
    expect(problemsOf(state())).toContain('Drag a field into Dimensions or Metrics.');
  });

  it('asks for a name only when saving', () => {
    const s = state({ name: '', metrics: [rowCountMetric()] });

    expect(problemsOf(s)).toEqual([]);
    expect(problemsOf(s, { forSave: true })).toEqual(['Give the chart a name.']);
  });

  it('needs a dataset', () => {
    expect(problemsOf(state({ datasetId: '', metrics: [rowCountMetric()] }))).toContain('Choose a dataset.');
  });

  it('wants a metric for charts that plot numbers, but not for a table', () => {
    const grouped = state({ dimensions: [dimensionFor(region)] });

    expect(problemsOf({ ...grouped, type: 'Bar' })).toContain('This chart type needs at least one metric.');
    expect(problemsOf({ ...grouped, type: 'Table' })).toEqual([]);
  });

  it('wants a number to be exactly one metric', () => {
    const one = state({ type: 'Number', metrics: [rowCountMetric()] });

    expect(problemsOf(one)).toEqual([]);
    expect(problemsOf({ ...one, metrics: [rowCountMetric(), metricFor(price)] })).toContain('A number shows exactly one metric and no dimensions.');
    expect(problemsOf({ ...one, dimensions: [dimensionFor(region)] })).toContain('A number shows exactly one metric and no dimensions.');
  });

  it('wants a word cloud to have one dimension and one metric', () => {
    const ok = state({ type: 'WordCloud', dimensions: [dimensionFor(region)], metrics: [metricFor(price)] });
    const message = 'A word cloud needs one dimension (the word) and one metric (how big it is).';

    expect(problemsOf(ok)).toEqual([]);
    expect(problemsOf({ ...ok, dimensions: [] })).toContain(message);
    expect(problemsOf({ ...ok, dimensions: [...ok.dimensions, dimensionFor(ordered)] })).toContain(message);
    expect(problemsOf({ ...ok, metrics: [] })).toContain(message);
    expect(problemsOf({ ...ok, metrics: [metricFor(price), rowCountMetric()] })).toContain(message);
  });

  it('wants a heatmap to have two dimensions and one metric', () => {
    const ok = state({ type: 'Heatmap', dimensions: [dimensionFor(region), dimensionFor(ordered)], metrics: [metricFor(price)] });
    const message = 'A heatmap needs two dimensions (across, then down) and one metric (the colour).';

    expect(problemsOf(ok)).toEqual([]);
    expect(problemsOf({ ...ok, dimensions: [dimensionFor(region)] })).toContain(message);
    expect(problemsOf({ ...ok, dimensions: [...ok.dimensions, dimensionFor(region)] })).toContain(message);
    expect(problemsOf({ ...ok, metrics: [] })).toContain(message);
    expect(problemsOf({ ...ok, metrics: [metricFor(price), rowCountMetric()] })).toContain(message);
  });

  it('wants a scatter chart to have a dimension for the points and two or three metrics', () => {
    const ok = state({ type: 'Scatter', dimensions: [dimensionFor(region)], metrics: [metricFor(price), rowCountMetric()] });

    expect(problemsOf(ok)).toEqual([]);
    expect(problemsOf({ ...ok, metrics: [...ok.metrics, metricFor(price)] })).toEqual([]); // the third is the size
    expect(problemsOf({ ...ok, dimensions: [...ok.dimensions, dimensionFor(ordered)] })).toEqual([]); // the second is the colour
    expect(problemsOf({ ...ok, dimensions: [] })).toEqual(expect.arrayContaining([expect.stringContaining('one dimension')]));
    expect(problemsOf({ ...ok, dimensions: [dimensionFor(region), dimensionFor(ordered), dimensionFor(region)] })).toHaveLength(1);
    expect(problemsOf({ ...ok, metrics: [metricFor(price)] })).toEqual(expect.arrayContaining([expect.stringContaining('two metrics')]));
    expect(problemsOf({ ...ok, metrics: [...ok.metrics, metricFor(price), rowCountMetric()] })).toHaveLength(1);
  });

  it('keeps the row limit within what the server accepts', () => {
    const s = state({ metrics: [rowCountMetric()] });

    expect(problemsOf({ ...s, limit: 5000 })).toEqual([]);
    expect(problemsOf({ ...s, limit: 0 })).toHaveLength(1);
    expect(problemsOf({ ...s, limit: 5001 })).toHaveLength(1);
    expect(problemsOf({ ...s, limit: 2.5 })).toHaveLength(1);
  });

  it('keeps the series limit within what the server accepts', () => {
    const s = state({ metrics: [rowCountMetric()] });

    expect(problemsOf({ ...s, seriesLimit: 100 })).toEqual([]);
    expect(problemsOf({ ...s, seriesLimit: 0 })).toHaveLength(1);
    expect(problemsOf({ ...s, seriesLimit: 101 })).toHaveLength(1);
    expect(problemsOf({ ...s, seriesLimit: 1.5 })).toHaveLength(1);
  });
});
