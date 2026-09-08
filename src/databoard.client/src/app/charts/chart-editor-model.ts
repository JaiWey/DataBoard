import { Aggregation, ChartQuery, Dataset, DatasetColumn, TimeGrain } from '../organizations/datasets.service';
import { Chart, ChartDefinition, ChartStyle, ChartType, SaveChart, defaultChartStyle } from './charts.service';

export type FilterOperator =
  | 'Eq' | 'NotEq' | 'Gt' | 'Gte' | 'Lt' | 'Lte' | 'In' | 'NotIn' | 'Between' | 'Contains' | 'StartsWith' | 'IsNull' | 'IsNotNull';

export const FILTER_OPERATORS: { value: FilterOperator; label: string }[] = [
  { value: 'Eq', label: 'is' },
  { value: 'NotEq', label: 'is not' },
  { value: 'Gt', label: 'greater than' },
  { value: 'Gte', label: 'at least' },
  { value: 'Lt', label: 'less than' },
  { value: 'Lte', label: 'at most' },
  { value: 'In', label: 'is one of' },
  { value: 'NotIn', label: 'is none of' },
  { value: 'Between', label: 'between' },
  { value: 'Contains', label: 'contains' },
  { value: 'StartsWith', label: 'starts with' },
  { value: 'IsNull', label: 'is empty' },
  { value: 'IsNotNull', label: 'is not empty' },
];

export const AGGREGATIONS: { value: Aggregation; label: string }[] = [
  { value: 'Sum', label: 'Sum' },
  { value: 'Avg', label: 'Average' },
  { value: 'Min', label: 'Min' },
  { value: 'Max', label: 'Max' },
  { value: 'Count', label: 'Count' },
  { value: 'CountDistinct', label: 'Count distinct' },
];

export const TIME_GRAINS: TimeGrain[] = ['Day', 'Week', 'Month', 'Quarter', 'Year'];

/** A field the chart is grouped by. */
export interface DimensionItem {
  column: string;
  dataType: string;
  grain: TimeGrain | '';
}

/** A number the chart shows. A null column with Count means "count rows". */
export interface MetricItem {
  column: string | null;
  dataType: string;
  aggregation: Aggregation;
}

export interface FilterItem {
  column: string;
  dataType: string;
  operator: FilterOperator;
  /** Typed as text. In, NotIn and Between take several values, separated by commas. */
  value: string;
}

export interface EditorState {
  name: string;
  description: string;
  datasetId: string;
  type: ChartType;
  dimensions: DimensionItem[];
  metrics: MetricItem[];
  filters: FilterItem[];
  style: ChartStyle;
  /** Null leaves the server's default. */
  limit: number | null;
  /** The name of a dimension or metric in the result, or empty for no sorting. */
  sortBy: string;
  sortDirection: 'Asc' | 'Desc';
}

export const emptyEditorState = (datasetId = ''): EditorState => ({
  name: '',
  description: '',
  datasetId,
  type: 'Bar',
  dimensions: [],
  metrics: [],
  filters: [],
  style: defaultChartStyle(),
  limit: null,
  sortBy: '',
  sortDirection: 'Desc',
});

// ---- looking at fields --------------------------------------------------------------------------------------

export const isDateType = (dataType: string): boolean => /^(date|timestamp)/i.test(dataType);
const isNumberType = (column: Pick<DatasetColumn, 'role'>): boolean => column.role === 'Measure';

/** The short badge shown beside a field: text, number or date. */
export function fieldBadge(column: DatasetColumn): 'Str' | '123' | 'Date' {
  if (isDateType(column.dataType)) return 'Date';
  return isNumberType(column) ? '123' : 'Str';
}

/** Measures start as a sum; anything else can only be counted. */
export function defaultAggregation(column: DatasetColumn): Aggregation {
  return column.role === 'Measure' ? 'Sum' : 'Count';
}

/** What an aggregation may be applied to: sums and averages need numbers; dates also have a first and last. */
export function aggregationsFor(dataType: string | null, role: DatasetColumn['role'] | null): Aggregation[] {
  if (dataType === null) return ['Count'];
  if (role === 'Measure') return ['Sum', 'Avg', 'Min', 'Max', 'Count', 'CountDistinct'];
  if (isDateType(dataType)) return ['Min', 'Max', 'Count', 'CountDistinct'];
  return ['Count', 'CountDistinct'];
}

// ---- adding things ------------------------------------------------------------------------------------------

export const dimensionFor = (column: DatasetColumn): DimensionItem => ({
  column: column.name,
  dataType: column.dataType,
  grain: isDateType(column.dataType) ? 'Month' : '',
});

export const metricFor = (column: DatasetColumn): MetricItem => ({
  column: column.name,
  dataType: column.dataType,
  aggregation: defaultAggregation(column),
});

export const rowCountMetric = (): MetricItem => ({ column: null, dataType: '', aggregation: 'Count' });

export const filterFor = (column: DatasetColumn): FilterItem => ({
  column: column.name,
  dataType: column.dataType,
  operator: 'Eq',
  value: '',
});

/** The zone a field goes to when it is clicked: numbers are shown, everything else groups. */
export const defaultZone = (column: DatasetColumn): 'dimensions' | 'metrics' => (column.role === 'Measure' ? 'metrics' : 'dimensions');

// ---- names in the result ------------------------------------------------------------------------------------

