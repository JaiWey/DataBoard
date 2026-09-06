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
const full = (id: string, name: string, widgets: unknown[] = []) => ({
  id, name, description: null, createdUtc: '', updatedUtc: '', definition: { widgets, filters: [] },
});

describe('DashboardsComponent', () => {
  let fixture: ComponentFixture<DashboardsComponent>;
  let http: HttpTestingController;
  let routeParams: Record<string, string>;
  const el = () => fixture.nativeElement as HTMLElement;

  const tabNames = () => Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.textContent?.replace('●', '').trim());
  const activeTab = () => el().querySelector('.tab.active [role=tab]')?.textContent?.replace('●', '').trim();
  const item = (index: number) => el().querySelectorAll('button.item')[index] as HTMLButtonElement;
  const buttonLabels = () => Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());

  const start = (role: 'Viewer' | 'Editor' | 'Owner', items: unknown[]) => {
    fixture = TestBed.createComponent(DashboardsComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush(items);
    fixture.detectChanges();
  };

  /** A newly opened tab loads its own data: the datasets, the dashboard, then its run. */
  const answerTab = (id: string, name: string) => {
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/${id}` }).flush(full(id, name));
    http.expectOne(`/api/organizations/${ORG}/dashboards/${id}/run`).flush({ widgets: [] });
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
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

  it('tells editors how to start and offers nothing to create to viewers', () => {
    start('Viewer', []);

    expect(el().textContent).toContain('No dashboards yet.');
    expect(buttonLabels()).not.toContain('New dashboard');
  });

  it('opens a clicked dashboard in a tab and marks it in the list', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);

    item(0).click();
    fixture.detectChanges();
    answerTab('d1', 'Sales');

    expect(tabNames()).toEqual(['Sales']);
    expect(el().querySelector('app-dashboard mat-card-title')?.textContent).toContain('Sales');
    expect(item(0).classList.contains('mdc-list-item--activated')).toBe(true);
    expect(item(1).classList.contains('mdc-list-item--activated')).toBe(false);
  });

  it('keeps several dashboards open and switches between them', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    item(0).click();
    item(1).click();
    fixture.detectChanges();
    http.match(`/api/organizations/${ORG}/datasets`).forEach((r) => r.flush([]));
    ['d1', 'd2'].forEach((id) => {
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/${id}` }).flush(full(id, id === 'd1' ? 'Sales' : 'Costs'));
      http.expectOne(`/api/organizations/${ORG}/dashboards/${id}/run`).flush({ widgets: [] });
    });
    fixture.detectChanges();

    expect(tabNames()).toEqual(['Sales', 'Costs']);
    expect(activeTab()).toBe('Costs');
    expect(el().querySelectorAll('app-dashboard.inactive').length).toBe(1);

    (el().querySelectorAll('[role=tab]')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(activeTab()).toBe('Sales');

    item(1).click();
    item(0).click();
    fixture.detectChanges();
    expect(tabNames()).toEqual(['Sales', 'Costs']); // no duplicate tab
  });

  it('closes a tab and moves to a neighbour, ending with the empty message', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    item(0).click();
    item(1).click();
    fixture.detectChanges();
    http.match(`/api/organizations/${ORG}/datasets`).forEach((r) => r.flush([]));
    ['d1', 'd2'].forEach((id) => {
      http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/${id}` }).flush(full(id, id));
      http.expectOne(`/api/organizations/${ORG}/dashboards/${id}/run`).flush({ widgets: [] });
    });
    fixture.detectChanges();

    (el().querySelector('button[aria-label="Close Costs"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(tabNames()).toEqual(['Sales']);
    expect(activeTab()).toBe('Sales');

    (el().querySelector('button[aria-label="Close Sales"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('[role=tablist]')).toBeNull();
    expect(el().textContent).toContain('Select a dashboard to see it.');
  });

  it('remembers the open tabs across a reload', () => {
    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    item(0).click();
    fixture.detectChanges();
    answerTab('d1', 'Sales');
    fixture.destroy();

    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    answerTab('d1', 'Sales');

    expect(tabNames()).toEqual(['Sales']);
    expect(activeTab()).toBe('Sales');
  });

  it('opens the dashboard named in the address', () => {
    routeParams = { id: ORG, dashboardId: 'd2' };

    start('Viewer', [summary('d1', 'Sales'), summary('d2', 'Costs')]);
    answerTab('d2', 'Costs');

    expect(tabNames()).toEqual(['Costs']);
    expect(activeTab()).toBe('Costs');
  });

  it('opens the new dashboard dialog and then shows the created dashboard in a tab', () => {
    start('Editor', []);
    const created = full('d9', 'Fresh');
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(created) } as never);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'New dashboard') as HTMLButtonElement).click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG });
    fixture.detectChanges();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d9', 'Fresh')]);
    answerTab('d9', 'Fresh');

    expect(tabNames()).toEqual(['Fresh']);
    expect(el().querySelector('button.item [matListItemLine]')?.textContent).toContain('0 charts');
  });

  it('keeps a dashboard\'s chart count in the list current after its charts change', () => {
    start('Editor', [summary('d1', 'Sales', 2)]);
    item(0).click();
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([]);
    const widget = (id: string) => ({ id, title: id, datasetId: 'x', chartType: 'Table', query: {} });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(full('d1', 'Sales', [widget('w1'), widget('w2')]));
    http.expectOne(`/api/organizations/${ORG}/dashboards/d1/run`).flush({ widgets: [] });
    fixture.detectChanges();

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Remove') as HTMLButtonElement).click();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(full('d1', 'Sales', [widget('w2')]));
    http.expectOne(`/api/organizations/${ORG}/dashboards/d1/run`).flush({ widgets: [] });
    fixture.detectChanges();

    expect(el().querySelector('button.item [matListItemLine]')?.textContent).toContain('1 chart');
  });

  it('closes the tab of a deleted dashboard and reloads the list', () => {
    start('Owner', [summary('d1', 'Sales')]);
    item(0).click();
    fixture.detectChanges();
    answerTab('d1', 'Sales');
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Delete dashboard') as HTMLButtonElement).click();
    http.expectOne({ method: 'DELETE', url: `/api/organizations/${ORG}/dashboards/d1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([]);
    fixture.detectChanges();

    expect(el().querySelector('[role=tablist]')).toBeNull();
    expect(el().textContent).toContain('No dashboards yet.');
  });
});
