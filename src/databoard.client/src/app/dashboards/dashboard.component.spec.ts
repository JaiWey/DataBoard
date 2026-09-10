import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardComponent, parseFilterInput } from './dashboard.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '44444444-4444-4444-4444-444444444444';
const DATASET = '33333333-3333-3333-3333-333333333333';
const C_BAR = 'c0000000-0000-0000-0000-000000000001';
const C_TABLE = 'c0000000-0000-0000-0000-000000000002';
const C_SPARE = 'c0000000-0000-0000-0000-000000000003';
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
    widgets: [{ id: W_BAR, chartId: C_BAR }, { id: W_TABLE, chartId: C_TABLE }],
    filters: withFilter ? [{ id: F_REGION, label: 'Region', column: 'region', operator: 'In' }] : [],
  },
});

const chart = (id: string, name: string, type: string) => ({
  id, name, description: null, datasetId: DATASET, type, createdUtc: '', updatedUtc: '',
  definition: { query: { measures: [{ aggregation: 'Count' }] }, style: { legend: true, labels: false, palette: 'default' } },
});
const charts = [chart(C_BAR, 'Revenue by region', 'Bar'), chart(C_TABLE, 'Orders', 'Table'), chart(C_SPARE, 'Spare', 'Pie')];

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
  const dashboardUrl = `/api/organizations/${ORG}/dashboards/${DASH}`;

  /** A dashboard on the right asks for the charts, the datasets and the dashboard itself. */
  const open = (role: 'Viewer' | 'Editor' | 'Owner') => {
    fixture.componentRef.setInput('orgId', ORG);
    fixture.componentRef.setInput('dashboardId', DASH);
    fixture.componentRef.setInput('canEdit', role !== 'Viewer');
    fixture.detectChanges();
  };

  const load = (role: 'Viewer' | 'Editor' | 'Owner', def = dashboard()) => {
    open(role);
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(dashboardUrl).flush(def);
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
        provideRouter([]),
        // ECharts needs a real canvas; the drawing itself is covered by the chart component's own tests.
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    fixture = TestBed.createComponent(DashboardComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('runs the dashboard on open and draws each chart the way it was saved', () => {
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

    expect(Array.from(el().querySelectorAll('.widget mat-card-title')).map((t) => t.textContent?.trim())).toEqual(['Revenue by region', 'Orders']);
    expect(el().querySelectorAll('app-chart').length).toBe(1); // the bar chart
    const cells = Array.from(el().querySelectorAll('tbody td')).map((c) => c.textContent);
    expect(cells).toEqual(['Auckland', '3', 'Dunedin', '1']); // the table
  });

  it('shows the dataset each chart reads from', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    expect(el().querySelector('.widget mat-card-subtitle')?.textContent).toContain('Orders');
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

  it('shows a chart error in place without hiding the other charts', () => {
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

  it('says so when a chart on the dashboard no longer exists', () => {
    open('Editor');
    http.expectOne(`/api/organizations/${ORG}/charts`).flush([charts[1]]); // the bar chart is gone
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(dashboardUrl).flush(dashboard());
    http.expectOne(runUrl).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().textContent).toContain('Chart not found');
    expect(el().textContent).toContain('This chart was deleted.');
  });

  it('hides editing controls from viewers', () => {
    load('Viewer');
    http.expectOne(runUrl).flush({ widgets: [] });

    const buttons = Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['Apply filters', 'Clear']);
    expect(el().textContent).not.toContain('Add a shared filter');
    expect(el().querySelector('.widget a')).toBeNull(); // no "Edit chart"
  });

  it('lets editors remove a chart from the dashboard by saving it without that widget', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });

    (el().querySelector('button.remove-widget') as HTMLButtonElement).click();
    const put = http.expectOne({ method: 'PUT', url: dashboardUrl });
    expect(put.request.body.name).toBe('Sales overview');
    // The widget left behind is written down with its place (it had none saved, so it takes the first free spot).
    expect(put.request.body.definition.widgets).toEqual([{ id: W_TABLE, chartId: C_TABLE, layout: { x: 0, y: 0, w: 6, h: 5 } }]);
    expect(put.request.body.definition.filters.length).toBe(1);

    const saved = dashboard();
    saved.definition.widgets = saved.definition.widgets.slice(1);
    put.flush(saved);
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  it('links each chart to the chart editor for editors', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });

    const links = Array.from(el().querySelectorAll('.widget a')).map((a) => a.getAttribute('href'));
    expect(links).toEqual([`/organizations/${ORG}/charts/${C_BAR}`, `/organizations/${ORG}/charts/${C_TABLE}`]);
  });

  it('offers only the charts that are not on the dashboard yet, and adds the chosen ones', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of([C_SPARE]) } as never);

    buttonByText('Add chart').click();
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts); // the list is refreshed when the dialog opens

    const data = (open.mock.calls[0][1] as { data: { charts: { id: string }[]; orgId: string } }).data;
    expect(data.orgId).toBe(ORG);
    expect(data.charts.map((c) => c.id)).toEqual([C_SPARE]);

    const put = http.expectOne({ method: 'PUT', url: dashboardUrl });
    // Widgets that had no place get one: side by side first, then on the next row. The new one has no id yet.
    expect(put.request.body.definition.widgets).toEqual([
      { id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 6, h: 5 } },
      { id: W_TABLE, chartId: C_TABLE, layout: { x: 6, y: 0, w: 6, h: 5 } },
      { id: '00000000-0000-0000-0000-000000000000', chartId: C_SPARE, layout: { x: 0, y: 5, w: 6, h: 5 } },
    ]);
    put.flush(dashboard());
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  it('does nothing when the add chart dialog is cancelled', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    buttonByText('Add chart').click();
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);

    http.expectNone({ method: 'PUT', url: dashboardUrl });
  });

  describe('layout', () => {
    const cell = (index: number) => el().querySelectorAll('app-dashboard-grid .cell')[index] as HTMLElement;
    const handle = (index: number) => cell(index).querySelector('.handle') as HTMLElement;
    const press = (target: Element, key: string, shiftKey = false) => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true }));
      fixture.detectChanges();
    };

    it('places widgets that were saved without a place, side by side', () => {
      load('Viewer');
      http.expectOne(runUrl).flush({ widgets: [] });

      expect([0, 1].map((i) => [cell(i).style.getPropertyValue('--x'), cell(i).style.getPropertyValue('--y'), cell(i).style.getPropertyValue('--w')]))
        .toEqual([['0', '0', '6'], ['6', '0', '6']]);
    });

    it('uses the place a widget was saved with', () => {
      const saved = dashboard();
      saved.definition.widgets = [
        { id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 12, h: 4 } },
        { id: W_TABLE, chartId: C_TABLE, layout: { x: 3, y: 4, w: 6, h: 3 } },
      ] as never;
      load('Viewer', saved);
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(cell(0).style.getPropertyValue('--w')).toBe('12');
      expect([cell(1).style.getPropertyValue('--x'), cell(1).style.getPropertyValue('--y'), cell(1).style.getPropertyValue('--h')]).toEqual(['3', '4', '3']);
    });

    it('is arranged only by editors', () => {
      load('Viewer');
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(buttonByText('Edit layout')).toBeUndefined();
      expect(el().querySelector('.handle')).toBeNull();
    });

    it('can be changed with the keyboard and is saved without running the charts again', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });

      buttonByText('Edit layout').click();
      fixture.detectChanges();
      expect(buttonByText('Save layout').disabled).toBe(true); // nothing has changed yet

      press(handle(0), 'ArrowDown', true); // Shift resizes: one row taller
      expect(buttonByText('Save layout').disabled).toBe(false);
      expect(cell(0).style.getPropertyValue('--h')).toBe('6');

      buttonByText('Save layout').click();
      const put = http.expectOne({ method: 'PUT', url: dashboardUrl });
      expect(put.request.body.definition.widgets).toEqual([
        { id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 6, h: 6 } },
        { id: W_TABLE, chartId: C_TABLE, layout: { x: 6, y: 0, w: 6, h: 5 } },
      ]);
      put.flush({ ...dashboard(), definition: { ...dashboard().definition, widgets: put.request.body.definition.widgets } });
      fixture.detectChanges();

      http.expectNone(runUrl); // moving a widget does not change what it shows
      expect(el().querySelector('.handle')).toBeNull(); // back to viewing
      expect(buttonByText('Edit layout')).toBeDefined();
    });

    it('pushes a widget down when another one is moved or grown over it', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      buttonByText('Edit layout').click();
      fixture.detectChanges();

      press(handle(0), 'ArrowRight', true); // the bar chart gets one column wider and reaches the table

      expect(cell(0).style.getPropertyValue('--w')).toBe('7');
      expect(cell(1).style.getPropertyValue('--y')).toBe('5'); // the table moved below it
    });

    it('can be changed by dragging the corner of a widget', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      buttonByText('Edit layout').click();
      fixture.detectChanges();
      // jsdom has no layout: pretend the grid is 1200px wide, so one column is about 101px including its gap.
      const grid = el().querySelector('app-dashboard-grid .grid') as HTMLElement;
      vi.spyOn(grid, 'getBoundingClientRect').mockReturnValue({ width: 1200 } as DOMRect);

      const corner = cell(0).querySelector('.resize') as HTMLElement;
      corner.dispatchEvent(new MouseEvent('pointerdown', { clientX: 500, clientY: 300, button: 0, bubbles: true, cancelable: true }));
      document.dispatchEvent(new MouseEvent('pointermove', { clientX: 500 - 101, clientY: 300 + 96 * 2 }));
      document.dispatchEvent(new MouseEvent('pointerup'));
      fixture.detectChanges();

      expect(cell(0).style.getPropertyValue('--w')).toBe('5'); // one column narrower
      expect(cell(0).style.getPropertyValue('--h')).toBe('7'); // two rows taller

      document.dispatchEvent(new MouseEvent('pointermove', { clientX: 0, clientY: 0 })); // the drag is over: no effect
      fixture.detectChanges();
      expect(cell(0).style.getPropertyValue('--w')).toBe('5');
    });

    it('drops the changes when cancelled', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      buttonByText('Edit layout').click();
      fixture.detectChanges();
      press(handle(0), 'ArrowDown', true);

      buttonByText('Cancel').click();
      fixture.detectChanges();

      expect(cell(0).style.getPropertyValue('--h')).toBe('5');
      http.expectNone({ method: 'PUT', url: dashboardUrl });
    });

    it('does not offer layout editing for an empty dashboard', () => {
      load('Editor', { ...dashboard(), definition: { widgets: [], filters: [] } });
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(buttonByText('Edit layout')).toBeUndefined();
    });
  });

  it('tells editors of an empty dashboard how to put charts on it', () => {
    load('Editor', { ...dashboard(), definition: { widgets: [], filters: [] } });
    http.expectOne(runUrl).flush({ widgets: [] });

    expect(el().textContent).toContain('This dashboard has no charts yet.');
    const link = Array.from(el().querySelectorAll('a')).find((a) => a.textContent?.includes('create a new chart'));
    expect(link?.getAttribute('href')).toBe(`/organizations/${ORG}/charts/new`);
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

    const put = http.expectOne({ method: 'PUT', url: dashboardUrl });
    expect(put.request.body.definition.filters).toEqual([
      { id: '00000000-0000-0000-0000-000000000000', label: 'Region', column: 'region', operator: 'In' },
    ]);
    put.flush(dashboard());
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  it('shows the API message when the dashboard cannot be loaded', () => {
    open('Viewer');
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(dashboardUrl).flush({ title: 'Not found', detail: 'Dashboard not found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')).not.toBeNull();
  });

  it('tells the page when a change was saved, so its list can show the new chart count', () => {
    load('Editor');
    http.expectOne(runUrl).flush({ widgets: [] });
    const changed: { definition: { widgets: unknown[] } }[] = [];
    fixture.componentInstance.changed.subscribe((d) => changed.push(d));

    (el().querySelector('button.remove-widget') as HTMLButtonElement).click();
    const saved = dashboard();
    saved.definition.widgets = saved.definition.widgets.slice(1);
    http.expectOne({ method: 'PUT', url: dashboardUrl }).flush(saved);
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
    http.expectNone({ method: 'DELETE', url: dashboardUrl });

    confirmSpy.mockReturnValue(true);
    buttonByText('Delete dashboard').click();
    http.expectOne({ method: 'DELETE', url: dashboardUrl }).flush(null, { status: 204, statusText: 'No Content' });

    expect(deleted).toEqual([DASH]);
  });
});
