import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Dataset, DatasetFolder } from './datasets.service';
import { FolderNameDialogComponent, FolderNameDialogData, MoveDialogComponent, MoveDialogData } from './folder-dialogs.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const folder = (id: string, name: string, parentId: string | null = null): DatasetFolder => ({ id, name, parentId });

describe('FolderNameDialogComponent', () => {
  let fixture: ComponentFixture<FolderNameDialogComponent>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const input = () => el().querySelector<HTMLInputElement>('input[aria-label="Folder name"]')!;
  const submit = () => el().querySelector<HTMLButtonElement>('button[type=submit]')!;

  const create = (data: FolderNameDialogData) => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(FolderNameDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  };
  const type = (value: string) => {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  afterEach(() => http.verify());

  it('creates a folder under the given parent with the trimmed name', () => {
    create({ orgId: ORG, parentId: 'sales' });
    expect(submit().textContent).toContain('Create folder');
    expect(submit().disabled).toBe(true);

    type('  Europe ');
    submit().click();

    const request = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dataset-folders` });
    expect(request.request.body).toEqual({ name: 'Europe', parentId: 'sales' });
    request.flush(folder('europe', 'Europe', 'sales'));
    expect(close).toHaveBeenCalledWith(folder('europe', 'Europe', 'sales'));
  });

  it('renames a folder and keeps it where it is', () => {
    create({ orgId: ORG, parentId: 'sales', folder: folder('europe', 'Europe', 'sales') });
    expect(input().value).toBe('Europe');

    type('EMEA');
    submit().click();

    const request = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dataset-folders/europe` });
    expect(request.request.body).toEqual({ name: 'EMEA', parentId: 'sales' });
    request.flush(folder('europe', 'EMEA', 'sales'));
    expect(close).toHaveBeenCalled();
  });

  it('will not submit a blank name or one with a slash', () => {
    create({ orgId: ORG, parentId: null });

    type('   ');
    expect(submit().disabled).toBe(true);
    type('a/b');
    expect(submit().disabled).toBe(true);
    type('a\\b');
    expect(submit().disabled).toBe(true);
    type('ok');
    expect(submit().disabled).toBe(false);
  });

  it('stays open and shows the reason when the server refuses', () => {
    create({ orgId: ORG, parentId: null });
    type('Sales');
    submit().click();

    http.expectOne(`/api/organizations/${ORG}/dataset-folders`).flush(
      { title: 'Conflict', detail: 'A folder with that name already exists here.' }, { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')?.textContent).toContain('already exists');
    expect(submit().disabled).toBe(false);
  });
});

describe('MoveDialogComponent', () => {
  let fixture: ComponentFixture<MoveDialogComponent>;
  let close: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const labels = () => Array.from(el().querySelectorAll('.option')).map((o) => o.textContent?.trim());
  const option = (name: string) => Array.from(el().querySelectorAll<HTMLLabelElement>('.option')).find((o) => o.textContent?.trim() === name)!;
  const radio = (name: string) => option(name).querySelector<HTMLInputElement>('input')!;
  const moveButton = () => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Move') as HTMLButtonElement;

  const folders = [folder('sales', 'Sales'), folder('europe', 'Europe', 'sales'), folder('archive', 'Archive')];
  const datasets = [{ id: 'orders', name: 'Orders', folderId: 'europe' } as Dataset];

  const create = (data: MoveDialogData) => {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    });
    fixture = TestBed.createComponent(MoveDialogComponent);
    fixture.detectChanges();
  };

  it('lists the top level and every folder in tree order, and names what is moved', () => {
    create({ folders, datasets, item: { kind: 'dataset', id: 'orders' } });

    expect(labels()).toEqual(['Top level', 'Archive', 'Sales', 'Europe']);
    expect(el().querySelector('h2')?.textContent).toContain('Orders');
  });

  it('does not offer where a dataset already is', () => {
    create({ folders, datasets, item: { kind: 'dataset', id: 'orders' } });

    expect(radio('Europe').disabled).toBe(true);
    expect(radio('Sales').disabled).toBe(false);
    expect(radio('Top level').disabled).toBe(false);
  });

  it('does not offer a folder itself or anything below it', () => {
    create({ folders, datasets, item: { kind: 'folder', id: 'sales' } });

    expect(['Sales', 'Europe'].map((n) => radio(n).disabled)).toEqual([true, true]);
    expect(radio('Archive').disabled).toBe(false);
  });

  it('cannot move until a destination is picked, then closes with it', () => {
    create({ folders, datasets, item: { kind: 'dataset', id: 'orders' } });
    expect(moveButton().disabled).toBe(true);

    radio('Archive').click();
    fixture.detectChanges();
    moveButton().click();

    expect(close).toHaveBeenCalledWith({ targetId: 'archive' });
  });

  it('can move to the top level', () => {
    create({ folders, datasets, item: { kind: 'dataset', id: 'orders' } });

    radio('Top level').click();
    fixture.detectChanges();
    moveButton().click();

    expect(close).toHaveBeenCalledWith({ targetId: null });
  });
});
