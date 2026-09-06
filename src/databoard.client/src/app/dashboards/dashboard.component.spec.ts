import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardComponent, parseFilterInput } from './dashboard.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '44444444-4444-4444-4444-444444444444';
const DATASET = '33333333-3333-3333-3333-333333333333';
const W_BAR = 'a0000000-0000-0000-0000-000000000001';
const W_TABLE = 'a0000000-0000-0000-0000-000000000002';
const F_REGION = 'b0000000-0000-0000-0000-000000000001';

const dashboard = (withFilter = true) => ({
  id: DASH,
  name: 'Sales overview',
  description: null,
  createdUtc: '2026-10-07T00:00:00Z',
  updatedUtc: '2026-10-07T00:00:00Z',
  definition: {
    widgets: [
      { id: W_BAR, title: 'Revenue by region', datasetId: DATASET, chartType: 'Bar', query: { measures: [{ aggregation: 'Count' }] } },
      { id: W_TABLE, title: 'Orders', datasetId: DATASET, chartType: 'Table', query: { measures: [{ aggregation: 'Count' }] } },
    ],
    filters: withFilter ? [{ id: F_REGION, label: 'Region', column: 'region', operator: 'In' }] : [],
  },
});

const datasets = [
  {
    id: DATASET, dataSourceId: 'x', name: 'Orders', description: null, sql: null,
    columns: [{ name: 'region', dataType: 'text', role: 'Dimension' }, { name: 'price', dataType: 'numeric', role: 'Measure' }],
    createdUtc: '', updatedUtc: '',
  },
];

const ok = (rows: unknown[][]) => ({
  columns: [{ name: 'region', dataType: 'text' }, { name: 'n', dataType: 'bigint' }],
  rows,
  truncated: false,
  sql: null,
});

