import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { fakeMonaco } from '../sql-editor/fake-monaco.testing';
import { MONACO_LOADER } from '../sql-editor/sql-editor.component';
import { DatasetsComponent } from './datasets.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const FOLDERS = `/api/organizations/${ORG}/dataset-folders`;

const dataset = (id: string, name: string, folderId: string | null = null) => ({
  id, name, folderId, dataSourceId: '22222222-2222-2222-2222-222222222222', description: null, sql: 'SELECT 1', kind: 'Sql', definition: null,
  columns: [{ name: 'region', dataType: 'text', role: 'Dimension' }], createdUtc: '2026-10-07T00:00:00Z', updatedUtc: '2026-10-07T00:00:00Z',
});
const folder = (id: string, name: string, parentId: string | null = null) => ({ id, name, parentId });

describe('DatasetsComponent folders', () => {
  let fixture: ComponentFixture<DatasetsComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const names = () => Array.from(el().querySelectorAll('.list .row .name')).map((n) => n.textContent?.trim());
  const row = (name: string) => Array.from(el().querySelectorAll<HTMLElement>('.list .row')).find((r) => r.querySelector('.name')?.textContent?.trim() === name)!;
  const tree = () => fixture.debugElement.query((d) => d.name === 'app-dataset-tree').componentInstance;
  const dialog = (result: unknown) => vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(result) } as never);

  const load = (role: 'Viewer' | 'Editor' | 'Owner' = 'Editor') => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(FOLDERS).flush([folder('sales', 'Sales'), folder('europe', 'Europe', 'sales'), folder('archive', 'Archive')]);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([dataset('orders', 'Orders', 'europe'), dataset('zebra', 'Zebra')]);
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
    const monaco = fakeMonaco();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
        { provide: MONACO_LOADER, useValue: monaco.loader },
      ],
    });
    fixture = TestBed.createComponent(DatasetsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows the datasets as a tree of folders', () => {
    load();

    expect(names()).toEqual(['Archive', 'Sales', 'Zebra']);
  });

  it('creates a folder through the dialog and shows it', () => {
    load();
    const open = dialog(folder('ops', 'Ops'));

    tree().createFolder.emit(null);
    fixture.detectChanges();

    expect(open.mock.calls[0][1]).toMatchObject({ data: { orgId: ORG, parentId: null } });
    expect(names()).toEqual(['Archive', 'Ops', 'Sales', 'Zebra']);
  });

  it('renames a folder through the dialog', () => {
    load();
    const open = dialog(folder('archive', 'Old'));

    tree().renameFolder.emit(folder('archive', 'Archive'));
    fixture.detectChanges();

    expect(open.mock.calls[0][1]).toMatchObject({ data: { folder: { id: 'archive' } } });
    expect(names()).toEqual(['Old', 'Sales', 'Zebra']);
  });

  it('moves a dataset into a folder and shows it there', () => {
    load();

    tree().move.emit({ item: { kind: 'dataset', id: 'zebra' }, targetId: 'archive' });
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/datasets/zebra/folder` }).flush(dataset('zebra', 'Zebra', 'archive'));
    fixture.detectChanges();

    expect(names()).toEqual(['Archive', 'Sales']); // Archive is collapsed, so Zebra is inside it now
    expect(row('Archive').querySelector('.count')?.textContent).toBe('1');
  });

  it('moves a folder by updating its parent and keeping its name', () => {
    load();

    tree().move.emit({ item: { kind: 'folder', id: 'archive' }, targetId: 'sales' });
    const request = http.expectOne({ method: 'PUT', url: `${FOLDERS}/archive` });
    expect(request.request.body).toEqual({ name: 'Archive', parentId: 'sales' });
    request.flush(folder('archive', 'Archive', 'sales'));
    fixture.detectChanges();

    expect(names()).toEqual(['Sales', 'Zebra']);
  });

  it('shows the server\'s reason when a move is refused', () => {
    load();

    tree().move.emit({ item: { kind: 'folder', id: 'sales' }, targetId: 'europe' });
    http.expectOne({ method: 'PUT', url: `${FOLDERS}/sales` }).flush(
      { title: 'Validation', errors: { ParentId: ['A folder cannot be moved into itself or one of its own subfolders.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(el().querySelector('.list [role=alert]')?.textContent).toContain('cannot be moved into itself');
    expect(names()).toEqual(['Archive', 'Sales', 'Zebra']);
  });

  it('moves to the destination chosen in the Move to… dialog', () => {
    load();
    const open = dialog({ targetId: null });

    tree().moveTo.emit({ kind: 'dataset', id: 'orders' });
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/datasets/orders/folder` }).flush(dataset('orders', 'Orders', null));
    fixture.detectChanges();

    expect(open.mock.calls[0][1]).toMatchObject({ data: { item: { kind: 'dataset', id: 'orders' } } });
    expect(names()).toEqual(['Archive', 'Sales', 'Orders', 'Zebra']);
  });

  it('does not move anything when the Move to… dialog is cancelled', () => {
    load();
    dialog(undefined);

    tree().moveTo.emit({ kind: 'dataset', id: 'orders' });
  });

  it('deletes an empty folder after asking', () => {
    load();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    tree().deleteFolder.emit(folder('archive', 'Archive'));
    http.expectOne({ method: 'DELETE', url: `${FOLDERS}/archive` }).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(names()).toEqual(['Sales', 'Zebra']);
  });

  it('keeps the folder and says why when it is not empty', () => {
    load();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    tree().deleteFolder.emit(folder('sales', 'Sales'));
    http.expectOne({ method: 'DELETE', url: `${FOLDERS}/sales` }).flush(
      { title: 'Conflict', detail: 'This folder is not empty. Move or delete what is inside first.' }, { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(el().querySelector('.list [role=alert]')?.textContent).toContain('not empty');
    expect(names()).toContain('Sales');
  });

  it('does nothing when the delete is not confirmed', () => {
    load();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    tree().deleteFolder.emit(folder('archive', 'Archive'));
  });

  it('starts a new dataset in the folder it was asked for, and saves it there', () => {
    load();

    tree().addDataset.emit({ kind: 'sql', folderId: 'sales' });
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush([]);

    const tab = fixture.debugElement.query((d) => d.name === 'app-new-dataset-tab').componentInstance;
    expect(tab.folderId()).toBe('sales');
  });

  it('starts a new dataset from the Add dataset button in the last folder clicked', () => {
    load();
    row('Archive').querySelector<HTMLButtonElement>('.main')!.click(); // selects it
    fixture.detectChanges();

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add dataset') as HTMLButtonElement).click();
    fixture.detectChanges();
    Array.from(document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-item')).find((b) => b.textContent?.trim() === 'SQL dataset')!.click();
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush([]);

    const tab = fixture.debugElement.query((d) => d.name === 'app-new-dataset-tab').componentInstance;
    expect(tab.folderId()).toBe('archive');
  });

  it('gives viewers the tree without any way to change it', () => {
    load('Viewer');

    expect(names()).toEqual(['Archive', 'Sales', 'Zebra']);
    expect(el().querySelector('.list .more')).toBeNull();
    expect(el().querySelector('.list button[aria-label="New folder"]')).toBeNull();
  });
});
