import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { DashboardsComponent } from './dashboards.component';

const ORG = '11111111-1111-1111-1111-111111111111';

describe('DashboardsComponent', () => {
  let fixture: ComponentFixture<DashboardsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  const load = (role: 'Viewer' | 'Editor', items: unknown[]) => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/dashboards`).flush(items);
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
      ],
    });
    fixture = TestBed.createComponent(DashboardsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists dashboards with their chart counts and links to them', () => {
    load('Viewer', [
      { id: 'd1', name: 'Sales', description: null, widgetCount: 3, updatedUtc: '' },
      { id: 'd2', name: 'Empty', description: null, widgetCount: 1, updatedUtc: '' },
    ]);

    expect(el().textContent).toContain('3 charts');
    expect(el().textContent).toContain('1 chart');
    const links = Array.from(el().querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(links).toContain(`/organizations/${ORG}/dashboards/d1`);
  });

  it('hides the create form from viewers', () => {
    load('Viewer', []);

    expect(el().textContent).toContain('No dashboards yet.');
    expect(el().querySelector('form')).toBeNull();
  });

  it('lets editors create a dashboard and reloads the list', () => {
    load('Editor', []);

    const input = el().querySelector('input[aria-label="Dashboard name"]') as HTMLInputElement;
    input.value = 'Sales';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();

    const post = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dashboards` });
    expect(post.request.body).toEqual({ name: 'Sales', definition: { widgets: [], filters: [] } });
    post.flush({ id: 'd1', name: 'Sales' });
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([
      { id: 'd1', name: 'Sales', description: null, widgetCount: 0, updatedUtc: '' },
    ]);
    fixture.detectChanges();

    expect(el().textContent).toContain('0 charts');
  });

  it('shows the API message when creating fails', () => {
    load('Editor', []);

    const input = el().querySelector('input[aria-label="Dashboard name"]') as HTMLInputElement;
    input.value = 'Sales';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dashboards` })
      .flush({ title: 'Conflict', detail: 'A dashboard with that name already exists.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('already exists');
  });
});