describe('parseFilterInput', () => {
  it('splits lists on commas and trims, and keeps single values whole', () => {
    expect(parseFilterInput('In', ' Auckland , Dunedin,, ')).toEqual(['Auckland', 'Dunedin']);
    expect(parseFilterInput('Between', '1, 10')).toEqual(['1', '10']);
    expect(parseFilterInput('Eq', ' Auckland, North ')).toEqual(['Auckland, North']);
  });

  it('treats blank input as no value', () => {
    expect(parseFilterInput('In', '   ')).toEqual([]);
    expect(parseFilterInput('Eq', '')).toEqual([]);
  });
});

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const runUrl = `/api/organizations/${ORG}/dashboards/${DASH}/run`;

  /** The page decides who may edit and passes it down, so the tests do the same. */
  const open = (role: 'Viewer' | 'Editor' | 'Owner') => {
    fixture.componentRef.setInput('orgId', ORG);
    fixture.componentRef.setInput('dashboardId', DASH);
    fixture.componentRef.setInput('canEdit', role !== 'Viewer');
    fixture.detectChanges();
  };

  const load = (role: 'Viewer' | 'Editor' | 'Owner', def = dashboard()) => {
    open(role);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(`/api/organizations/${ORG}/dashboards/${DASH}`).flush(def);
    fixture.detectChanges();
  };

  const type = (selector: string, value: string) => {
    const input = el().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  const buttonByText = (text: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // ECharts needs a real canvas; the drawing itself is covered by the chart component's own tests.
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    fixture = TestBed.createComponent(DashboardComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('runs the dashboard on open and draws a chart or a table per widget', async () => {
    load('Viewer');
    const run = http.expectOne(runUrl);
    expect(run.request.body).toEqual({ filters: {} });
    run.flush({
      widgets: [
        { widgetId: W_BAR, result: ok([['Auckland', 3]]), error: null, appliedFilters: [] },
        { widgetId: W_TABLE, result: ok([['Auckland', 3], ['Dunedin', 1]]), error: null, appliedFilters: [] },
      ],
    });
    fixture.detectChanges();

    expect(el().querySelectorAll('app-chart').length).toBe(1);
    const cells = Array.from(el().querySelectorAll('tbody td')).map((c) => c.textContent);
    expect(cells).toEqual(['Auckland', '3', 'Dunedin', '1']);
  });

  it('sends the filter values typed by the viewer when applying', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    type('input[aria-label="Region"]', 'Auckland, Dunedin');
    buttonByText('Apply filters').click();

    expect(http.expectOne(runUrl).request.body).toEqual({ filters: { [F_REGION]: ['Auckland', 'Dunedin'] } });
  });

  it('leaves blank filters out and clears them again', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    type('input[aria-label="Region"]', 'Auckland');
    buttonByText('Clear').click();

    expect(http.expectOne(runUrl).request.body).toEqual({ filters: {} });
    expect((el().querySelector('input[aria-label="Region"]') as HTMLInputElement).value).toBe('');
  });

  it('shows a widget error in place without hiding the other widgets', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({
      widgets: [
        { widgetId: W_BAR, result: null, error: "'price' is not a column of this dataset.", appliedFilters: [] },
        { widgetId: W_TABLE, result: ok([['Auckland', 3]]), error: null, appliedFilters: [] },
      ],
    });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain("'price' is not a column");
    expect(el().querySelectorAll('tbody td').length).toBe(2);
  });

  it('hides editing controls from viewers', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    const buttons = Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['Apply filters', 'Clear']);
    expect(el().textContent).not.toContain('Add a shared filter');
  });

  it('lets editors remove a widget by saving the dashboard without it', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
    const put = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${DASH}` });
    expect(put.request.body.name).toBe('Sales overview');
    expect(put.request.body.definition.widgets.map((w: { id: string }) => w.id)).toEqual([W_TABLE]);
    expect(put.request.body.definition.filters.length).toBe(1);

    const saved = dashboard();
    saved.definition.widgets = saved.definition.widgets.slice(1);
    put.flush(saved);
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  it('lets editors add a shared filter on a column of the organization datasets', () => {
    load('Editor', dashboard(false));
    http.expectOne(runUrl).flush({ widgets: [] });

    const columns = Array.from(el().querySelectorAll('select[aria-label="Filter column"] option')).map((o) => o.textContent?.trim());
    expect(columns).toEqual(['price', 'region']);

    type('input[aria-label="Filter label"]', 'Region');
    const select = el().querySelector('select[aria-label="Filter column"]') as HTMLSelectElement;
    select.value = 'region';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();

    const put = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${DASH}` });
    expect(put.request.body.definition.filters).toEqual([
      { id: '00000000-0000-0000-0000-000000000000', label: 'Region', column: 'region', operator: 'In' },
    ]);
    put.flush(dashboard());
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  it('shows the API message when the dashboard cannot be loaded', () => {
    open('Viewer');
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(`/api/organizations/${ORG}/dashboards/${DASH}`).flush(
      { title: 'Not found', detail: 'Dashboard not found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')).not.toBeNull();
  });

  it('tells the page when a change was saved, so its list can show the new chart count', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });
    const changed: { definition: { widgets: unknown[] } }[] = [];
    fixture.componentInstance.changed.subscribe((d) => changed.push(d));

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
    const saved = dashboard();
    saved.definition.widgets = saved.definition.widgets.slice(1);
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${DASH}` }).flush(saved);
    http.expectOne(runUrl).flush({ widgets: [] });

    expect(changed.length).toBe(1);
    expect(changed[0].definition.widgets.length).toBe(1);
  });

  it('asks before deleting and tells the page it is gone', () => {
    load('Owner');
    http.expectOne(runUrl).flush({ widgets: [] });
    const deleted: string[] = [];
    fixture.componentInstance.deleted.subscribe((id) => deleted.push(id));
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);

    buttonByText('Delete dashboard').click();
    http.expectNone({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/${DASH}` });

    confirmSpy.mockReturnValue(true);
    buttonByText('Delete dashboard').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/${DASH}` }).flush(null, { status: 204, statusText: 'No Content' });

    expect(deleted).toEqual([DASH]);
  });

  it('is hidden while another tab is showing, and keeps what it has', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    fixture.componentRef.setInput('active', false);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).classList.contains('inactive')).toBe(true);
    expect(el().querySelector('mat-card-title')?.textContent).toContain('Sales overview');
  });
});
