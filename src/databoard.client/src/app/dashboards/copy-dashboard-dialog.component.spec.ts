import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { CopyDashboardDialogComponent } from './copy-dashboard-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
const TO = '22222222-2222-2222-2222-222222222222';
const OTHER = '33333333-3333-3333-3333-333333333333';
const url = (target: string) => `/api/organizations/${ORG}/dashboards/${DASH}/copy-to/${target}`;

describe('CopyDashboardDialogComponent', () => {
  let fixture: ComponentFixture<CopyDashboardDialogComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    Array.from(el().querySelectorAll('button, a')).find((b) => b.textContent?.trim() === text) as HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { orgId: ORG, dashboardId: DASH, dashboardName: 'Sales', targets: [{ id: TO, name: 'Partner' }, { id: OTHER, name: 'Lab' }] },
        },
      ],
    });
    fixture = TestBed.createComponent(CopyDashboardDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('offers the first organization and copies into it', () => {
    expect(el().textContent).toContain('Copies "Sales"');

    button('Copy').click();

    const request = http.expectOne({ method: 'POST', url: url(TO) });
    request.flush({ dashboard: { id: 'new', name: 'Sales' }, newDatasets: 1, newCharts: 2, missingDataSources: [], skippedCharts: [] });
    fixture.detectChanges();

    expect(el().querySelector('.done')?.textContent).toContain('2 new charts and 1 new dataset');
    expect(el().querySelector('.warn')).toBeNull();
    expect(button('Open the copy')?.getAttribute('href')).toBe(`/organizations/${TO}/dashboards/new`);
  });

  it('says which data sources are missing and what was left out', () => {
    button('Copy').click();
    http.expectOne({ method: 'POST', url: url(TO) }).flush({
      dashboard: { id: 'new', name: 'Sales' }, newDatasets: 0, newCharts: 0, missingDataSources: ['Shop DB'], skippedCharts: ['Revenue', 'Average'],
    });
    fixture.detectChanges();

    const warning = el().querySelector('.warn')?.textContent ?? '';
    expect(warning).toContain('"Shop DB"');
    expect(warning).toContain('"Revenue", "Average" were left out');
  });

  it('stays open and shows the message when the copy is refused', () => {
    button('Copy').click();
    http.expectOne({ method: 'POST', url: url(TO) })
      .flush({ title: 'Forbidden', detail: 'You do not have permission.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('permission');
    expect(button('Copy').hasAttribute('disabled')).toBe(false);
  });
});
