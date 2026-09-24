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

  const load = (role: 'Viewer' | 'Editor' | 'Owner', def: object = dashboard(), datasetList: object[] = datasets) => {
    open(role);
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasetList);
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
    expect(buttons).toEqual(['Print / PDF', 'Apply filters', 'Clear']); // printing is not editing
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

  describe('refresh schedule', () => {
    const refresh = (over: Record<string, unknown> = {}) => ({
      intervalMinutes: 15, nextUtc: null, lastUtc: new Date(Date.now() - 4 * 60_000).toISOString(), lastFailures: 0, lastMessage: null, ...over,
    });
    const scheduled = (over: Record<string, unknown> = {}) => ({ ...dashboard(false), refresh: refresh(over) });
    const select = () => el().querySelector('select[aria-label="Refresh schedule"]') as HTMLSelectElement;
    const status = () => el().querySelector('.schedule')?.textContent?.replace(/\s+/g, ' ').trim();
    const choose = (value: string) => {
      select().value = value;
      select().dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('shows editors the schedule with its current choice', () => {
      load('Editor', scheduled());
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(select().selectedOptions[0].textContent?.trim()).toBe('15 minutes');
      expect(Array.from(select().options).map((o) => o.textContent?.trim())).toEqual(
        ['Off', '5 minutes', '15 minutes', '30 minutes', '1 hour', '6 hours', '24 hours']);
      expect(status()).toContain('Last refresh 4 min ago.');
    });

    it('tells a viewer how often the data is refreshed and when, without a way to change it', () => {
      load('Viewer', scheduled());
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(select()).toBeNull();
      expect(status()).toBe('Refreshed every 15 min. Last refresh 4 min ago.');
    });

    it('says when a schedule has not run yet', () => {
      load('Viewer', scheduled({ lastUtc: null }));
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(status()).toContain('Not refreshed yet.');
    });

    it('shows nothing about a schedule to a viewer when there is none, and only the choice to an editor', () => {
      load('Viewer', { ...dashboard(false), refresh: refresh({ intervalMinutes: null, lastUtc: null }) });
      http.expectOne(runUrl).flush({ widgets: [] });
      expect(el().querySelector('.schedule')).toBeNull();
    });

    it('offers an editor the choice even when there is no schedule, with nothing else to say', () => {
      load('Editor', { ...dashboard(false), refresh: refresh({ intervalMinutes: null, lastUtc: null }) });
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(select().selectedOptions[0].textContent?.trim()).toBe('Off');
      expect(el().querySelector('.schedule .status')).toBeNull();
    });

    it('says how many charts could not be refreshed, and why, to everyone', () => {
      load('Viewer', scheduled({ lastFailures: 2, lastMessage: "'price' is not a column of this dataset." }));
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(el().querySelector('.schedule [role=alert]')?.textContent?.replace(/\s+/g, ' ').trim())
        .toBe("2 charts could not be refreshed: 'price' is not a column of this dataset.");
    });

    it('says "chart" for one', () => {
      load('Viewer', scheduled({ lastFailures: 1, lastMessage: null }));
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(el().querySelector('.schedule [role=alert]')?.textContent?.trim()).toBe('1 chart could not be refreshed');
    });

    it('puts the dashboard on the schedule that was chosen, at once and without running it again', () => {
      load('Editor', { ...dashboard(false), refresh: refresh({ intervalMinutes: null, lastUtc: null }) });
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('15');

      const put = http.expectOne({ method: 'PUT', url: `${dashboardUrl}/refresh` });
      expect(put.request.body).toEqual({ intervalMinutes: 15 });
      put.flush(scheduled({ lastUtc: null }));
      fixture.detectChanges();
      expect(select().selectedOptions[0].textContent?.trim()).toBe('15 minutes');
      expect(status()).toContain('Not refreshed yet.');
      http.expectNone(runUrl); // the data on the page is as fresh as it was
    });

    it('takes the dashboard off the schedule with Off', () => {
      load('Editor', scheduled());
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('');

      const put = http.expectOne({ method: 'PUT', url: `${dashboardUrl}/refresh` });
      expect(put.request.body).toEqual({ intervalMinutes: null });
      put.flush({ ...dashboard(false), refresh: refresh({ intervalMinutes: null }) });
      fixture.detectChanges();
      expect(el().querySelector('.schedule .status')).toBeNull();
    });

    it('shows why a schedule could not be set and puts the choice back', () => {
      load('Editor', { ...dashboard(false), refresh: refresh({ intervalMinutes: null, lastUtc: null }) });
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('5');
      http.expectOne({ method: 'PUT', url: `${dashboardUrl}/refresh` }).flush(
        { errors: { IntervalMinutes: ['At most 20 dashboards of an organization can be refreshed on a schedule. Turn one off first.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('At most 20 dashboards');
      expect(select().selectedOptions[0].textContent?.trim()).toBe('Off');
    });

    it('does not ask again for the choice that is already made', () => {
      load('Editor', scheduled());
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('15');

      http.expectNone({ method: 'PUT', url: `${dashboardUrl}/refresh` });
    });

    it('keeps the page quiet for older answers that say nothing about a schedule', () => {
      load('Editor', dashboard(false));
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(el().querySelector('.schedule')).toBeNull();
    });
  });

  describe('stored results', () => {
    const cachedWidget = (cachedUtc: string | null) => ({
      widgets: [{ widgetId: W_BAR, result: { ...ok([['Auckland', 3]]), cachedUtc }, error: null, appliedFilters: [] }],
    });

    it('says how old reused data is, so nobody takes it for live', () => {
      load('Viewer');
      http.expectOne(runUrl).flush(cachedWidget(new Date(Date.now() - 7 * 60_000).toISOString()));
      fixture.detectChanges();

      expect(el().querySelector('.cached')?.textContent).toBe('Data from 7 min ago.');
    });

    it('says nothing about age for data that was just fetched', () => {
      load('Viewer');
      http.expectOne(runUrl).flush(cachedWidget(null));
      fixture.detectChanges();

      expect(el().querySelector('.cached')).toBeNull();
    });

    it('lets editors fetch fresh data, and asks for exactly that', () => {
      load('Editor');
      http.expectOne(runUrl).flush(cachedWidget(new Date().toISOString()));
      fixture.detectChanges();

      buttonByText('Refresh data').click();

      const req = http.expectOne(runUrl);
      expect(req.request.body).toEqual({ filters: {}, refresh: true });
      req.flush(cachedWidget(null));
      fixture.detectChanges();
      expect(el().querySelector('.cached')).toBeNull(); // fresh now
    });

    it('keeps the filters that were typed when refreshing', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      fixture.detectChanges();
      type('input[aria-label="Region"]', 'Auckland');

      buttonByText('Refresh data').click();

      expect(http.expectOne(runUrl).request.body).toEqual({ filters: { [F_REGION]: ['Auckland'] }, refresh: true });
    });

    it('does not offer refreshing to viewers, and an ordinary run does not ask for it', () => {
      load('Viewer');
      const run = http.expectOne(runUrl);

      expect(run.request.body).toEqual({ filters: {} });
      run.flush({ widgets: [] });
      fixture.detectChanges();
      expect(buttonByText('Refresh data')).toBeUndefined();
    });
  });

  describe('exporting', () => {
    it('lets anyone print the dashboard to PDF through the browser', () => {
      const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
      load('Viewer');
      http.expectOne(runUrl).flush({ widgets: [] });

      buttonByText('Print / PDF').click();

      expect(print).toHaveBeenCalledTimes(1);
      print.mockRestore();
    });

    it('lets editors, and only editors, share the dashboard through a public link', () => {
      load('Viewer');
      http.expectOne(runUrl).flush({ widgets: [] });
      expect(buttonByText('Share')).toBeUndefined();
    });

    it('opens the sharing dialog for the dashboard that is shown', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({} as never);

      buttonByText('Share').click();

      const [, config] = open.mock.calls[0] as unknown as [unknown, { data: unknown }];
      expect(config.data).toEqual({ orgId: ORG, dashboardId: DASH, dashboardName: 'Sales overview' });
    });

    it('marks everything that should stay off the printed page', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });

      expect(el().querySelector('mat-card-header .actions')?.classList).toContain('no-print'); // the buttons
      expect(el().querySelector('form.filters')?.classList).toContain('no-print'); // the filter inputs
      expect(el().querySelector('h3')?.classList).toContain('no-print'); // "Add a shared filter" and its form
      expect(el().querySelector('h3 + form')?.classList).toContain('no-print');
    });

    it('gives each chart with data an export menu, for viewers too', () => {
      load('Viewer');
      http.expectOne(runUrl).flush({
        widgets: [
          { widgetId: W_BAR, result: ok([['Auckland', 3]]), error: null, appliedFilters: [] },
          { widgetId: W_TABLE, result: null, error: 'boom', appliedFilters: [] },
        ],
      });
      fixture.detectChanges();

      expect(Array.from(el().querySelectorAll('app-export-menu')).length).toBe(1); // none for the one that failed
    });
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
      { id: '00000000-0000-0000-0000-000000000000', label: 'Region', column: 'region', operator: 'In', target: 'Column' },
    ]);
    put.flush(dashboard());
    http.expectOne(runUrl).flush({ widgets: [] });
  });

  describe('filters on a dataset parameter', () => {
    const withParameters = [{ ...datasets[0], parameters: [{ name: 'region', type: 'Text', default: 'Auckland' }, { name: 'min_price', type: 'Number', default: 0 }] }];
    const filterSelect = (label: string) => el().querySelector(`select[aria-label="${label}"]`) as HTMLSelectElement;
    const choose = (label: string, value: string) => {
      const select = filterSelect(label);
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('offers parameters only when a dataset has them', () => {
      load('Editor', dashboard(false));
      http.expectOne(runUrl).flush({ widgets: [] });

      const option = Array.from(filterSelect('Filter applies to').options).find((o) => o.value === 'Parameter')!;
      expect(option.disabled).toBe(true);
    });

    it('lists the parameters of the datasets, fixes the condition at "is", and saves the target', () => {
      load('Editor', dashboard(false), withParameters);
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('Filter applies to', 'Parameter');
      expect(Array.from(filterSelect('Filter column').options).map((o) => o.textContent?.trim())).toEqual(['min_price', 'region']);
      expect(el().querySelector('select[aria-label="Filter condition"]')).toBeNull(); // a parameter takes one value

      type('input[aria-label="Filter label"]', 'Region');
      choose('Filter column', 'region');
      (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();

      const put = http.expectOne({ method: 'PUT', url: dashboardUrl });
      expect(put.request.body.definition.filters).toEqual([
        { id: '00000000-0000-0000-0000-000000000000', label: 'Region', column: 'region', operator: 'Eq', target: 'Parameter' },
      ]);
      put.flush(dashboard());
      http.expectOne(runUrl).flush({ widgets: [] });
    });

    it('goes back to columns, and their conditions, when the target is changed back', () => {
      load('Editor', dashboard(false), withParameters);
      http.expectOne(runUrl).flush({ widgets: [] });

      choose('Filter applies to', 'Parameter');
      choose('Filter applies to', 'Column');

      expect(Array.from(filterSelect('Filter column').options).map((o) => o.textContent?.trim())).toEqual(['price', 'region']);
      expect(filterSelect('Filter condition')).not.toBeNull();
    });
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
  // ---- background picture ----

  describe('background picture', () => {
    const withBackground = () => ({ ...dashboard(), background: { version: 'v1', fit: 'tile', opacity: 40 } });
    const layer = () => el().querySelector('app-background-layer');

    beforeEach(() => {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:picture');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    });

    it('has no layer when there is no picture', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      fixture.detectChanges();

      expect(layer()).toBeNull();
    });

    it('fetches the picture with its version and puts it behind the content, shown the way it was saved', () => {
      load('Viewer', withBackground());
      http.expectOne(runUrl).flush({ widgets: [] });
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/background?v=v1` }).flush(new Blob(['x']));
      fixture.detectChanges();

      const picture = el().querySelector('app-background-layer .layer') as HTMLElement;
      expect(picture.style.backgroundImage).toContain('blob:picture');
      expect(picture.style.backgroundRepeat).toBe('repeat');
      expect(picture.style.opacity).toBe('0.4');
      expect(el().querySelector('mat-card-content app-background-layer')).not.toBeNull();
    });

    it('offers the Background button to editors only', () => {
      load('Editor');
      http.expectOne(runUrl).flush({ widgets: [] });
      expect(buttonByText('Background')).toBeDefined();
    });

    it('does not offer the Background button to viewers', () => {
      load('Viewer');
      http.expectOne(runUrl).flush({ widgets: [] });
      fixture.detectChanges();

      expect(buttonByText('Background')).toBeUndefined();
    });

    it('opens the background dialog with what the dashboard has, and shows what it returns', () => {
      load('Owner', withBackground());
      http.expectOne(runUrl).flush({ widgets: [] });
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/background?v=v1` }).flush(new Blob(['x']));
      const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
        afterClosed: () => of({ ...dashboard(), background: { version: 'v2', fit: 'cover', opacity: 100 } }),
      } as never);

      buttonByText('Background').click();
      fixture.detectChanges();

      expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({
        orgId: ORG, dashboardId: DASH, dashboardName: 'Sales overview', background: { version: 'v1', fit: 'tile', opacity: 40 },
      });
      // A new version is a new picture: it is fetched again.
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/background?v=v2` }).flush(new Blob(['y']));
    });

    it('takes the layer away when the dialog removed the picture', () => {
      load('Owner', withBackground());
      http.expectOne(runUrl).flush({ widgets: [] });
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/background?v=v1` }).flush(new Blob(['x']));
      vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of({ ...dashboard(), background: null }) } as never);

      buttonByText('Background').click();
      fixture.detectChanges();

      expect(layer()).toBeNull();
    });

    it('keeps everything as it is when the dialog is cancelled', () => {
      load('Owner', withBackground());
      http.expectOne(runUrl).flush({ widgets: [] });
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/background?v=v1` }).flush(new Blob(['x']));
      vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

      buttonByText('Background').click();
      fixture.detectChanges();

      expect(layer()).not.toBeNull();
    });
  });

  // ---- picture widgets ----

  describe('picture widgets', () => {
    const W_PIC = 'd0000000-0000-0000-0000-000000000001';
    const withPicture = (over: object = {}) => ({
      ...dashboard(),
      definition: {
        widgets: [
          { id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 6, h: 5 } },
          { id: W_PIC, chartId: '00000000-0000-0000-0000-000000000000', kind: 'Image', imageKey: 'k1', imageFit: 'cover', alt: 'Logo', linkUrl: 'https://example.com', layout: { x: 6, y: 0, w: 4, h: 4 }, ...over },
        ],
        filters: [],
      },
    });
    const imageUrl = `${dashboardUrl}/image-widgets/${W_PIC}/image?v=k1`;

    beforeEach(() => {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:picture');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    });

    const open = (role: 'Viewer' | 'Editor' | 'Owner' = 'Editor') => {
      load(role, withPicture());
      http.expectOne(runUrl).flush({ widgets: [{ widgetId: W_BAR, result: ok([['Auckland', 3]]), error: null, appliedFilters: [] }] });
      http.expectOne({ method: 'GET', url: imageUrl }).flush(new Blob(['x']));
      fixture.detectChanges();
    };

    it('shows the picture where the widget is, as a link, and does not treat it as a missing chart', () => {
      open('Viewer');

      const picture = el().querySelector('app-image-widget img') as HTMLImageElement;
      expect(picture.getAttribute('src')).toBe('blob:picture');
      expect(picture.getAttribute('alt')).toBe('Logo');
      expect(picture.style.objectFit).toBe('cover');
      expect((el().querySelector('app-image-widget a') as HTMLAnchorElement).getAttribute('href')).toBe('https://example.com/');
      expect(el().textContent).not.toContain('Chart not found');
      expect(el().querySelectorAll('app-dashboard-grid mat-card').length).toBe(2); // the chart and the picture
    });

    it('offers editors Add picture, Edit picture and Remove on the picture, and viewers none of them', () => {
      open('Editor');
      expect(buttonByText('Add picture')).toBeDefined();
      expect(el().querySelector('.picture-actions .edit-picture')).not.toBeNull();
      expect(el().querySelector('.picture-actions .remove-widget')).not.toBeNull();
    });

    it('offers viewers nothing to change', () => {
      open('Viewer');

      expect(buttonByText('Add picture')).toBeUndefined();
      expect(el().querySelector('.picture-actions')).toBeNull();
    });

    it('hides the picture buttons while the layout is being arranged', () => {
      open('Owner');

      buttonByText('Edit layout').click();
      fixture.detectChanges();

      expect(el().querySelector('.picture-actions')).toBeNull();
    });

    it('opens the dialog for a new picture and shows the dashboard it returns, without running the charts again', () => {
      open('Owner');
      const added = withPicture();
      const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of({ ...added, name: 'Sales overview' }) } as never);
      const changed = vi.fn();
      fixture.componentInstance.changed.subscribe(changed);

      buttonByText('Add picture').click();
      fixture.detectChanges();

      expect((dialog.mock.calls[0][1] as { data: { dashboard: { id: string }; widget?: unknown } }).data.widget).toBeUndefined();
      expect(changed).toHaveBeenCalled();
      http.expectNone(runUrl);
    });

    it('opens the dialog for an existing picture with that widget', () => {
      open('Owner');
      const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

      (el().querySelector('.picture-actions .edit-picture') as HTMLButtonElement).click();

      expect((dialog.mock.calls[0][1] as { data: { widget: { id: string } } }).data.widget.id).toBe(W_PIC);
    });

    it('fetches the picture again when the dialog gave it a new version', () => {
      open('Owner');
      vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(withPicture({ imageKey: 'k2' })) } as never);

      (el().querySelector('.picture-actions .edit-picture') as HTMLButtonElement).click();
      fixture.detectChanges();

      http.expectOne({ method: 'GET', url: `${dashboardUrl}/image-widgets/${W_PIC}/image?v=k2` }).flush(new Blob(['y']));
    });

    it('takes the picture off by saving the dashboard without that widget', () => {
      open('Owner');

      (el().querySelector('.picture-actions .remove-widget') as HTMLButtonElement).click();

      const request = http.expectOne({ method: 'PUT', url: dashboardUrl });
      expect(request.request.body.definition.widgets.map((w: { id: string }) => w.id)).toEqual([W_BAR]);
      request.flush({ ...withPicture(), definition: { widgets: [{ id: W_BAR, chartId: C_BAR }], filters: [] } });
      http.expectOne(runUrl).flush({ widgets: [] });
    });

    it('keeps everything about a picture when the layout is saved, and moves it', () => {
      open('Owner');
      buttonByText('Edit layout').click();
      fixture.detectChanges();
      const component = fixture.componentInstance as unknown as { draft: { set(v: unknown): void }; items(): { id: string; x: number; y: number }[] };
      component.draft.set(component.items().map((i) => (i.id === W_PIC ? { ...i, x: 0, y: 6 } : i)));
      fixture.detectChanges();

      buttonByText('Save layout').click();

      const request = http.expectOne({ method: 'PUT', url: dashboardUrl });
      const saved = request.request.body.definition.widgets.find((w: { id: string }) => w.id === W_PIC);
      expect(saved).toMatchObject({
        kind: 'Image', imageKey: 'k1', imageFit: 'cover', alt: 'Logo', linkUrl: 'https://example.com', layout: { x: 0, y: 6, w: 4, h: 4 },
      });
      request.flush(withPicture());
    });
  });

  // ---- text widgets ----

  describe('text widgets', () => {
    const W_TEXT = 'f0000000-0000-0000-0000-000000000001';
    const withText = () => ({
      ...dashboard(),
      definition: {
        widgets: [
          { id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 6, h: 5 } },
          { id: W_TEXT, chartId: '00000000-0000-0000-0000-000000000000', kind: 'Text', text: '# Notes\nSee **below**', textAlign: 'center', textSize: 'large', layout: { x: 0, y: 5, w: 12, h: 2 } },
        ],
        filters: [],
      },
    });
    const open = (role: 'Viewer' | 'Editor' | 'Owner' = 'Editor') => {
      load(role, withText());
      http.expectOne(runUrl).flush({ widgets: [{ widgetId: W_BAR, result: ok([['Auckland', 3]]), error: null, appliedFilters: [] }] });
      fixture.detectChanges();
    };

    it('draws the text as Markdown, aligned and sized as saved, and not as a missing chart', () => {
      open('Viewer');

      const words = el().querySelector('app-markdown') as HTMLElement;
      expect(words.querySelector('h3')?.textContent).toBe('Notes');
      expect(words.querySelector('strong')?.textContent).toBe('below');
      expect(words.classList).toContain('center');
      expect(words.classList).toContain('large');
      expect(el().textContent).not.toContain('Chart not found');
    });

    it('offers editors Add text, Edit text and Remove, and viewers none of them', () => {
      open('Editor');
      expect(buttonByText('Add text')).toBeDefined();
      expect(el().querySelector('.picture-actions .edit-text')).not.toBeNull();
      expect(el().querySelector('.picture-actions .remove-widget')).not.toBeNull();
    });

    it('offers viewers nothing to change', () => {
      open('Viewer');

      expect(buttonByText('Add text')).toBeUndefined();
      expect(el().querySelector('.picture-actions')).toBeNull();
    });

    it('opens the dialog for new text, and for existing text with that widget, without running the charts again', () => {
      open('Owner');
      const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(withText()) } as never);

      buttonByText('Add text').click();
      fixture.detectChanges();
      (el().querySelector('.picture-actions .edit-text') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect((dialog.mock.calls[0][1] as { data: { widget?: unknown } }).data.widget).toBeUndefined();
      expect((dialog.mock.calls[1][1] as { data: { widget: { id: string } } }).data.widget.id).toBe(W_TEXT);
      http.expectNone(runUrl);
    });

    it('takes the text off by saving the dashboard without that widget', () => {
      open('Owner');

      (el().querySelector('.picture-actions .remove-widget') as HTMLButtonElement).click();

      const request = http.expectOne({ method: 'PUT', url: dashboardUrl });
      expect(request.request.body.definition.widgets.map((w: { id: string }) => w.id)).toEqual([W_BAR]);
      request.flush({ ...withText(), definition: { widgets: [{ id: W_BAR, chartId: C_BAR }], filters: [] } });
      http.expectOne(runUrl).flush({ widgets: [] });
    });

    it('keeps the chart in the Add chart list apart from the text', () => {
      open('Owner');

      // The text widget has an empty chart id: it must not hide any chart, only the chart that is on the dashboard.
      const component = fixture.componentInstance as unknown as { addCharts(d: unknown): void; dashboard(): unknown };
      const dialog = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);
      component.addCharts(component.dashboard());
      http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);

      const offered = (dialog.mock.calls[0][1] as { data: { charts: { id: string }[] } }).data.charts.map((c) => c.id);
      expect(offered).toEqual([C_TABLE, C_SPARE]);
    });
  });

  // ---- tabs ----

  describe('tabs', () => {
    const T1 = 'a1000000-0000-0000-0000-000000000001';
    const T2 = 'a1000000-0000-0000-0000-000000000002';
    const tabbed = (over: Record<string, unknown> = {}) => ({
      ...dashboard(),
      definition: {
        widgets: [
          { id: W_BAR, chartId: C_BAR, tabId: T1, layout: { x: 0, y: 0, w: 6, h: 5 } },
          { id: W_TABLE, chartId: C_TABLE, tabId: T2, layout: { x: 0, y: 0, w: 6, h: 5 } },
        ],
        filters: [],
        tabs: [{ id: T1, name: 'Overview' }, { id: T2, name: 'Costs' }],
        ...over,
      },
    });
    const open = (role: 'Viewer' | 'Editor' | 'Owner' = 'Editor', def: object = tabbed()) => {
      load(role, def);
      http.expectOne(runUrl).flush({ widgets: [] });
      fixture.detectChanges();
    };
    const tabNames = () => Array.from(el().querySelectorAll('[role=tab]')).map((x) => x.textContent?.trim());
    const gridTitles = () => Array.from(el().querySelectorAll('app-dashboard-grid mat-card-title')).map((x) => x.textContent?.trim());
    const clickTab = (name: string) => {
      (Array.from(el().querySelectorAll('[role=tab]')).find((x) => x.textContent?.trim() === name) as HTMLButtonElement).click();
      fixture.detectChanges();
    };
    const dialog = (result: unknown) => vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(result) } as never);
    const lastSave = () => http.expectOne({ method: 'PUT', url: dashboardUrl });

    it('shows no tab bar to a viewer of a dashboard without tabs, and the widgets as before', () => {
      open('Viewer', dashboard());

      expect(el().querySelector('app-dashboard-tab-bar .bar')).toBeNull();
      expect(gridTitles().length).toBe(2);
    });

    it('shows the tabs and only the widgets of the first one to begin with', () => {
      open('Viewer');

      expect(tabNames()).toEqual(['Overview', 'Costs']);
      expect(gridTitles()).toEqual(['Revenue by region']);
    });

    it('switches to another tab and its widgets', () => {
      open('Viewer');

      clickTab('Costs');

      expect(gridTitles()).toEqual(['Orders']);
      expect(el().querySelector('.tab.active .name')?.textContent).toBe('Costs');
    });

    it('puts a widget with no tab on the first one', () => {
      open('Viewer', tabbed({ widgets: [{ id: W_BAR, chartId: C_BAR, layout: { x: 0, y: 0, w: 6, h: 5 } }] }));

      expect(gridTitles()).toEqual(['Revenue by region']);
    });

    it('does not run the charts again when switching tabs', () => {
      open('Viewer');

      clickTab('Costs');
      clickTab('Overview');

      http.expectNone(runUrl);
    });

    it('turns one page into two tabs: what is there becomes the first, the new one is second and shows', () => {
      open('Owner', dashboard(false));
      dialog('Costs');

      (el().querySelector('app-dashboard-tab-bar .add') as HTMLButtonElement).click();
      fixture.detectChanges();

      const request = lastSave();
      expect(request.request.body.definition.tabs).toEqual([{ id: '00000000-0000-0000-0000-000000000000', name: 'Main' }, { id: '00000000-0000-0000-0000-000000000000', name: 'Costs' }]);
      request.flush({ ...tabbed(), definition: { ...tabbed().definition, widgets: [{ id: W_BAR, chartId: C_BAR, tabId: T1, layout: { x: 0, y: 0, w: 6, h: 5 } }] } });
      fixture.detectChanges();

      expect(tabNames()).toEqual(['Overview', 'Costs']);
      expect(el().querySelector('.tab.active .name')?.textContent).toBe('Costs');
      http.expectNone(runUrl);
    });

    it('does not call the first tab Main when the new one is called that', () => {
      open('Owner', dashboard(false));
      dialog('main');

      (el().querySelector('app-dashboard-tab-bar .add') as HTMLButtonElement).click();

      const request = lastSave();
      expect(request.request.body.definition.tabs.map((x: { name: string }) => x.name)).toEqual(['Overview', 'main']);
      request.flush(tabbed());
    });

    it('adds a tab to the ones there are, and offers the names taken so far', () => {
      open('Owner');
      const picker = dialog('People');

      (el().querySelector('app-dashboard-tab-bar .add') as HTMLButtonElement).click();

      expect((picker.mock.calls[0][1] as { data: { taken: string[] } }).data.taken).toEqual(['Overview', 'Costs']);
      const request = lastSave();
      expect(request.request.body.definition.tabs.map((x: { name: string }) => x.name)).toEqual(['Overview', 'Costs', 'People']);
      request.flush(tabbed());
    });

    it('renames the tab on show', () => {
      open('Owner');
      dialog('Summary');

      (el().querySelector('.tab.active .more') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Rename') as HTMLButtonElement).click();

      const request = lastSave();
      expect(request.request.body.definition.tabs).toEqual([{ id: T1, name: 'Summary' }, { id: T2, name: 'Costs' }]);
      request.flush(tabbed());
    });

    it('removes a tab after asking, sending its widgets to the first tab; two tabs left over one become a single page', () => {
      open('Owner');
      clickTab('Costs');
      vi.spyOn(window, 'confirm').mockReturnValue(true);

      (el().querySelector('.tab.active .more') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();

      expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('1 widget on it moves to the first tab'));
      const request = lastSave();
      expect(request.request.body.definition.tabs).toEqual([]);
      expect(request.request.body.definition.widgets.every((w: { tabId: unknown }) => w.tabId === null)).toBe(true);
      const places = request.request.body.definition.widgets.map((w: { layout: { x: number; y: number } }) => `${w.layout.x},${w.layout.y}`);
      expect(new Set(places).size).toBe(2); // they no longer share cells: the moved one takes the first free place
      request.flush(dashboard());
    });

    it('keeps the tabs when more than one is left', () => {
      const three = tabbed({ tabs: [{ id: T1, name: 'Overview' }, { id: T2, name: 'Costs' }, { id: 'a1000000-0000-0000-0000-000000000003', name: 'People' }] });
      open('Owner', three);
      clickTab('Costs');
      vi.spyOn(window, 'confirm').mockReturnValue(true);

      (el().querySelector('.tab.active .more') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();

      const request = lastSave();
      expect(request.request.body.definition.tabs.map((x: { name: string }) => x.name)).toEqual(['Overview', 'People']);
      expect(request.request.body.definition.widgets.map((w: { tabId: string }) => w.tabId)).toEqual([T1, T1]);
      request.flush(tabbed());
    });

    it('does nothing when the removal is not confirmed', () => {
      open('Owner');
      vi.spyOn(window, 'confirm').mockReturnValue(false);

      (el().querySelector('.tab.active .more') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();

      http.expectNone({ method: 'PUT', url: dashboardUrl });
    });

    it('moves a tab to the right', () => {
      open('Owner');

      (el().querySelector('.tab.active .more') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Move right') as HTMLButtonElement).click();

      const request = lastSave();
      expect(request.request.body.definition.tabs.map((x: { id: string }) => x.id)).toEqual([T2, T1]);
      request.flush(tabbed());
    });

    it('moves a widget to another tab, to its first free place there, and puts the rest of its old tab together', () => {
      open('Owner');

      (el().querySelector('app-move-to-tab .move') as HTMLButtonElement).click();
      fixture.detectChanges();
      (Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Costs') as HTMLButtonElement).click();

      const request = lastSave();
      const moved = request.request.body.definition.widgets.find((w: { id: string }) => w.id === W_BAR);
      expect(moved.tabId).toBe(T2);
      expect(moved.layout).toEqual({ x: 6, y: 0, w: 6, h: 5 }); // beside the chart already on that tab
      request.flush(tabbed());
      http.expectNone(runUrl);
    });

    it('offers no moving between tabs on a dashboard without tabs, or to a viewer', () => {
      open('Owner', dashboard(false));
      expect(el().querySelector('app-move-to-tab .move')).toBeNull();
    });

    it('puts new charts, text and pictures on the tab on show', () => {
      open('Owner');
      clickTab('Costs');
      const picker = dialog(undefined);

      buttonByText('Add text').click();
      buttonByText('Add picture').click();

      expect((picker.mock.calls[0][1] as { data: { tabId: string } }).data.tabId).toBe(T2);
      expect((picker.mock.calls[1][1] as { data: { tabId: string } }).data.tabId).toBe(T2);
    });

    it('cannot change or leave the tab while the layout is being arranged, and arranges the tab it started on', () => {
      open('Owner');
      clickTab('Costs');

      buttonByText('Edit layout').click();
      fixture.detectChanges();
      expect(el().querySelector('app-dashboard-tab-bar .more')).toBeNull();
      clickTab('Overview');

      expect(gridTitles()).toEqual(['Orders']); // still the tab it started on
    });

    it('saves a layout for the tab on show and leaves the places on the other tabs alone', () => {
      open('Owner');
      clickTab('Costs');
      buttonByText('Edit layout').click();
      fixture.detectChanges();
      const component = fixture.componentInstance as unknown as { draft: { set(v: unknown): void }; items(): { id: string; x: number; y: number }[] };
      component.draft.set(component.items().map((i) => ({ ...i, x: 6, y: 3 })));
      fixture.detectChanges();

      buttonByText('Save layout').click();

      const request = lastSave();
      const widgets = request.request.body.definition.widgets;
      expect(widgets.find((w: { id: string }) => w.id === W_TABLE).layout).toMatchObject({ x: 6 });
      expect(widgets.find((w: { id: string }) => w.id === W_BAR).layout).toEqual({ x: 0, y: 0, w: 6, h: 5 });
      expect(request.request.body.definition.tabs.length).toBe(2);
      request.flush(tabbed());
    });

    it('adds the charts chosen to the tab on show', () => {
      open('Owner');
      clickTab('Costs');
      const picker = dialog([C_SPARE]);

      buttonByText('Add chart').click();
      http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);

      const request = lastSave();
      expect(picker).toHaveBeenCalled();
      const added = request.request.body.definition.widgets.find((w: { chartId: string }) => w.chartId === C_SPARE);
      expect(added.tabId).toBe(T2);
      request.flush(tabbed());
      http.expectOne(runUrl).flush({ widgets: [] }); // the new chart has to be run
    });
  });

  // ---- duplicate and export ----

  describe('duplicate and export', () => {
    const open = (role: 'Viewer' | 'Editor' | 'Owner') => {
      load(role);
      http.expectOne(runUrl).flush({ widgets: [] });
      fixture.detectChanges();
    };

    it('offers Duplicate and Export to editors and not to viewers', () => {
      open('Viewer');
      expect(buttonByText('Duplicate')).toBeUndefined();
      expect(buttonByText('Export')).toBeUndefined();
    });

    it('shows them to an editor', () => {
      open('Editor');
      expect(buttonByText('Duplicate')).toBeDefined();
      expect(buttonByText('Export')).toBeDefined();
    });

    it('makes a copy and reports it, without running anything', () => {
      open('Owner');
      const copied: unknown[] = [];
      fixture.componentInstance.duplicated.subscribe((c) => copied.push(c));

      buttonByText('Duplicate').click();
      const request = http.expectOne({ method: 'POST', url: `${dashboardUrl}/duplicate` });
      expect(request.request.body).toEqual({});
      request.flush({ ...dashboard(), id: 'copy-id', name: 'Copy of Sales overview' });

      expect(copied).toEqual([expect.objectContaining({ id: 'copy-id', name: 'Copy of Sales overview' })]);
      http.expectNone(runUrl);
    });

    it('shows the reason when the copy fails, and can try again', () => {
      open('Owner');

      buttonByText('Duplicate').click();
      http.expectOne({ method: 'POST', url: `${dashboardUrl}/duplicate` }).flush(
        { title: 'Conflict', detail: 'A dashboard with that name already exists.' }, { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('already exists');
      expect(buttonByText('Duplicate').disabled).toBe(false);
    });

    it('saves the dashboard as a file named after it', async () => {
      open('Owner');
      const clicked: HTMLAnchorElement[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { clicked.push(this); });
      const made = vi.fn(() => 'blob:file');
      (URL as unknown as Record<string, unknown>)['createObjectURL'] = made;
      (URL as unknown as Record<string, unknown>)['revokeObjectURL'] = vi.fn();

      buttonByText('Export').click();
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/export` }).flush({ format: 'databoard-dashboard', version: 1, name: 'Sales overview' });

      expect(clicked.map((a) => a.download)).toEqual(['Sales overview.databoard.json']);
      const blob = made.mock.calls[0] as unknown as [Blob];
      expect(JSON.parse(await blob[0].text())).toMatchObject({ format: 'databoard-dashboard', name: 'Sales overview' });
    });

    it('shows the reason when the export fails', () => {
      open('Owner');

      buttonByText('Export').click();
      http.expectOne({ method: 'GET', url: `${dashboardUrl}/export` }).flush(
        { title: 'Bad request', errors: { Pictures: ['The pictures on this dashboard are too big to export in one file.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('too big to export');
    });
  });});
