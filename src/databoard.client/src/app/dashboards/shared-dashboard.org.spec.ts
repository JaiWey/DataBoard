import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { SharedDashboardComponent } from './shared-dashboard.component';
import { SharedDashboardsService, orgSharedAddress } from './shared-dashboards.service';

const ME = '22222222-2222-2222-2222-222222222222'; // the organization looking
const OWNER = '11111111-1111-1111-1111-111111111111'; // the organization that owns the dashboard
const DASH = '44444444-4444-4444-4444-444444444444';
const base = `/api/organizations/${ME}/shared-dashboards/${OWNER}/${DASH}`;

const style = { legend: true, labels: false, palette: 'default', stack: false };
const page = (over: Record<string, unknown> = {}) => ({
  name: 'Sales overview',
  description: null,
  widgets: [{ id: 'w1', chartId: 'c1', layout: { x: 0, y: 0, w: 12, h: 5 } }],
  filters: [],
  charts: [{ id: 'c1', name: 'Orders', type: 'Table', style, dimensionCount: 1 }],
  expiresUtc: null,
  sharedBy: 'Home Co',
  ...over,
});

describe('SharedDashboardComponent for a dashboard another organization shares', () => {
  let fixture: ComponentFixture<SharedDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ME, sourceId: OWNER, dashboardId: DASH }) } } },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const open = (body: object = page()) => {
    fixture = TestBed.createComponent(SharedDashboardComponent);
    fixture.detectChanges();
    http.expectOne(base).flush(body);
    fixture.detectChanges();
  };

  it('reads the dashboard and its data from the shared address of the organization looking', () => {
    open();

    const run = http.expectOne(`${base}/run`);
    expect(run.request.method).toBe('POST');
    expect(run.request.body).toEqual({ filters: {} });
    run.flush({ widgets: [] });
    fixture.detectChanges();
    expect(el().querySelector('h1')?.textContent).toBe('Sales overview');
  });

  it('says who shares it, and goes back to the dashboards of the organization looking', () => {
    open();
    http.expectOne(`${base}/run`).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().querySelector('.note')?.textContent).toContain('Shared by Home Co');
    expect(el().querySelector('.note')?.textContent).not.toContain('Shared view');
    expect((el().querySelector('a.back') as HTMLAnchorElement).getAttribute('href')).toBe(`/organizations/${ME}/dashboards`);
  });

  it('has no "link works until" line, since it is not a link', () => {
    open();
    http.expectOne(`${base}/run`).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().textContent).not.toContain('This link works until');
  });

  it('says so when it is not shared with this organization (any more)', () => {
    fixture = TestBed.createComponent(SharedDashboardComponent);
    fixture.detectChanges();
    http.expectOne(base).flush({ detail: 'This dashboard is not shared with your organization.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')).not.toBeNull();
    expect(el().querySelector('a.other-account')).toBeNull();
  });
});

describe('SharedDashboardsService addresses', () => {
  let api: SharedDashboardsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SharedDashboardsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds the address of a dashboard another organization shares', () => {
    expect(orgSharedAddress('me', 'owner', 'dash')).toBe('/api/organizations/me/shared-dashboards/owner/dash');
  });

  it('reads a link by its token (written safely) and a shared dashboard by its address, for every kind of request', () => {
    api.get('a b/c').subscribe();
    http.expectOne('/api/shared/dashboards/a%20b%2Fc');

    const where = orgSharedAddress('me', 'owner', 'dash');
    api.get(where).subscribe();
    http.expectOne({ method: 'GET', url: where });
    api.run(where, {}).subscribe();
    http.expectOne({ method: 'POST', url: `${where}/run` });
    api.backgroundImage(where, 'v1').subscribe();
    http.expectOne({ method: 'GET', url: `${where}/background?v=v1` });
    api.widgetImage(where, 'w1', 'v2').subscribe();
    http.expectOne({ method: 'GET', url: `${where}/images/w1?v=v2` });
  });
});