/** The names the result columns will have: dimensions keep their field name; metrics are "sum_price" or "count". */
export function resultNames(state: Pick<EditorState, 'dimensions' | 'metrics'>): { dimensions: string[]; metrics: string[] } {
  const used = new Set<string>();
  const unique = (base: string): string => {
    let name = base;
    for (let n = 2; used.has(name.toLowerCase()); n++) name = `${base}_${n}`;
    used.add(name.toLowerCase());
    return name;
  };
  const dimensions = state.dimensions.map((d) => unique(d.column));
  const metrics = state.metrics.map((m) => unique(m.column === null ? 'count' : `${m.aggregation.toLowerCase()}_${m.column}`));
  return { dimensions, metrics };
}

// ---- state <-> saved chart ----------------------------------------------------------------------------------

const splitValues = (text: string): string[] =>
  text.split(',').map((v) => v.trim()).filter((v) => v.length > 0);

const needsNoValue = (op: FilterOperator): boolean => op === 'IsNull' || op === 'IsNotNull';
const takesList = (op: FilterOperator): boolean => op === 'In' || op === 'NotIn' || op === 'Between';

/** The query the API understands. Sorting and the row limit are part of it. */
export function toQuery(state: EditorState): ChartQuery {
  const names = resultNames(state);
  const query: ChartQuery = {
    dimensions: state.dimensions.map((d, i) => ({
      column: d.column,
      alias: names.dimensions[i],
      ...(d.grain ? { grain: d.grain } : {}),
    })),
    measures: state.metrics.map((m, i) => ({
      aggregation: m.aggregation,
      ...(m.column === null ? {} : { column: m.column }),
      alias: names.metrics[i],
    })),
  };

  const filters = state.filters
    .filter((f) => needsNoValue(f.operator) || f.value.trim() !== '')
    .map((f) => ({
      column: f.column,
      operator: f.operator,
      ...(needsNoValue(f.operator) ? {} : takesList(f.operator) ? { values: splitValues(f.value) } : { value: f.value.trim() }),
    }));
  if (filters.length > 0) query.filters = filters;

  if (state.sortBy) query.orderBy = [{ field: state.sortBy, direction: state.sortDirection }];
  if (state.limit !== null) query.limit = state.limit;
  return query;
}

export function toSaveChart(state: EditorState): SaveChart {
  const definition: ChartDefinition = { query: toQuery(state), style: { ...state.style } };
  return {
    name: state.name.trim(),
    description: state.description.trim() || null,
    datasetId: state.datasetId,
    type: state.type,
    definition,
  };
}

/** Rebuilds the editor from a saved chart. Fields the dataset no longer has are left out. */
export function fromChart(chart: Chart, dataset: Dataset | undefined): EditorState {
  const columns = new Map((dataset?.columns ?? []).map((c) => [c.name, c]));
  const query = chart.definition.query;

  const dimensions: DimensionItem[] = (query.dimensions ?? []).flatMap((d) => {
    const column = columns.get(d.column);
    return column ? [{ column: d.column, dataType: column.dataType, grain: d.grain ?? '' }] : [];
  });
  const metrics: MetricItem[] = (query.measures ?? []).flatMap((m) => {
    if (m.column === undefined || m.column === null) return [rowCountMetric()];
    const column = columns.get(m.column);
    return column ? [{ column: m.column, dataType: column.dataType, aggregation: m.aggregation }] : [];
  });
  const filters: FilterItem[] = (query.filters ?? []).flatMap((f) => {
    const column = columns.get(f.column);
    if (!column) return [];
    const value = f.values ? f.values.map(String).join(', ') : f.value === undefined || f.value === null ? '' : String(f.value);
    return [{ column: f.column, dataType: column.dataType, operator: f.operator as FilterOperator, value }];
  });

  // The sort field is stored by its name in the result; keep it only if it still names something.
  const names = resultNames({ dimensions, metrics });
  const order = query.orderBy?.[0];
  const known = order && [...names.dimensions, ...names.metrics].some((n) => n === order.field);

  return {
    name: chart.name,
    description: chart.description ?? '',
    datasetId: chart.datasetId,
    type: chart.type,
    dimensions,
    metrics,
    filters,
    style: { ...defaultChartStyle(), ...chart.definition.style },
    limit: query.limit ?? null,
    sortBy: known ? order.field : '',
    sortDirection: known ? order.direction : 'Desc',
  };
}

// ---- checking -----------------------------------------------------------------------------------------------

/** What is still missing before the chart can be drawn or saved. Empty means it is ready. */
export function problemsOf(state: EditorState, options: { forSave: boolean } = { forSave: false }): string[] {
  const problems: string[] = [];
  if (options.forSave && !state.name.trim()) problems.push('Give the chart a name.');
  if (!state.datasetId) problems.push('Choose a dataset.');
  if (state.metrics.length === 0 && state.dimensions.length === 0) problems.push('Drag a field into Dimensions or Metrics.');

  switch (state.type) {
    case 'Number':
      if (state.metrics.length !== 1 || state.dimensions.length > 0) problems.push('A number shows exactly one metric and no dimensions.');
      break;
    case 'Table':
      break;
    default:
      if (state.metrics.length === 0) problems.push('This chart type needs at least one metric.');
  }

  if (state.limit !== null && (!Number.isInteger(state.limit) || state.limit < 1 || state.limit > 1000)) {
    problems.push('The row limit must be a whole number from 1 to 1000.');
  }
  return problems;
}
