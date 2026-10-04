import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
import { DashboardsComponent } from './dashboards.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DATASET = '33333333-3333-3333-3333-333333333333';
const FOLDERS = `/api/organizations/${ORG}/folders`;

const summary = (id: string, name: string, folderId: string | null = null) => ({
  id, name, description: null, widgetCount: 2, updatedUtc: '', chartIds: [], folderId,
});
const chart = (id: string, name: string, folderId: string | null = null) => ({
  id, name, description: null, datasetId: DATASET, type: 'Bar', createdUtc: '', updatedUtc: '', folderId,
  definition: { query: { measures: [{ aggregation: 'Count' }] }, style: { legend: true, labels: false, palette: 'default', stack: false, percent: false } },
});
const folder = (id: string, name: string, parentId: string | null = null) => ({ id, name, parentId });
const datasets = [{ id: DATASET, dataSourceId: 'x', name: 'Orders', description: null, sql: null, columns: [], createdUtc: '', updatedUtc: '' }];

describe('DashboardsComponent folders', () => {
  let fixture: ComponentFixture<DashboardsComponent>;
  let http: HttpTestingController;
  let queryParams: Record<string, string>;

  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('.list .row .name')).map((n) => n.textContent?.trim());
  const subs = () => Array.from(el().querySelectorAll('.list .row .sub')).map((n) => n.textContent?.trim());
  const row = (name: string) => Array.from(el().querySelectorAll<HTMLElement>('.list .row')).find((r) => r.querySelector('.name')?.textContent?.trim() === name)!;
  const trees = () => fixture.debugElement.queryAll((d) => d.name === 'app-folder-tree').map((d) => d.componentInstance);
  const toggle = (label: string) =>
    Array.from(el().querySelectorAll('mat-button-toggle button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const dialog = (result: unknown) => vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(result) } as never);

  const start = (role: 'Viewer' | 'Editor' = 'Editor') => {
    fixture = TestBed.createComponent(DashboardsComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(`/api/organizations/${ORG}/shared-dashboards`).flush([]);
    http.expectOne(`${FOLDERS}?kind=Dashboard`).flush([folder('dsales', 'Sales dashboards'), folder('darchive', 'Archive')]);
    http.expectOne(`${FOLDERS}?kind=Chart`).flush([folder('csales', 'Sales charts'), folder('cops', 'Ops')]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([summary('d1', 'Weekly', 'dsales'), summary('d2', 'Overview')]);
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/charts` }).flush([chart('c1', 'Revenue', 'csales'), chart('c2', 'Share')]);
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
    queryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }), get queryParamMap() { return convertToParamMap(queryParams); } } },
        },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows dashboards as a tree of dashboard folders, with their chart counts', () => {
    start();

    expect(names()).toEqual(['Archive', 'Sales dashboards', 'Overview']);
    expect(subs()).toEqual(['2 charts']);
  });

  it('shows charts as a tree of chart folders when the list is switched', () => {
    start();

    toggle('Charts').click();
    fixture.detectChanges();

    expect(names()).toEqual(['Ops', 'Sales charts', 'Share']);
    expect(subs()).toEqual(['Bar · Orders']);
  });

  it('moves a dashboard into a folder and shows it there', () => {
    start();

    trees()[0].move.emit({ item: { kind: 'item', id: 'd2' }, targetId: 'darchive' });
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/d2/folder` }).flush({ id: 'd2', folderId: 'darchive' });
    fixture.detectChanges();

    expect(names()).toEqual(['Archive', 'Sales dashboards']);
    expect(row('Archive').querySelector('.count')?.textContent).toBe('1');
  });

  it('moves a chart into a folder and shows it there', () => {
    start();
    toggle('Charts').click();
    fixture.detectChanges();

    trees()[0].move.emit({ item: { kind: 'item', id: 'c2' }, targetId: 'cops' });
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/charts/c2/folder` }).flush({ id: 'c2', folderId: 'cops' });
    fixture.detectChanges();

    expect(names()).toEqual(['Ops', 'Sales charts']);
    expect(row('Ops').querySelector('.count')?.textContent).toBe('1');
  });

  it('makes folders of the right kind', () => {
    start();
    const open = dialog(folder('new', 'New'));

    trees()[0].createFolder.emit(null);
    toggle('Charts').click();
    fixture.detectChanges();
    trees()[0].createFolder.emit('csales');

    expect(open.mock.calls.map((c) => (c[1] as { data: { kind: string } }).data.kind)).toEqual(['Dashboard', 'Chart']);
  });

  it('starts a new dashboard in the folder asked for', () => {
    start();
    const open = dialog(undefined);

    trees()[0].addItem.emit({ action: 'new', folderId: 'dsales' });

    expect(open.mock.calls[0][1]).toMatchObject({ data: { orgId: ORG, folderId: 'dsales' } });
  });

  it('opens the chart editor for a new chart in the folder asked for, or from the folder last clicked', () => {
    start();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    toggle('Charts').click();
    fixture.detectChanges();

    trees()[0].addItem.emit({ action: 'new', folderId: 'cops' });
    row('Sales charts').querySelector<HTMLButtonElement>('.main')!.click();
    fixture.detectChanges();
    const link = Array.from(el().querySelectorAll('a')).find((a) => a.textContent?.trim() === 'New chart')!;

    expect(navigate).toHaveBeenCalledWith(['/organizations', ORG, 'charts', 'new'], { queryParams: { folder: 'cops' } });
    expect(link.getAttribute('href')).toContain('folder=csales');
  });

  it('gives viewers the trees without any way to change them', () => {
    start('Viewer');

    expect(el().querySelector('.list .more')).toBeNull();
    expect(el().querySelector('.list button[aria-label="New folder"]')).toBeNull();
  });
});
