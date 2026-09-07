import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardsComponent } from './dashboards.component';

const ORG = '11111111-1111-1111-1111-111111111111';

const summary = (id: string, name: string, widgetCount = 0) => ({ id, name, description: null, widgetCount, updatedUtc: '' });
const widget = (id: string) => ({ id, title: id, datasetId: 'x', chartType: 'Table', query: {} });
const full = (id: string, name: string, widgets: unknown[] = []) => ({
  id, name, description: null, createdUtc: '', updatedUtc: '', definition: { widgets, filters: [] },
});

describe('DashboardsComponent', () => {
  let fixture: ComponentFixture<DashboardsComponent>;
  let http: HttpTestingController;
  let routeParams: Record<string, string>;
  const el = () => fixture.nativeElement as HTMLElement;

  const item = (index: number) => el().querySelectorAll('button.item')[index] as HTMLButtonElement;
  const buttonLabels = () => Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());
  const button = (text: string) => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

  const start = (role: 'Viewer' | 'Editor' | 'Owner', items: unknown[]) => {
    fixture = TestBed.createComponent(DashboardsComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush(items);
    fixture.detectChanges();
  };

  /** A dashboard picked on the right loads its own data: the datasets, the dashboard, then its run. */
  const answer = (id: string, name: string, widgets: unknown[] = []) => {
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/${id}` }).flush(full(id, name, widgets));
    http.expectOne(`/api/organizations/${ORG}/dashboards/${id}/run`).flush({ widgets: [] });
    fixture.detectChanges();
  };

  beforeEach(() => {
    routeParams = { id: ORG };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { get paramMap() { return convertToParamMap(routeParams); } } } },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists dashboards on the left with their chart counts and asks for a selection on the right', () => {
    start('Viewer', [summary('d1', 'Sales', 3), summary('d2', 'Empty', 1)]);

    const lines = Array.from(el().querySelectorAll('button.item [matListItemLine]')).map((l) => l.textContent?.trim());
    expect(lines).toEqual(['3 charts', '1 chart']);
    expect(el().querySelector('.detail')?.textContent).toContain('Select a dashboard to see it.');
  });

  it('has no tabs', () => {
    start('Viewer', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales');

    expect(el().querySelector('[role=tablist]')).toBeNull();
  });

  it('tells editors how to start and offers nothing to create to viewers', () => {
    start('Viewer', []);

    expect(el().textContent).toContain('No dashboards yet.');
    expect(buttonLabels()).not.toContain('New dashboard');
  });

  it('shows the clicked dashboard on the right and marks it in the list', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);

    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Sales');
    expect(item(0).classList.contains('mdc-list-item--activated')).toBe(true);
    expect(item(1).classList.contains('mdc-list-item--activated')).toBe(false);
  });

  it('replaces the dashboard on the right when another one is clicked', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales');

    item(1).click();
    fixture.detectChanges();
    answer('d2', 'Costs');

    expect(el().querySelectorAll('app-dashboard').length).toBe(1);
    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Costs');
  });

  it('opens the dashboard named in the address', () => {
    routeParams = { id: ORG, dashboardId: 'd2' };

    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    answer('d2', 'Costs');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Costs');
  });

  it('opens the new dashboard dialog and then shows the created dashboard', () => {
    start('Editor', []);
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(full('d9', 'Fresh')) } as never);

    button('New dashboard').click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG });
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d9', 'Fresh')]);
    answer('d9', 'Fresh');

    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Fresh');
    expect(el().querySelector('button.item [matListItemLine]')?.textContent).toContain('0 charts');
  });

  it('keeps a dashboard\'s chart count in the list current after its charts change', () => {
    start('Editor', [summary('d1', 'Sales', 2)]);
    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales', [widget('w1'), widget('w2')]);

    button('Remove').click();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(full('d1', 'Sales', [widget('w2')]));
    http.expectOne(`/api/organizations/${ORG}/dashboards/d1/run`).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().querySelector('button.item [matListItemLine]')?.textContent).toContain('1 chart');
  });

  it('points editors with an empty dashboard to the datasets, where charts are made', () => {
    start('Editor', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales');

    const link = Array.from(el().querySelectorAll('app-dashboard a')).find((a) => a.textContent?.includes('Open a dataset'));
    expect(link?.getAttribute('href')).toBe(`/organizations/${ORG}/datasets`);
  });

  it('clears the right side and reloads the list when the shown dashboard is deleted', () => {
    start('Owner', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answer('d1', 'Sales');
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    button('Delete dashboard').click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([]);
    fixture.detectChanges();

    expect(el().querySelector('app-dashboard')).toBeNull();
    expect(el().textContent).toContain('No dashboards yet.');
  });
});
