import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardsComponent } from './dashboards.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DATASET = '33333333-3333-3333-3333-333333333333';

const summary = (id: string, name: string, widgetCount = 0, chartIds: string[] = [], folderId: string | null = null) => ({
  id, name, description: null, widgetCount, updatedUtc: '', chartIds, folderId,
});
const widget = (id: string, chartId: string) => ({ id, chartId });
const full = (id: string, name: string, widgets: unknown[] = []) => ({
  id, name, description: null, createdUtc: '', updatedUtc: '', definition: { widgets, filters: [] },
});
const chart = (id: string, name: string, type = 'Bar') => ({
  id, name, description: 'About ' + name, datasetId: DATASET, type, createdUtc: '', updatedUtc: '',
  definition: { query: { measures: [{ aggregation: 'Count' }] }, style: { legend: true, labels: false, palette: 'default' } },
});
const datasets = [{ id: DATASET, dataSourceId: 'x', name: 'Orders', description: null, sql: null, columns: [], createdUtc: '', updatedUtc: '' }];
const result = { columns: [{ name: 'n', dataType: 'bigint' }], rows: [[3]], truncated: false, sql: null };

describe('DashboardsComponent', () => {
  let fixture: ComponentFixture<DashboardsComponent>;
  let http: HttpTestingController;
  let routeParams: Record<string, string>;
  let queryParams: Record<string, string>;
  const el = () => fixture.nativeElement as HTMLElement;

  const item = (index: number) => el().querySelectorAll('button.item')[index] as HTMLButtonElement;
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const buttonLabels = () => Array.from(el().querySelectorAll('button, a.mat-mdc-button, a[mat-flat-button]')).map((b) => b.textContent?.trim());
  const toggle = (label: string) =>
    Array.from(el().querySelectorAll('mat-button-toggle button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  const start = (role: 'Viewer' | 'Editor' | 'Owner', dashboards: unknown[], charts: unknown[] = [], dashboardFolders: unknown[] = [], chartFolders: unknown[] = [], sharedWithUs: unknown[] = []) => {
    fixture = TestBed.createComponent(DashboardsComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(`/api/organizations/${ORG}/shared-dashboards`).flush(sharedWithUs);
    http.expectOne(`/api/organizations/${ORG}/folders?kind=Dashboard`).flush(dashboardFolders);
    http.expectOne(`/api/organizations/${ORG}/folders?kind=Chart`).flush(chartFolders);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush(dashboards);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts` }).flush(charts);
    fixture.detectChanges();
  };

  /** A dashboard picked on the right loads its own data: the charts, the datasets, the dashboard, then its run. */
  const answerDashboard = (id: string, name: string, widgets: unknown[] = [], charts: unknown[] = []) => {
    http.expectOne(`/api/organizations/${ORG}/charts`).flush(charts);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/${id}` }).flush(full(id, name, widgets));
    http.expectOne(`/api/organizations/${ORG}/dashboards/${id}/run`).flush({ widgets: [] });
    fixture.detectChanges();
  };

  /** A chart picked on the right runs itself. */
  const answerChart = (id: string) => {
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts/${id}/run` }).flush(result);
    fixture.detectChanges();
  };

  beforeEach(() => {
    routeParams = { id: ORG };
    queryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get paramMap() { return convertToParamMap(routeParams); },
              get queryParamMap() { return convertToParamMap(queryParams); },
            },
          },
        },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ---- dashboards ----

  it('lists dashboards on the left with their chart counts and asks for a selection on the right', () => {
    start('Viewer', [summary('d1', 'Sales', 3), summary('d2', 'Zero', 1)]);

    const lines = Array.from(el().querySelectorAll('button.item .sub')).map((l) => l.textContent?.trim());
    expect(lines).toEqual(['3 charts', '1 chart']);
    expect(el().querySelector('.detail')?.textContent).toContain('Select a dashboard to see it.');
  });

  it('has no tabs', () => {
    start('Viewer', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');

    expect(el().querySelector('[role=tablist]')).toBeNull();
  });

  it('tells editors how to start and offers nothing to create to viewers', () => {
    start('Viewer', []);

    expect(el().textContent).toContain('No dashboards yet.');
    expect(buttonLabels()).not.toContain('New dashboard');
  });

  it('shows the clicked dashboard on the right and marks it in the list', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Travel')]);

    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Sales');
    expect(item(0).closest('.row')?.classList.contains('active')).toBe(true);
    expect(item(1).closest('.row')?.classList.contains('active')).toBe(false);
  });

  it('replaces the dashboard on the right when another one is clicked', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Travel')]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');

    item(1).click();
    fixture.detectChanges();
    answerDashboard('d2', 'Travel');

    expect(el().querySelectorAll('app-dashboard').length).toBe(1);
    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Travel');
  });

  it('opens the dashboard named in the address', () => {
    routeParams = { id: ORG, dashboardId: 'd2' };

    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Travel')]);
    answerDashboard('d2', 'Travel');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Travel');
  });

  it('opens the new dashboard dialog and then shows the created dashboard', () => {
    start('Editor', []);
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(full('d9', 'Fresh')) } as never);

    button('New dashboard').click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG, folderId: null });
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d9', 'Fresh')]);
    answerDashboard('d9', 'Fresh');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Fresh');
    expect(el().querySelector('button.item .sub')?.textContent).toContain('0 charts');
  });

  it('keeps a dashboard\'s chart count in the list current after its charts change', () => {
    const c1 = chart('c1', 'One');
    const c2 = chart('c2', 'Two');
    start('Editor', [summary('d1', 'Sales', 2, ['c1', 'c2'])], [c1, c2]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales', [widget('w1', 'c1'), widget('w2', 'c2')], [c1, c2]);

    (el().querySelector('button.remove-widget') as HTMLButtonElement).click();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(full('d1', 'Sales', [widget('w2', 'c2')]));
    http.expectOne(`/api/organizations/${ORG}/dashboards/d1/run`).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().querySelector('button.item .sub')?.textContent).toContain('1 chart');
  });

  it('does not count a picture or text as a chart in the list', () => {
    const c1 = chart('c1', 'One');
    start('Editor', [summary('d1', 'Sales', 1, ['c1'])], [c1]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales', [widget('w1', 'c1')], [c1]);
    const picture = { id: 'w9', chartId: '00000000-0000-0000-0000-000000000000', kind: 'Image', imageKey: 'k', imageFit: 'contain' };
    const words = { id: 'w8', chartId: '00000000-0000-0000-0000-000000000000', kind: 'Text', text: 'Notes' };
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(full('d1', 'Sales', [widget('w1', 'c1'), picture, words])) } as never);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add picture') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/d1/image-widgets/w9/image?v=k` }).flush(new Blob(['x']));
    fixture.detectChanges();

    expect(el().querySelector('button.item .sub')?.textContent).toContain('1 chart');
    expect(el().querySelector('button.item .sub')?.textContent).not.toContain('2');
  });
  it('clears the right side and reloads the list when the shown dashboard is deleted', () => {
    start('Owner', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    button('Delete dashboard').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([]);
    fixture.detectChanges();

    expect(el().querySelector('app-dashboard')).toBeNull();
    expect(el().textContent).toContain('No dashboards yet.');
  });

  // ---- charts ----

  it('switches the list to the saved charts, showing each one\'s type and dataset', () => {
    start('Viewer', [summary('d1', 'Sales')], [chart('c1', 'Revenue', 'Bar'), chart('c2', 'Share', 'Donut')]);

    toggle('Charts').click();
    fixture.detectChanges();

    const names = Array.from(el().querySelectorAll('button.item .name')).map((n) => n.textContent?.trim());
    const lines = Array.from(el().querySelectorAll('button.item .sub')).map((l) => l.textContent?.trim());
    expect(names).toEqual(['Revenue', 'Share']);
    expect(lines).toEqual(['Bar · Orders', 'Donut · Orders']);
    expect(el().querySelector('.detail')?.textContent).toContain('Select a chart to see it.');
  });

  it('tells people when there are no charts yet', () => {
    start('Viewer', []);

    toggle('Charts').click();
    fixture.detectChanges();

    expect(el().textContent).toContain('No charts yet.');
  });

  it('offers editors a New chart link to the editor and viewers nothing', () => {
    start('Editor', []);
    toggle('Charts').click();
    fixture.detectChanges();
    const link = el().querySelector('a.add');
    expect(link?.textContent?.trim()).toBe('New chart');
    expect(link?.getAttribute('href')).toBe(`/organizations/${ORG}/charts/new`);

    fixture.destroy();
    start('Viewer', []);
    toggle('Charts').click();
    fixture.detectChanges();
    expect(el().querySelector('a.add')).toBeNull();
  });

  it('shows a clicked chart on the right, drawn, with where it is used and an Edit link', () => {
    const c1 = chart('c1', 'Revenue');
    start('Editor', [summary('d1', 'Sales', 1, ['c1']), summary('d2', 'Travel', 0, [])], [c1]);
    toggle('Charts').click();
    fixture.detectChanges();

    item(0).click();
    fixture.detectChanges();
    answerChart('c1');

    const card = el().querySelector('app-chart-card') as HTMLElement;
    expect(card.querySelector('mat-card-title')?.textContent).toContain('Revenue');
    expect(card.textContent).toContain('About Revenue');
    expect(card.textContent).toContain('Used in: Sales');
    expect(card.querySelector('app-chart')).not.toBeNull();
    const edit = Array.from(card.querySelectorAll('a')).find((a) => a.textContent?.includes('Edit chart'));
    expect(edit?.getAttribute('href')).toBe(`/organizations/${ORG}/charts/c1`);
  });

  it('says when a chart is on no dashboard, and hides Edit and Delete from viewers', () => {
    start('Viewer', [summary('d1', 'Sales')], [chart('c1', 'Revenue')]);
    toggle('Charts').click();
    fixture.detectChanges();

    item(0).click();
    fixture.detectChanges();
    answerChart('c1');

    const card = el().querySelector('app-chart-card') as HTMLElement;
    expect(card.textContent).toContain('Not on any dashboard yet.');
    expect(card.querySelector('a')).toBeNull();
    // Anyone who can see the chart may export it; only editors get the buttons that change it.
    expect(Array.from(card.querySelectorAll('button')).map((b) => b.textContent?.trim())).toEqual(['Export']);
  });

  it('opens the Charts list and one chart when the address asks for it (as the editor\'s back button does)', () => {
    queryParams = { view: 'charts', chart: 'c2' };

    start('Viewer', [], [chart('c1', 'Revenue'), chart('c2', 'Share')]);
    answerChart('c2');

    expect(el().querySelector('app-chart-card mat-card-title')?.textContent).toContain('Share');
    expect(el().querySelectorAll('button.item').length).toBe(2); // the Charts list is showing
  });

  it('asks before deleting a chart, naming the dashboards it will leave, then refreshes both lists', () => {
    const c1 = chart('c1', 'Revenue');
    start('Owner', [summary('d1', 'Sales', 1, ['c1'])], [c1]);
    toggle('Charts').click();
    fixture.detectChanges();
    item(0).click();
    fixture.detectChanges();
    answerChart('c1');
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);

    button('Delete chart').click();
    expect(confirmSpy.mock.calls[0][0]).toContain('Sales');
    http.expectNone({ method: 'DELETE', url: `/api/organizations/${ORG}/charts/c1` });

    confirmSpy.mockReturnValue(true);
    button('Delete chart').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/charts/c1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts` }).flush([]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d1', 'Sales', 0, [])]);
    fixture.detectChanges();

    expect(el().querySelector('app-chart-card')).toBeNull();
    expect(el().textContent).toContain('No charts yet.');
  });

  // ---- import and copies ----

  describe('import', () => {
    const importButton = () => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Import') as HTMLButtonElement | undefined;
    const fileInput = () => el().querySelector('input[aria-label="Dashboard file"]') as HTMLInputElement;
    const importUrl = `/api/organizations/${ORG}/dashboards/import`;
    const row = (name: string) => Array.from(el().querySelectorAll<HTMLElement>('.list .row')).find((r) => r.querySelector('.name')?.textContent?.trim() === name)!;
    const choose = async (file: File) => {
      Object.defineProperty(fileInput(), 'files', { value: [file], configurable: true });
      fileInput().dispatchEvent(new Event('change'));
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve)); // File.text() answers on a later turn
      fixture.detectChanges();
    };
    const json = (value: unknown, name = 'sales.databoard.json') => new File([typeof value === 'string' ? value : JSON.stringify(value)], name, { type: 'application/json' });
    const pkg = { format: 'databoard-dashboard', version: 1, name: 'Sales' };

    it('offers viewers no Import button', () => {
      start('Viewer', []);
      expect(importButton()).toBeUndefined();
    });

    it('offers an editor an Import button', () => {
      start('Editor', []);
      expect(importButton()).toBeDefined();
    });

    it('sends the file and shows the dashboard it made, in the folder last clicked', async () => {
      start('Editor', [summary('d1', 'Old')], [], [{ id: 'f1', name: 'Reports', parentId: null }]);
      row('Reports').querySelector<HTMLButtonElement>('.main')!.click();
      fixture.detectChanges();

      await choose(json(pkg));

      const request = http.expectOne({ method: 'POST', url: importUrl });
      expect(request.request.body).toEqual({ package: pkg, name: null, folderId: 'f1' });
      request.flush({ dashboard: { ...full('new', 'Sales'), folderId: 'f1' }, missingCharts: [] });
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d1', 'Old'), summary('new', 'Sales', 0, [], 'f1')]);
      fixture.detectChanges();
      answerDashboard('new', 'Sales');

      expect(el().querySelector('.notice')).toBeNull();
      expect(el().querySelector('.fail')).toBeNull();
    });

    it('tells which charts were not found and so left out', async () => {
      start('Editor', []);

      await choose(json(pkg));
      http.expectOne({ method: 'POST', url: importUrl }).flush({ dashboard: full('new', 'Sales'), missingCharts: ['Average', 'Revenue'] });
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('new', 'Sales')]);
      fixture.detectChanges();
      answerDashboard('new', 'Sales');

      expect(el().querySelector('.notice')?.textContent).toContain('These charts were not found here');
      expect(el().querySelector('.notice')?.textContent).toContain('Average, Revenue');
    });

    it('says one chart in the singular', async () => {
      start('Editor', []);

      await choose(json(pkg));
      http.expectOne({ method: 'POST', url: importUrl }).flush({ dashboard: full('new', 'Sales'), missingCharts: ['Average'] });
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('new', 'Sales')]);
      fixture.detectChanges();
      answerDashboard('new', 'Sales');

      expect(el().querySelector('.notice')?.textContent).toContain('This chart was not found here, so its widget was left out: Average.');
    });

    it('refuses a file that is not JSON, or is some other kind of file, without sending anything', async () => {
      start('Editor', []);

      await choose(json('{ not json', 'x.json'));
      expect(el().querySelector('.fail')?.textContent).toContain('not valid JSON');

      await choose(json({ hello: 'world' }));
      expect(el().querySelector('.fail')?.textContent).toContain('not a DataBoard dashboard file');
      http.expectNone(importUrl);
    });

    it('refuses a file that is too big without reading it', async () => {
      start('Editor', []);
      const big = new File(['x'], 'big.json');
      Object.defineProperty(big, 'size', { value: 65 * 1024 * 1024 });

      await choose(big);

      expect(el().querySelector('.fail')?.textContent).toContain('too big');
      http.expectNone(importUrl);
    });

    it('shows the server\'s reason when it refuses the file, and can be tried again', async () => {
      start('Editor', []);

      await choose(json(pkg));
      http.expectOne({ method: 'POST', url: importUrl }).flush(
        { title: 'Bad request', errors: { Package: ['This file is version 2; this DataBoard reads version 1.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      fixture.detectChanges();

      expect(el().querySelector('.fail')?.textContent).toContain('version 2');
      expect(importButton()!.disabled).toBe(false);
    });

    it('clears an old message when another file is chosen', async () => {
      start('Editor', []);
      await choose(json('{ not json', 'x.json'));
      expect(el().querySelector('.fail')).not.toBeNull();

      await choose(json(pkg));
      http.expectOne({ method: 'POST', url: importUrl }).flush({ dashboard: full('new', 'Sales'), missingCharts: [] });
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('new', 'Sales')]);
      fixture.detectChanges();
      answerDashboard('new', 'Sales');

      expect(el().querySelector('.fail')).toBeNull();
    });
  });

  it('adds a copy to the list and shows it', () => {
    start('Editor', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Duplicate') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dashboards/d1/duplicate` }).flush(full('d2', 'Copy of Sales'));
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d1', 'Sales'), summary('d2', 'Copy of Sales')]);
    fixture.detectChanges();
    answerDashboard('d2', 'Copy of Sales');

    expect(Array.from(el().querySelectorAll('.list .row .name')).map((n) => n.textContent?.trim())).toEqual(['Copy of Sales', 'Sales']);
    expect(el().querySelector('mat-card-title')?.textContent).toContain('Copy of Sales');
  });

  describe('shared with us', () => {
    const shared = (id: string, name: string, owner: string, widgets = 2) =>
      ({ dashboardId: id, organizationId: 'f0000000-0000-0000-0000-000000000001', organizationName: owner, name, description: null, widgetCount: widgets, updatedUtc: '2026-10-10T00:00:00Z' });
    const links = () => Array.from(el().querySelectorAll('.shared-with-me a')) as HTMLAnchorElement[];

    it('lists what other organizations show, with who from, and opens it on its own page', () => {
      start('Viewer', [], [], [], [], [shared('s1', 'Partner KPIs', 'Partner Co', 1), shared('s2', 'Costs', 'Partner Co')]);

      expect(links().map((a) => a.textContent?.trim())).toEqual(['Partner KPIs', 'Costs']);
      expect(el().querySelector('.shared-with-me')!.textContent).toContain('from Partner Co · 1 chart');
      expect(el().querySelector('.shared-with-me')!.textContent).toContain('2 charts');
      expect(links()[0].getAttribute('href')).toBe(`/organizations/${ORG}/shared/f0000000-0000-0000-0000-000000000001/s1`);
    });

    it('shows nothing when nothing is shared, and not in the Charts view', () => {
      start('Editor', []);
      expect(el().querySelector('.shared-with-me')).toBeNull();
    });

    it('is only in the dashboards view', () => {
      start('Editor', [], [], [], [], [shared('s1', 'Partner KPIs', 'Partner Co')]);
      expect(el().querySelector('.shared-with-me')).not.toBeNull();

      toggle('Charts').click();
      fixture.detectChanges();

      expect(el().querySelector('.shared-with-me')).toBeNull();
    });
  });

  describe('who can see what', () => {
    const limited = (id: string, name: string) => ({ ...summary(id, name), restricted: true });

    it('marks a limited dashboard with a lock in the list', () => {
      start('Editor', [limited('d1', 'Staff plan'), summary('d2', 'For all')]);

      const rows = Array.from(el().querySelectorAll('.row'));
      expect(rows.filter((r) => r.querySelector('svg.lock')).map((r) => r.textContent)).toEqual([expect.stringContaining('Staff plan')]);
    });
  });
});
