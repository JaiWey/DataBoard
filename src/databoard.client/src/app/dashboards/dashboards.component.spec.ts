import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardsComponent } from './dashboards.component';
import { answerConfirm } from '../shared/confirm.testing';

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
  /** What the page's screen-width query says: false is a wide screen, true a narrow one. */
  let narrow$: BehaviorSubject<BreakpointState>;
  const el = () => fixture.nativeElement as HTMLElement;

  const item = (index: number) => el().querySelectorAll('button.item')[index] as HTMLButtonElement;
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text || b.getAttribute('aria-label') === text) as HTMLButtonElement;
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
    narrow$ = new BehaviorSubject<BreakpointState>({ matches: false, breakpoints: {} });
    TestBed.configureTestingModule({
      providers: [
        { provide: BreakpointObserver, useValue: { observe: () => narrow$.asObservable() } },
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

    (el().querySelector('.widget-actions .more') as HTMLButtonElement).click();
    fixture.detectChanges();
    Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Remove')!.click();
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
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

    (el().querySelector('.add-menu') as HTMLButtonElement).click();
    fixture.detectChanges();
    Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'Picture')!.click();
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
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
    answerConfirm(true);

    button('Delete dashboard').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([]);
    fixture.detectChanges();

    expect(el().querySelector('app-dashboard')).toBeNull();
    expect(el().textContent).toContain('No dashboards yet.');
  });

  // ---- the list on the left can be folded ----

  describe('folding the list', () => {
    const drawer = () => el().querySelector('mat-sidenav') as HTMLElement;
    const folded = () => !drawer().classList.contains('mat-drawer-opened');
    const arrange = () => {
      start('Editor', [summary('d1', 'Sales', 1, ['c1'])], [chart('c1', 'One')]);
      item(0).click();
      fixture.detectChanges();
      answerDashboard('d1', 'Sales', [widget('w1', 'c1')], [chart('c1', 'One')]);
      button('Edit layout').click();
      fixture.detectChanges();
    };

    beforeEach(() => localStorage.removeItem('databoard.dashboardsListFolded'));
    afterEach(() => localStorage.removeItem('databoard.dashboardsListFolded'));

    it('is a drawer beside the dashboard, open to begin with, that closes and opens again', () => {
      start('Viewer', [summary('d1', 'Sales')]);
      expect(folded()).toBe(false);
      expect(drawer().classList).toContain('mat-drawer-side');
      expect(button('Show the list')).toBeUndefined(); // nothing to open while it is open

      button('Hide the list').click();
      fixture.detectChanges();
      expect(folded()).toBe(true);
      expect(button('Show the list')).toBeDefined();

      button('Show the list').click();
      fixture.detectChanges();
      expect(folded()).toBe(false);
      expect(button('Show the list')).toBeUndefined();
    });

    describe('on a narrow screen', () => {
      beforeEach(() => narrow$.next({ matches: true, breakpoints: {} }));

      it('is a drawer over the page, closed to begin with, whatever was remembered', () => {
        start('Viewer', [summary('d1', 'Sales')]);

        expect(drawer().classList).toContain('mat-drawer-over');
        expect(folded()).toBe(true);
        expect(localStorage.getItem('databoard.dashboardsListFolded')).toBeNull();
      });

      it('opens when asked, and closes again after something in it is chosen', () => {
        start('Viewer', [summary('d1', 'Sales')]);

        button('Show the list').click();
        fixture.detectChanges();
        expect(folded()).toBe(false);

        item(0).click();
        fixture.detectChanges();
        answerDashboard('d1', 'Sales');
        expect(folded()).toBe(true);
      });

      it('closes when the person closes it (Escape or the backdrop), and does not remember it', () => {
        start('Viewer', [summary('d1', 'Sales')]);
        button('Show the list').click();
        fixture.detectChanges();

        (fixture.debugElement.query((d) => d.name === 'mat-sidenav').componentInstance as { close(): void }).close();
        fixture.detectChanges();

        expect(folded()).toBe(true);
        expect(localStorage.getItem('databoard.dashboardsListFolded')).toBeNull();
      });
    });

    it('is remembered for the next visit', () => {
      start('Viewer', [summary('d1', 'Sales')]);
      button('Hide the list').click();
      fixture.detectChanges();
      fixture.destroy();

      start('Viewer', [summary('d1', 'Sales')]);

      expect(folded()).toBe(true);
    });

    it('folds while a dashboard is being arranged and opens again afterwards', () => {
      arrange();
      expect(folded()).toBe(true);

      button('Cancel').click();
      fixture.detectChanges();

      expect(folded()).toBe(false);
    });

    it('does not remember the fold that arranging caused', () => {
      arrange();
      fixture.destroy();

      start('Viewer', [summary('d1', 'Sales')]);

      expect(folded()).toBe(false);
    });

    it('stays as the person left it when they open the list while arranging', () => {
      arrange();

      button('Show the list').click();
      fixture.detectChanges();
      expect(folded()).toBe(false);

      button('Cancel').click();
      fixture.detectChanges();
      expect(folded()).toBe(false);
    });

    it('stays folded after arranging when the person had folded it already', () => {
      start('Editor', [summary('d1', 'Sales', 1, ['c1'])], [chart('c1', 'One')]);
      button('Hide the list').click();
      fixture.detectChanges();
      // the list is folded, so a dashboard is opened some other way (a link): select it directly
      (fixture.componentInstance as unknown as { select(id: string): void }).select('d1');
      fixture.detectChanges();
      answerDashboard('d1', 'Sales', [widget('w1', 'c1')], [chart('c1', 'One')]);
      button('Edit layout').click();
      fixture.detectChanges();
      button('Cancel').click();
      fixture.detectChanges();

      expect(folded()).toBe(true);
    });
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
    const confirmSpy = answerConfirm(false);

    button('Delete chart').click();
    expect(confirmSpy.messages[0]).toContain('Sales');
    http.expectNone({ method: 'DELETE', url: `/api/organizations/${ORG}/charts/c1` });

    confirmSpy.answer(true);
    button('Delete chart').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/charts/c1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts` }).flush([]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d1', 'Sales', 0, [])]);
    fixture.detectChanges();

    expect(el().querySelector('app-chart-card')).toBeNull();
    expect(el().textContent).toContain('No charts yet.');
  });

  // ---- copies ----

  it('adds a copy to the list and shows it', () => {
    start('Editor', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answerDashboard('d1', 'Sales');

    button('Duplicate').click();
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
