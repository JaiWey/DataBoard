import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { SharedDashboardComponent } from './shared-dashboard.component';

const TOKEN = 'T0KEN_with-dash_and_underscore_0123456789';
const pageUrl = `/api/shared/dashboards/${TOKEN}`;
const runUrl = `${pageUrl}/run`;
const F_REGION = 'b0000000-0000-0000-0000-000000000001';

const style = { legend: true, labels: false, palette: 'default', stack: false };
const page = (over: Record<string, unknown> = {}) => ({
  name: 'Sales overview',
  description: 'Weekly numbers',
  widgets: [
    { id: 'w1', chartId: 'c1', layout: { x: 0, y: 0, w: 8, h: 5 } },
    { id: 'w2', chartId: 'c2', layout: { x: 8, y: 0, w: 4, h: 5 } },
  ],
  filters: [{ id: F_REGION, label: 'Region', operator: 'In' }],
  charts: [
    { id: 'c1', name: 'Revenue by region', type: 'Bar', style, dimensionCount: 1 },
    { id: 'c2', name: 'Orders', type: 'Table', style, dimensionCount: 1 },
  ],
  expiresUtc: '2026-11-08T00:00:00Z',
  ...over,
});

const rows = (...r: unknown[][]) => ({ columns: [{ name: 'region', dataType: 'text' }, { name: 'n', dataType: 'bigint' }], rows: r, truncated: false, sql: null });
const run = (...widgets: { widgetId: string; result: unknown; error: string | null }[]) => ({ widgets: widgets.map((w) => ({ ...w, appliedFilters: [] })) });

describe('SharedDashboardComponent', () => {
  let fixture: ComponentFixture<SharedDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());

  const open = (body: object = page()) => {
    fixture = TestBed.createComponent(SharedDashboardComponent);
    fixture.detectChanges();
    http.expectOne(pageUrl).flush(body);
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: TOKEN }) } } },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('opens the dashboard by the token in the address and runs it with no filters', () => {
    open();

    expect(http.expectOne(runUrl).request.body).toEqual({ filters: {} });
    expect(el().querySelector('h1')?.textContent).toBe('Sales overview');
    expect(el().textContent).toContain('Weekly numbers');
  });

  it('draws each chart the way it was saved, in the places it was given', () => {
    open();
    http.expectOne(runUrl).flush(run(
      { widgetId: 'w1', result: rows(['Auckland', 3]), error: null },
      { widgetId: 'w2', result: rows(['Auckland', 3], ['Dunedin', 1]), error: null },
    ));
    fixture.detectChanges();

    expect(Array.from(el().querySelectorAll('.widget mat-card-title')).map((t) => t.textContent?.trim())).toEqual(['Revenue by region', 'Orders']);
    expect(el().querySelectorAll('app-chart').length).toBe(1);
    expect(Array.from(el().querySelectorAll('tbody td')).map((c) => c.textContent)).toEqual(['Auckland', '3', 'Dunedin', '1']);
    const cells = Array.from(el().querySelectorAll('app-dashboard-grid .cell')) as HTMLElement[];
    expect(cells.map((c) => c.style.getPropertyValue('--w'))).toEqual(['8', '4']);
  });

  it('is read-only: nothing to edit, remove, share or arrange, and no way to move a widget', () => {
    open();
    http.expectOne(runUrl).flush(run({ widgetId: 'w1', result: rows(['Auckland', 3]), error: null }, { widgetId: 'w2', result: rows(['Dunedin', 1]), error: null }));
    fixture.detectChanges();

    const names = buttons().join('|');
    expect(names).not.toMatch(/Edit|Remove|Share|Add|Delete|Save layout/);
    expect(el().querySelector('.handle')).toBeNull();
    expect(el().querySelector('app-dashboard-grid .resize')).toBeNull();
    expect(el().querySelector('a')).toBeNull(); // not even a link into the app
  });

  it('lets a visitor export a chart and print the page', () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    open();
    http.expectOne(runUrl).flush(run({ widgetId: 'w1', result: rows(['Auckland', 3]), error: null }, { widgetId: 'w2', result: null, error: 'x' }));
    fixture.detectChanges();

    expect(el().querySelectorAll('app-export-menu').length).toBe(1); // none for the chart that failed
    (el().querySelector('button.print') as HTMLButtonElement).click();
    expect(print).toHaveBeenCalledTimes(1);
    print.mockRestore();
  });

  it('sends the filter values a visitor types, and clears them again', () => {
    open();
    http.expectOne(runUrl).flush(run());
    fixture.detectChanges(); // the button is disabled while the first run is on its way

    const input = el().querySelector('input[aria-label="Region"]') as HTMLInputElement;
    input.value = 'Auckland, Dunedin';
    input.dispatchEvent(new Event('input'));
    (el().querySelector('button.apply') as HTMLButtonElement).click();
    expect(http.expectOne(runUrl).request.body).toEqual({ filters: { [F_REGION]: ['Auckland', 'Dunedin'] } });

    (Array.from(el().querySelectorAll('button.apply')).find((b) => b.textContent?.trim() === 'Clear') as HTMLButtonElement).click();
    expect(http.expectOne(runUrl).request.body).toEqual({ filters: {} });
    expect(input.value).toBe('');
  });

  it('tells a visitor how old reused data is', () => {
    open();
    http.expectOne(runUrl).flush(run(
      { widgetId: 'w1', result: { ...rows(['Auckland', 3]), cachedUtc: new Date(Date.now() - 3 * 3_600_000).toISOString() }, error: null },
      { widgetId: 'w2', result: rows(['Dunedin', 1]), error: null },
    ));
    fixture.detectChanges();

    expect(Array.from(el().querySelectorAll('.cached')).map((c) => c.textContent)).toEqual(['Data from 3 h ago.']); // only the stored one
  });

  it('shows a chart that failed in place, without hiding the others', () => {
    open();
    http.expectOne(runUrl).flush(run(
      { widgetId: 'w1', result: null, error: 'This chart could not be shown.' },
      { widgetId: 'w2', result: rows(['Auckland', 3]), error: null },
    ));
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('could not be shown');
    expect(el().querySelectorAll('tbody td').length).toBe(2);
  });

  it('says when the link is not valid or has expired, and shows nothing else', () => {
    fixture = TestBed.createComponent(SharedDashboardComponent);
    fixture.detectChanges();

    http.expectOne(pageUrl).flush(
      { title: 'Not found', status: 404, detail: 'This link is not valid or has expired.' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('not valid or has expired');
    expect(el().querySelector('h1')).toBeNull();
    http.expectNone(runUrl);
  });

  it('tells a visitor to wait when there are too many requests', () => {
    open();

    http.expectOne(runUrl).flush(null, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Too many requests');
  });

  it('says when a link has no charts, and mentions when it stops working', () => {
    open(page({ widgets: [], charts: [], filters: [] }));
    http.expectOne(runUrl).flush(run());
    fixture.detectChanges();

    expect(el().textContent).toContain('This dashboard has no charts.');
    expect(el().textContent).toContain('This link works until');
    expect(buttons()).toEqual([]); // nothing to print, no filters
  });
});
