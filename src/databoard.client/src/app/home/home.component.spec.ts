import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { OrgContext } from '../organizations/org-context.service';
import { chooseOption } from '../shared/select.testing';
import { HomeComponent } from './home.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const OTHER = '22222222-2222-2222-2222-222222222222';

const dashboards = [
  { id: 'd1', name: 'Sales overview', description: null, widgetCount: 3, updatedUtc: '2026-10-05T00:00:00Z' },
];
const datasets = [
  { id: 's1', dataSourceId: 'x', name: 'Orders', description: null, sql: null, columns: [{}, {}], createdUtc: '', updatedUtc: '2026-10-07T00:00:00Z' },
  { id: 's2', dataSourceId: 'x', name: 'Customers', description: null, sql: null, columns: [{}], createdUtc: '', updatedUtc: '2026-10-01T00:00:00Z' },
];
const sources = [{ id: 'c1', organizationId: ORG, name: 'Shop DB', type: 'PostgreSql', createdUtc: '2026-10-06T00:00:00Z' }];

describe('HomeComponent', () => {
  let fixture: ComponentFixture<HomeComponent>;
  let http: HttpTestingController;
  let ctx: OrgContext;

  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('tbody tr td:first-child')).map((c) => c.textContent?.trim());

  const flushOrg = (id: string, d = dashboards, s = datasets, c = sources) => {
    http.expectOne(`/api/organizations/${id}/dashboards`).flush(d);
    http.expectOne(`/api/organizations/${id}/datasets`).flush(s);
    http.expectOne(`/api/organizations/${id}/data-sources`).flush(c);
    fixture.detectChanges();
  };

  const start = (orgs: unknown[] = [{ id: ORG, name: 'Acme', role: 'Owner' }]) => {
    ctx.organizations.set(orgs as never);
    ctx.loaded.set(true);
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user: () => ({ id: 'u', email: 'dev@databoard.local', displayName: 'Dev User' }) } },
      ],
    });
    ctx = TestBed.inject(OrgContext);
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HomeComponent);
  });

  afterEach(() => http.verify());

  it('shows who is signed in, the role, and a count of each kind of item', () => {
    start();
    flushOrg(ORG);

    expect(el().querySelector('.avatar')?.textContent?.trim()).toBe('DU');
    expect(el().textContent).toContain('dev@databoard.local');
    expect(el().textContent).toContain('Your role: Owner');
    const counts = Array.from(el().querySelectorAll('.counts div')).map((d) => d.textContent?.replace(/\s+/g, ' ').trim());
    expect(counts).toEqual(['Dashboards1', 'Datasets2', 'Data sources1']);
  });

  it('links quick create to the places where each thing is made', () => {
    start();
    flushOrg(ORG);

    const hrefs = Array.from(el().querySelectorAll('.tile')).map((a) => [a.textContent?.trim(), a.getAttribute('href')]);
    expect(hrefs).toEqual([
      ['Dashboard', `/organizations/${ORG}/dashboards`],
      ['Data source', `/organizations/${ORG}`],
      ['Dataset', `/organizations/${ORG}/datasets`],
      ['Organizations', '/organizations'],
    ]);
  });

  it('lists everything newest first on the Recent tab', () => {
    start();
    flushOrg(ORG);

    expect(names()).toEqual(['Orders', 'Shop DB', 'Sales overview', 'Customers']);
  });

  it('filters by tab and by search text, and can reverse the order', async () => {
    start();
    flushOrg(ORG);

    (Array.from(el().querySelectorAll('[role=tab]')).find((t) => t.textContent?.trim() === 'Datasets') as HTMLElement).click();
    await fixture.whenStable(); // Material announces the new tab asynchronously
    fixture.detectChanges();
    expect(names()).toEqual(['Orders', 'Customers']);

    const search = el().querySelector('input[aria-label="Search"]') as HTMLInputElement;
    search.value = 'cust';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(names()).toEqual(['Customers']);

    search.value = '';
    search.dispatchEvent(new Event('input'));
    (el().querySelector('th[mat-sort-header], th.mat-sort-header') as HTMLElement).click();
    fixture.detectChanges();
    expect(names()).toEqual(['Customers', 'Orders']);
  });

  it('links each row to its page', () => {
    start();
    flushOrg(ORG);

    const links = Array.from(el().querySelectorAll('tbody tr td:first-child a')).map((a) => a.getAttribute('href'));
    expect(links).toContain(`/organizations/${ORG}/dashboards/d1`);
    expect(links).toContain(`/organizations/${ORG}/datasets`);
  });

  it('shows an empty state with no English-only assumptions about data', () => {
    start();
    flushOrg(ORG, [], [], []);

    expect(el().querySelector('.empty')?.textContent).toContain('Nothing here yet.');
    expect(el().querySelector('.empty svg')).not.toBeNull(); // an illustration, with no text baked into it
  });

  it('reloads for the chosen organization and ignores a slower earlier answer', () => {
    start([{ id: ORG, name: 'Acme', role: 'Owner' }, { id: OTHER, name: 'Beta', role: 'Viewer' }]);
    const first = {
      d: http.expectOne(`/api/organizations/${ORG}/dashboards`),
      s: http.expectOne(`/api/organizations/${ORG}/datasets`),
      c: http.expectOne(`/api/organizations/${ORG}/data-sources`),
    };

    chooseOption(fixture, 'Organization', OTHER);
    flushOrg(OTHER, [], [datasets[1]], []);

    // The first organization's data arrives late and must not overwrite the second's.
    first.d.flush(dashboards);
    first.s.flush(datasets);
    first.c.flush(sources);
    fixture.detectChanges();

    expect(names()).toEqual(['Customers']);
    expect(el().textContent).toContain('Your role: Viewer');
    expect(localStorage.getItem('databoard.organization')).toBe(OTHER);
  });

  it('invites a user without an organization to create one', () => {
    start([]);

    expect(el().textContent).toContain('You are not in an organization yet.');
    expect(el().querySelector('a.primary')?.getAttribute('href')).toBe('/organizations');
  });

  it('shows an error when the organization cannot be loaded', () => {
    start();
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([]);
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush([]);
    http.expectOne(`/api/organizations/${ORG}/dashboards`).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Could not load');
  });
});
