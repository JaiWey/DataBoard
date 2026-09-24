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

  describe('background picture', () => {
    beforeEach(() => {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:picture');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    });

    it('fetches the picture with the link and shows it the way it was saved', () => {
      open({ ...page(), background: { version: 'v9', fit: 'contain', opacity: 55 } });
      http.expectOne(runUrl).flush(run());
      http.expectOne({ method: 'GET', url: `/api/shared/dashboards/${TOKEN}/background?v=v9` }).flush(new Blob(['x']));
      fixture.detectChanges();

      const layer = el().querySelector('app-background-layer .layer') as HTMLElement;
      expect(layer.style.backgroundImage).toContain('blob:picture');
      expect(layer.style.backgroundSize).toBe('contain');
      expect(layer.style.opacity).toBe('0.55');
    });

    it('has no layer, and fetches nothing, when the dashboard has no picture', () => {
      open();
      http.expectOne(runUrl).flush(run());
      fixture.detectChanges();

      expect(el().querySelector('app-background-layer')).toBeNull();
    });

    it('still shows the dashboard when the picture cannot be fetched', () => {
      open({ ...page(), background: { version: 'v9', fit: 'cover', opacity: 100 } });
      http.expectOne(runUrl).flush(run());
      http.expectOne(`/api/shared/dashboards/${TOKEN}/background?v=v9`).flush(new Blob(['gone']), { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(el().querySelector('h1')?.textContent).toBe('Sales overview');
      expect((el().querySelector('app-background-layer .layer') as HTMLElement).style.backgroundImage).toBe('');
    });
  });

  describe('picture widgets', () => {
    const WID = 'e0000000-0000-0000-0000-000000000001';
    const withPicture = () => {
      const base = page() as { widgets: object[] };
      return { ...base, widgets: [...base.widgets, { id: WID, chartId: '00000000-0000-0000-0000-000000000000', kind: 'Image', imageKey: 'v7', imageFit: 'fill', alt: 'Logo', linkUrl: 'https://example.com', layout: { x: 0, y: 20, w: 4, h: 4 } }] };
    };

    beforeEach(() => {
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:picture');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    });

    it('shows the picture, fetched with the link and its version, and treats it as no chart', () => {
      open(withPicture());
      http.expectOne(runUrl).flush(run());
      http.expectOne({ method: 'GET', url: `/api/shared/dashboards/${TOKEN}/images/${WID}?v=v7` }).flush(new Blob(['x']));
      fixture.detectChanges();

      const img = el().querySelector('app-image-widget img') as HTMLImageElement;
      expect(img.getAttribute('src')).toBe('blob:picture');
      expect(img.getAttribute('alt')).toBe('Logo');
      expect(img.style.objectFit).toBe('fill');
      expect((el().querySelector('app-image-widget a') as HTMLAnchorElement).getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('still shows the rest of the page when the picture cannot be fetched', () => {
      open(withPicture());
      http.expectOne(runUrl).flush(run());
      http.expectOne(`/api/shared/dashboards/${TOKEN}/images/${WID}?v=v7`).flush(new Blob(['gone']), { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();

      expect(el().querySelector('h1')?.textContent).toBe('Sales overview');
      expect(el().textContent).toContain('could not be shown');
    });
  });

  describe('text widgets', () => {
    it('draws the text as Markdown, aligned and sized as saved', () => {
      const base = page() as { widgets: object[] };
      open({ ...base, widgets: [...base.widgets, { id: 'words', chartId: '00000000-0000-0000-0000-000000000000', kind: 'Text', text: '## Welcome\n[more](https://example.com)', textAlign: 'right', textSize: 'small', layout: { x: 0, y: 20, w: 12, h: 2 } }] });
      http.expectOne(runUrl).flush(run());
      fixture.detectChanges();

      const words = el().querySelector('app-markdown') as HTMLElement;
      expect(words.querySelector('h4')?.textContent).toBe('Welcome');
      expect(words.querySelector('a')?.getAttribute('rel')).toBe('noopener noreferrer');
      expect(words.classList).toContain('right');
      expect(words.classList).toContain('small');
    });
  });

  describe('tabs', () => {
    const T1 = 'a1000000-0000-0000-0000-000000000001';
    const T2 = 'a1000000-0000-0000-0000-000000000002';
    const tabbed = () => {
      const base = page() as { widgets: { id: string }[] };
      return {
        ...base,
        widgets: base.widgets.map((w, i) => ({ ...w, tabId: i === 0 ? T1 : T2 })),
        tabs: [{ id: T1, name: 'Overview' }, { id: T2, name: 'Costs' }],
      };
    };
    const titles = () => Array.from(el().querySelectorAll('app-dashboard-grid mat-card-title')).map((x) => x.textContent?.trim());

    it('shows the tabs and the widgets of the first, and switches without asking for the data again', () => {
      open(tabbed());
      http.expectOne(runUrl).flush(run());
      fixture.detectChanges();
      expect(Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.textContent?.trim())).toEqual(['Overview', 'Costs']);
      const first = titles();
      expect(first.length).toBe(1);

      (el().querySelectorAll('[role=tab]')[1] as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(titles().length).toBe(1);
      expect(titles()).not.toEqual(first);
      http.expectNone(runUrl);
    });

    it('offers a visitor no way to change the tabs', () => {
      open(tabbed());
      http.expectOne(runUrl).flush(run());
      fixture.detectChanges();

      expect(el().querySelector('.more')).toBeNull();
      expect(el().querySelector('.add')).toBeNull();
    });

    it('shows no tab bar for a dashboard that is one page', () => {
      open();
      http.expectOne(runUrl).flush(run());
      fixture.detectChanges();

      expect(el().querySelector('app-dashboard-tab-bar .bar')).toBeNull();
    });
  });});
