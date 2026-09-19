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

const dataset = (sql: string | null) => ({
  id: '33333333-3333-3333-3333-333333333333',
  dataSourceId: '22222222-2222-2222-2222-222222222222',
  name: 'Orders',
  description: 'All orders',
  sql,
  columns: [
    { name: 'region', dataType: 'text', role: 'Dimension' },
    { name: 'amount', dataType: 'numeric(10, 2)', role: 'Measure' },
  ],
  createdUtc: '2026-10-07T00:00:00Z',
  updatedUtc: '2026-10-07T00:00:00Z',
});

describe('DatasetsComponent', () => {
  let fixture: ComponentFixture<DatasetsComponent>;
  let http: HttpTestingController;
  let monaco: ReturnType<typeof fakeMonaco>;

  const el = () => fixture.nativeElement as HTMLElement;
  /** The buttons of the detail pane (the list on the left has its own). */
  const buttonText = () => Array.from(el().querySelectorAll('.detail button')).map((b) => b.textContent?.trim());

  /** Loads the page and, unless told otherwise, clicks the first dataset in the list to show its details. */
  const load = (role: 'Viewer' | 'Editor' | 'Owner', items: unknown[], selectFirst = true, folders: unknown[] = []) => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/dataset-folders`).flush(folders);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(items);
    fixture.detectChanges();
    if (selectFirst && items.length > 0) {
      (el().querySelector('button.item') as HTMLButtonElement).click();
      fixture.detectChanges();
    }
  };

  const tabNames = () => Array.from(el().querySelectorAll('[role=tab]')).map((t) => t.textContent?.replace('●', '').trim());
  const activeTab = () => el().querySelector('.tab.active [role=tab]')?.textContent?.replace('●', '').trim();
  const itemButton = (index: number) => el().querySelectorAll('button.item')[index] as HTMLButtonElement;
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
    monaco = fakeMonaco();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
        { provide: MONACO_LOADER, useValue: monaco.loader },
      ],
    });
    fixture = TestBed.createComponent(DatasetsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('tells editors how to create the first dataset', () => {
    load('Editor', []);

    expect(el().textContent).toContain('No datasets yet.');
    expect(Array.from(el().querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Add dataset')).toBe(true);
  });

  it('does not offer adding a dataset to viewers', () => {
    load('Viewer', []);

    expect(Array.from(el().querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Add dataset')).toBe(false);
  });

  it('lists datasets on the left and asks for a selection on the right', () => {
    load('Viewer', [dataset(null)], false);

    expect(el().querySelector('button.item .name')?.textContent?.trim()).toBe('Orders');
    expect(el().querySelector('.detail')?.textContent).toContain('Select a dataset to see its details.');
  });

  it('opens each clicked dataset in its own tab and keeps the earlier one as it was', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Returns' };
    load('Viewer', [dataset(null), second]);
    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Preview') as HTMLButtonElement).click();
    http.expectOne(`/api/organizations/${ORG}/datasets/${dataset(null).id}/preview`).flush({
      columns: [{ name: 'region', dataType: 'text' }], rows: [['Auckland']], truncated: false,
    });
    fixture.detectChanges();
    expect(el().querySelector('tbody td')?.textContent).toBe('Auckland');

    itemButton(1).click();
    fixture.detectChanges();

    expect(tabNames()).toEqual(['Orders', 'Returns']);
    expect(activeTab()).toBe('Returns');
    expect(el().querySelectorAll('app-dataset-tab.inactive').length).toBe(1);

    (el().querySelectorAll('[role=tab]')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(activeTab()).toBe('Orders');
    expect(el().querySelector('app-dataset-tab:not(.inactive) tbody td')?.textContent).toBe('Auckland'); // still open, as it was left
  });

  it('shows the tab of a dataset that is already open instead of opening it twice', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Returns' };
    load('Viewer', [dataset(null), second], false);
    itemButton(0).click();
    itemButton(1).click();
    itemButton(0).click();
    fixture.detectChanges();

    expect(tabNames()).toEqual(['Orders', 'Returns']);
    expect(activeTab()).toBe('Orders');
  });

  it('closes a tab and moves to its right neighbour, else its left', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Returns' };
    const third = { ...dataset(null), id: '66666666-6666-6666-6666-666666666666', name: 'Products' };
    load('Viewer', [dataset(null), second, third], false);
    [0, 1, 2].forEach((i) => itemButton(i).click());
    fixture.detectChanges();
    (el().querySelectorAll('[role=tab]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    (el().querySelector('button[aria-label="Close Returns"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(tabNames()).toEqual(['Orders', 'Products']);
    expect(activeTab()).toBe('Products');

    (el().querySelector('button[aria-label="Close Products"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(activeTab()).toBe('Orders');

    (el().querySelector('button[aria-label="Close Orders"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('[role=tablist]')).toBeNull();
    expect(el().textContent).toContain('Select a dataset to see its details.');
  });

  it('remembers the open tabs across a reload', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Returns' };
    load('Viewer', [dataset(null), second], false);
    itemButton(0).click();
    itemButton(1).click();
    fixture.detectChanges();

    fixture.destroy();
    fixture = TestBed.createComponent(DatasetsComponent);
    load('Viewer', [dataset(null), second], false);

    expect(tabNames()).toEqual(['Orders', 'Returns']);
    expect(activeTab()).toBe('Returns');
  });

  // ---- adding a dataset ----

  const sources = [
    { id: 'src-1', organizationId: ORG, name: 'Shop DB', type: 'PostgreSql', createdUtc: '' },
    { id: 'src-2', organizationId: ORG, name: 'Warehouse', type: 'PostgreSql', createdUtc: '' },
  ];
  const addButton = () =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add dataset') as HTMLButtonElement;

  /** Clicks "Add dataset", picks a kind from its menu, and answers the data source list the new tab asks for. */
  const startNew = async (kind: 'sql' | 'view' = 'sql') => {
    addButton().click();
    fixture.detectChanges();
    const label = kind === 'sql' ? 'SQL dataset' : 'Table view dataset';
    const item = Array.from(document.querySelectorAll('button[mat-menu-item]')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
    item.click();
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush(sources);
    await settle();
  };

  it('opens an editor straight away in a new tab, with the data source picked inside it', async () => {
    load('Owner', [dataset(null)], false);

    await startNew();

    expect(tabNames()).toEqual(['New dataset']);
    expect(monaco.editors.length).toBe(1);
    const options = Array.from(el().querySelectorAll('select[aria-label="Data source"] option')).map((o) => o.textContent?.trim());
    expect(options).toEqual(['Shop DB', 'Warehouse']);
  });

  it('cannot save the new dataset until there is SQL', async () => {
    load('Owner', [], false);
    await startNew();
    const save = () => Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement;
    expect(save().disabled).toBe(true);

    monaco.editors[0].type('SELECT 1');
    await settle();

    expect(save().disabled).toBe(false);
    expect(el().querySelector('.tab .dot')).not.toBeNull();
  });

  it('asks for the name in a dialog on save and turns the tab into the created dataset', async () => {
    load('Owner', [dataset(null)], false);
    await startNew();
    const pickSource = el().querySelector('select[aria-label="Data source"]') as HTMLSelectElement;
    pickSource.value = 'src-2';
    pickSource.dispatchEvent(new Event('change'));
    monaco.editors[0].type('SELECT region FROM sales');
    await settle();
    const created = { ...dataset('SELECT region FROM sales'), id: '55555555-5555-5555-5555-555555555555', name: 'Returns' };
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(created) } as never);

    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement).click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG, dataSourceId: 'src-2', sql: 'SELECT region FROM sales', parameters: [], folderId: null });
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([dataset(null), created]);
    await settle();

    expect(tabNames()).toEqual(['Returns']);
    expect(activeTab()).toBe('Returns');
    expect(el().querySelector('.tab .dot')).toBeNull();
    expect(el().querySelectorAll('button.item').length).toBe(2);
  });

  it('keeps the tab when the save dialog is cancelled', async () => {
    load('Owner', [], false);
    await startNew();
    monaco.editors[0].type('SELECT 1');
    await settle();
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement).click();

    expect(tabNames()).toEqual(['New dataset']);
  });

  it('does not bring an unsaved new tab back after a reload', async () => {
    load('Owner', [dataset(null)], false);
    await startNew();
    fixture.destroy();

    fixture = TestBed.createComponent(DatasetsComponent);
    load('Owner', [dataset(null)], false);

    expect(tabNames()).toEqual([]);
  });

  // ---- table view datasets ----

  const schema = {
    schemas: [{
      name: 'public',
      tables: [
        { name: 'orders', kind: 'Table', columns: [
          { name: 'id', dataType: 'integer', nullable: false }, { name: 'customer_id', dataType: 'integer', nullable: false } ] },
        { name: 'Returns', kind: 'Table', columns: [
          { name: 'id', dataType: 'integer', nullable: false }, { name: 'name', dataType: 'text', nullable: true } ] },
      ],
    }],
    truncated: false,
  };
  const schemaUrl = (source: string) => `/api/organizations/${ORG}/data-sources/${source}/schema`;
  const previewUrl = (source: string) => `/api/organizations/${ORG}/data-sources/${source}/table-view/preview`;
  const byLabel = <T extends HTMLElement>(label: string) => el().querySelector(`[aria-label="${label}"]`) as T;
  const click = (label: string) => {
    byLabel<HTMLButtonElement>(label).click();
    fixture.detectChanges();
  };
  const choose = (label: string, value: string) => {
    const select = byLabel<HTMLSelectElement>(label);
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  /** Makes the next "Fields" dialog answer with these fields, as if they were ticked and Done was pressed. */
  const pickFields = (fields: string[]) =>
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(fields) } as never);

  const startView = async () => {
    load('Owner', [], false);
    await startNew('view');
    http.expectOne(schemaUrl('src-1')).flush(schema);
    await settle();
  };

  it('offers SQL and table view datasets from the add menu', async () => {
    load('Owner', [], false);
    addButton().click();
    fixture.detectChanges();

    const items = Array.from(document.querySelectorAll('button[mat-menu-item]')).map((b) => b.textContent?.trim());
    expect(items).toEqual(['SQL dataset', 'Table view dataset']);
  });

  it('opens the table view builder in a new tab, with the data source picked inside it', async () => {
    await startView();

    expect(tabNames()).toEqual(['New table view']);
    expect(monaco.editors.length).toBe(0); // no SQL editor: this kind is built from tables
    expect(el().textContent).toContain('Step 1 · Main table');
    const tables = Array.from(el().querySelectorAll('select[aria-label="Table of step 1"] option')).map((o) => o.textContent?.trim());
    expect(tables).toEqual(['Choose a table…', 'public.orders', 'public.Returns']);
  });

  it('guides through the main table, its fields and a run of just that step', async () => {
    await startView();
    expect(byLabel<HTMLButtonElement>('Run step 1').disabled).toBe(true);
    expect(byLabel<HTMLButtonElement>('Choose fields of step 1').disabled).toBe(true);

    choose('Table of step 1', '0');
    pickFields(['id', 'customer_id']);
    click('Choose fields of step 1');

    expect(Array.from(el().querySelectorAll('.chip')).map((c) => c.textContent?.trim())).toEqual(['id', 'customer_id']);
    click('Run step 1');
    const req = http.expectOne({ method: 'POST', url: previewUrl('src-1') });
    expect(req.request.body).toEqual({
      definition: { main: { schema: 'public', name: 'orders', fields: ['id', 'customer_id'] }, joins: [] },
      maxRows: 100,
    });
    req.flush({ columns: [{ name: 'id', dataType: 'int4' }], rows: [[1], [2]], truncated: false, sql: 'SELECT t0."id" FROM "public"."orders" AS t0' });
    await settle();

    expect(el().querySelectorAll('app-sql-results tbody tr').length).toBe(2);
    expect(el().querySelector('.generated pre')?.textContent).toContain('FROM "public"."orders"');
  });

  it('adds a join step, relates the tables and runs the current step with its joins', async () => {
    await startView();
    choose('Table of step 1', '0');
    pickFields(['id']);
    click('Choose fields of step 1');

    el().querySelector<HTMLButtonElement>('button.add-join')!.click();
    fixture.detectChanges();
    expect(el().textContent).toContain('Step 2 · Join table');
    expect(byLabel<HTMLButtonElement>('Run step 2').disabled).toBe(true); // no table or relationship yet

    choose('Join type of step 2', 'Left');
    choose('Table of step 2', '1');
    pickFields(['name']);
    click('Choose fields of step 2');
    const left = Array.from(el().querySelectorAll('select[aria-label^="Earlier field of condition 1"] option')).map((o) => o.textContent?.trim());
    expect(left).toEqual(['Choose…', 'orders.id', 'orders.customer_id']);
    choose('Earlier field of condition 1 in step 2', '0:customer_id');
    choose('Field of this table in condition 1 in step 2', 'id');

    click('Run step 2');
    const req = http.expectOne({ method: 'POST', url: previewUrl('src-1') });
    expect(req.request.body.definition).toEqual({
      main: { schema: 'public', name: 'orders', fields: ['id'] },
      joins: [{
        type: 'Left',
        table: { schema: 'public', name: 'Returns', fields: ['name'] },
        on: [{ leftStep: 0, leftField: 'customer_id', rightField: 'id' }],
      }],
    });
    req.flush({ columns: [], rows: [], truncated: false, sql: 'SELECT 1' });
    await settle();

    // Running step 1 again sends only the main table.
    click('Run step 1');
    expect(http.expectOne({ method: 'POST', url: previewUrl('src-1') }).request.body.definition.joins).toEqual([]);
  });

  it('can have several conditions in a join and remove the last step', async () => {
    await startView();
    choose('Table of step 1', '0');
    pickFields(['id']);
    click('Choose fields of step 1');
    el().querySelector<HTMLButtonElement>('button.add-join')!.click();
    fixture.detectChanges();

    click('Add condition to step 2');
    expect(el().querySelectorAll('.condition').length).toBe(2);
    click('Remove condition 2 of step 2');
    expect(el().querySelectorAll('.condition').length).toBe(1);

    click('Remove step 2');
    expect(el().textContent).not.toContain('Step 2');
  });

  it('shows why a step failed', async () => {
    await startView();
    choose('Table of step 1', '0');
    pickFields(['id']);
    click('Choose fields of step 1');

    click('Run step 1');
    http.expectOne({ method: 'POST', url: previewUrl('src-1') })
      .flush({ errors: { Definition: ["Main table: the table 'public.orders' was not found in the data source."] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(el().querySelector('app-sql-results [role=alert]')?.textContent).toContain('was not found');
  });

  it('saves the table view through the name dialog and turns the tab into the created dataset', async () => {
    await startView();
    choose('Table of step 1', '0');
    pickFields(['id']);
    click('Choose fields of step 1');
    const created = { ...dataset('SELECT 1'), id: '77777777-7777-7777-7777-777777777777', name: 'Orders view', kind: 'TableView' };
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(created) } as never);

    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement).click();

    expect((open.mock.calls.at(-1)![1] as { data: unknown }).data).toEqual({
      orgId: ORG,
      dataSourceId: 'src-1',
      definition: { main: { schema: 'public', name: 'orders', fields: ['id'] }, joins: [] },
      folderId: null,
    });
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([created]);
    await settle();
    http.expectOne(schemaUrl(created.dataSourceId)).flush(schema); // the saved tab's builder loads the tables

    expect(tabNames()).toEqual(['Orders view']);
  });

  it('cannot save a table view until a table and a field are chosen', async () => {
    await startView();
    const save = () => Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement;
    expect(save().disabled).toBe(true);

    choose('Table of step 1', '0');
    expect(save().disabled).toBe(true);

    pickFields(['id']);
    click('Choose fields of step 1');
    expect(save().disabled).toBe(false);
  });

  it('starts the view over when another data source is chosen', async () => {
    load('Owner', [], false);
    addButton().click();
    fixture.detectChanges();
    (Array.from(document.querySelectorAll('button[mat-menu-item]')).find((b) => b.textContent?.trim() === 'Table view dataset') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush(sources);
    await settle();
    http.expectOne(schemaUrl('src-1')).flush(schema);
    await settle();
    choose('Table of step 1', '0');

    choose('Data source', 'src-2');
    http.expectOne(schemaUrl('src-2')).flush({ schemas: [], truncated: false });
    await settle();

    expect(byLabel<HTMLSelectElement>('Table of step 1').value).toBe('');
  });

  // ---- a saved table view ----

  const viewDataset = () => ({
    ...dataset('SELECT t0."id" AS "id" FROM "public"."orders" AS t0'),
    kind: 'TableView',
    definition: { main: { schema: 'public', name: 'orders', fields: ['id'] }, joins: [] },
  });

  it('opens a saved table view in the builder with its generated SQL shown read-only', async () => {
    load('Editor', [viewDataset()]);
    http.expectOne(schemaUrl(viewDataset().dataSourceId)).flush(schema);
    await settle();

    expect(el().textContent).toContain('Step 1 · Main table');
    expect(byLabel<HTMLSelectElement>('Table of step 1').selectedOptions[0].textContent?.trim()).toBe('public.orders');
    expect(Array.from(el().querySelectorAll('.step .chip')).map((c) => c.textContent?.trim())).toEqual(['id']);
    expect(monaco.editors.length).toBe(1);
    expect(monaco.editors[0].readOnly).toBe(true);
    expect(monaco.editors[0].value).toContain('FROM "public"."orders"');
    expect(el().querySelector('button.item .sub')?.textContent).toContain('table view');
  });

  it('saves an edited table view with its definition, not SQL', async () => {
    load('Editor', [viewDataset()]);
    http.expectOne(schemaUrl(viewDataset().dataSourceId)).flush(schema);
    await settle();
    const saveButton = () => Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement;
    expect(saveButton().disabled).toBe(true);

    pickFields(['id', 'customer_id']);
    click('Choose fields of step 1');
    await settle();
    expect(saveButton().disabled).toBe(false);
    expect(el().querySelector('.tab .dot')).not.toBeNull();

    saveButton().click();
    const req = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/datasets/${dataset(null).id}` });
    expect(req.request.body).toEqual({
      name: 'Orders',
      description: 'All orders',
      definition: { main: { schema: 'public', name: 'orders', fields: ['id', 'customer_id'] }, joins: [] },
    });
    req.flush({ ...viewDataset(), definition: req.request.body.definition });
    await settle();

    expect(el().querySelector('.tab .dot')).toBeNull();
  });

  it('shows a viewer the data of a table view but neither the builder nor the SQL', async () => {
    load('Viewer', [{ ...viewDataset(), sql: null, definition: null }]);
    await settle();

    expect(el().textContent).not.toContain('Step 1');
    expect(monaco.editors.length).toBe(0);
    expect(buttonText()).toEqual(['Preview']);
  });

  it('shows columns as dimensions or measures', () => {
    load('Viewer', [dataset(null)]);

    const chips = Array.from(el().querySelectorAll('.chip')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(chips).toEqual(['region dimension', 'amount measure']);
    expect(el().querySelectorAll('.chip.measure').length).toBe(1);
  });

  it('hides the SQL and the delete button from viewers', () => {
    load('Viewer', [dataset(null)]);

    expect(monaco.editors.length).toBe(0);
    expect(buttonText()).toEqual(['Preview']);
  });

  it('shows the SQL in an editor, with delete and save, to editors', async () => {
    load('Editor', [dataset('SELECT region, amount FROM sales')]);
    await settle();

    expect(monaco.editors[0].value).toBe('SELECT region, amount FROM sales');
    expect(monaco.editors[0].readOnly).toBe(false);
    expect(buttonText()).toEqual(['Preview', 'Delete', 'Save', 'Run query']);
  });

  it('shows the SQL read-only to a viewer who is allowed to see it', async () => {
    load('Viewer', [dataset('SELECT 1')]);
    await settle();

    expect(monaco.editors[0].readOnly).toBe(true);
    expect(buttonText()).toEqual(['Preview']);
  });

  // ---- editing the SQL ----

  const saveButton = () => Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement;
  const datasetUrl = `/api/organizations/${ORG}/datasets/${dataset(null).id}`;

  it('marks the tab as unsaved while the SQL differs from the saved one', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    expect(saveButton().disabled).toBe(true);

    monaco.editors[0].type('SELECT 2');
    await settle();
    expect(saveButton().disabled).toBe(false);
    expect(el().querySelector('.tab .dot')).not.toBeNull();

    monaco.editors[0].type('SELECT 1');
    await settle();
    expect(el().querySelector('.tab .dot')).toBeNull();
  });

  it('saves the edited SQL and clears the unsaved mark', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('SELECT region FROM sales');
    await settle();

    saveButton().click();
    const req = http.expectOne({ method: 'PUT', url: datasetUrl });
    expect(req.request.body).toEqual({ name: 'Orders', description: 'All orders', sql: 'SELECT region FROM sales' });
    req.flush({ ...dataset('SELECT region FROM sales'), columns: [{ name: 'region', dataType: 'text', role: 'Dimension' }] });
    await settle();

    expect(el().querySelector('.tab .dot')).toBeNull();
    expect(el().querySelector('button.item .sub')?.textContent).toContain('1 columns');
  });

  it('saves with Ctrl+S in the editor', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('SELECT 2');
    await settle();

    monaco.editors[0].pressSave();

    expect(http.expectOne({ method: 'PUT', url: datasetUrl }).request.body.sql).toBe('SELECT 2');
  });

  it('keeps the edit and shows the message when the SQL is rejected', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('DELETE FROM sales');
    await settle();

    saveButton().click();
    http.expectOne({ method: 'PUT', url: datasetUrl })
      .flush({ errors: { Sql: ['Only SELECT queries are allowed.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Only SELECT queries are allowed.');
    expect(saveButton().disabled).toBe(false);
  });

  it('asks before closing a tab with unsaved SQL', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('SELECT 2');
    await settle();
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);

    (el().querySelector('button[aria-label="Close Orders"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(tabNames()).toEqual(['Orders']);

    confirmSpy.mockReturnValue(true);
    (el().querySelector('button[aria-label="Close Orders"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(tabNames()).toEqual([]);
  });

  it('loads and renders a preview of the data', () => {
    load('Viewer', [dataset(null)]);

    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Preview') as HTMLButtonElement).click();
    http.expectOne(`/api/organizations/${ORG}/datasets/${dataset(null).id}/preview`).flush({
      columns: [{ name: 'region', dataType: 'text' }, { name: 'amount', dataType: 'numeric' }],
      rows: [['Auckland', 10], [null, 5]],
      truncated: true,
    });
    fixture.detectChanges();

    const cells = Array.from(el().querySelectorAll('tbody td')).map((c) => c.textContent);
    expect(cells).toEqual(['Auckland', '10', '∅', '5']);
    expect(el().textContent).toContain('more rows exist');
  });

  it('asks before deleting and reloads the list afterwards', () => {
    load('Owner', [dataset('SELECT 1')]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Delete') as HTMLButtonElement).click();
    http.expectOne(`/api/organizations/${ORG}/datasets/${dataset(null).id}`).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([]);
    fixture.detectChanges();

    expect(el().textContent).toContain('No datasets yet.');
  });

  it('does nothing when the delete is cancelled', () => {
    load('Owner', [dataset('SELECT 1')]);
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Delete') as HTMLButtonElement).click();

    http.expectNone(`/api/organizations/${ORG}/datasets/${dataset(null).id}`);
  });

  // ---- running the SQL ----

  const runButton = () => Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Run query') as HTMLButtonElement;
  const sourceUrl = (id: string) => `/api/organizations/${ORG}/data-sources/${id}/preview`;
  const rows = {
    columns: [{ name: 'region', dataType: 'text' }, { name: 'total', dataType: 'numeric' }],
    rows: [['Auckland', 10], [null, 5]],
    truncated: true,
  };

  it('runs the SQL of a new dataset on the chosen data source and shows the rows below', async () => {
    load('Owner', [], false);
    await startNew();
    expect(runButton().disabled).toBe(true);
    monaco.editors[0].type('SELECT region, SUM(amount) AS total FROM sales GROUP BY region ');
    await settle();

    runButton().click();
    const req = http.expectOne({ method: 'POST', url: sourceUrl('src-1') });
    expect(req.request.body).toEqual({ sql: 'SELECT region, SUM(amount) AS total FROM sales GROUP BY region', maxRows: 100 });
    req.flush(rows);
    await settle();

    expect(Array.from(el().querySelectorAll('app-sql-results th')).map((c) => c.textContent)).toEqual(['region', 'total']);
    expect(Array.from(el().querySelectorAll('app-sql-results tbody td')).map((c) => c.textContent)).toEqual(['Auckland', '10', '∅', '5']);
    expect(el().querySelector('app-sql-results')?.textContent).toContain('2 rows (more rows exist');
  });

  it('runs the edited, unsaved SQL of an existing dataset on its own data source, with Ctrl+Enter too', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('SELECT 2');
    await settle();

    monaco.editors[0].pressRun();
    const req = http.expectOne({ method: 'POST', url: sourceUrl(dataset(null).dataSourceId) });
    expect(req.request.body).toEqual({ sql: 'SELECT 2', maxRows: 100 });
    req.flush(rows);
    await settle();

    expect(el().querySelectorAll('app-sql-results tbody tr').length).toBe(2);
  });

  it('shows why the SQL failed and no table', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();

    runButton().click();
    http.expectOne({ method: 'POST', url: sourceUrl(dataset(null).dataSourceId) })
      .flush({ errors: { Sql: ['column "nope" does not exist'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(el().querySelector('app-sql-results [role=alert]')?.textContent).toContain('column "nope" does not exist');
    expect(el().querySelector('app-sql-results table')).toBeNull();
    expect(runButton().disabled).toBe(false); // can fix the query and run again
  });

  it('offers no Run query to a viewer', async () => {
    load('Viewer', [dataset('SELECT 1')]);
    await settle();

    expect(buttonText()).not.toContain('Run query');
  });

  // ---- keeping results ----

  const cacheSelect = () => el().querySelector('select[aria-label="Cache results for"]') as HTMLSelectElement;
  const cacheUrl = `${datasetUrl}/cache`;
  const chooseCache = async (seconds: number) => {
    cacheSelect().value = String(seconds);
    cacheSelect().dispatchEvent(new Event('change'));
    await settle();
  };

  it('shows editors how long results are kept, off to begin with', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();

    expect(cacheSelect().selectedOptions[0].textContent?.trim()).toBe('Off (always live)');
    expect(Array.from(cacheSelect().options).map((o) => o.textContent?.trim())).toEqual([
      'Off (always live)', '1 minute', '5 minutes', '15 minutes', '1 hour', '6 hours', '24 hours',
    ]);
  });

  it('saves the choice at once, without Save, and updates the list', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();

    await chooseCache(300);

    const req = http.expectOne({ method: 'PUT', url: cacheUrl });
    expect(req.request.body).toEqual({ cacheSeconds: 300 });
    req.flush({ ...dataset('SELECT 1'), cacheSeconds: 300 });
    await settle();
    expect(cacheSelect().selectedOptions[0].textContent?.trim()).toBe('5 minutes');
    expect(saveButton().disabled).toBe(true); // nothing to save: it was not a change to the dataset
  });

  it('does not lose an unsaved edit of the SQL when the choice is changed', async () => {
    load('Editor', [dataset('SELECT 1')]);
    await settle();
    monaco.editors[0].type('SELECT 2');
    await settle();

    await chooseCache(60);
    http.expectOne({ method: 'PUT', url: cacheUrl }).flush({ ...dataset('SELECT 1'), cacheSeconds: 60 });
    await settle();

    expect(monaco.editors[0].value).toBe('SELECT 2');
    expect(saveButton().disabled).toBe(false);
    expect(el().querySelector('.tab .dot')).not.toBeNull();
  });

  it('shows why the choice could not be saved, and keeps the old one', async () => {
    load('Editor', [{ ...dataset('SELECT 1'), cacheSeconds: 60 }]);
    await settle();

    await chooseCache(300);
    http.expectOne({ method: 'PUT', url: cacheUrl }).flush({ errors: { seconds: ['Results can be kept for 0 to 86400 seconds.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('0 to 86400');
    expect(cacheSelect().disabled).toBe(false);
    expect(cacheSelect().selectedOptions[0].textContent?.trim()).toBe('1 minute'); // the control is back to what is saved
  });

  it('lets a setting that is not one of the choices show as itself', async () => {
    load('Editor', [{ ...dataset('SELECT 1'), cacheSeconds: 120 }]);
    await settle();

    expect(cacheSelect().selectedOptions[0].textContent?.trim()).toBe('2 minutes');
  });

  it('tells a viewer that the data can be behind, without a way to change it', async () => {
    load('Viewer', [{ ...dataset('SELECT 1'), cacheSeconds: 300 }]);
    await settle();

    expect(cacheSelect()).toBeNull();
    expect(el().textContent).toContain('Results are reused for 5 minutes');
  });

  it('says nothing to a viewer when results are not kept', async () => {
    load('Viewer', [dataset('SELECT 1')]);
    await settle();

    expect(el().textContent).not.toContain('Results are reused');
  });

  // ---- parameters ----

  const PARAMETER_SQL = 'SELECT region FROM sales WHERE region = {{region}}';
  const withParameters = (sql = PARAMETER_SQL) => ({ ...dataset(sql), parameters: [{ name: 'region', type: 'Text', default: 'Auckland' }] });
  const control = (label: string) => el().querySelector(`[aria-label="${label}"]`) as HTMLInputElement | HTMLSelectElement;
  const setControl = async (label: string, value: string, event = 'input') => {
    const c = control(label);
    c.value = value;
    c.dispatchEvent(new Event(event));
    await settle();
  };

  it('shows the parameters of a dataset with their types and defaults', async () => {
    load('Editor', [withParameters()]);
    await settle();

    expect(el().querySelector('app-parameters-panel .name')?.textContent).toBe('{{region}}');
    expect((control('Type of region') as HTMLSelectElement).value).toBe('Text');
    expect((control('Default of region') as HTMLInputElement).value).toBe('Auckland');
    expect(el().textContent).toContain('Charts and dashboards can choose its value');
  });

  it('shows a viewer who may see the SQL its parameters, but not editable', async () => {
    load('Viewer', [withParameters()]);
    await settle();

    expect((control('Default of region') as HTMLInputElement).disabled).toBe(true);
  });

  it('adds a parameter when a placeholder is written, and will not run or save it until it has a default', async () => {
    load('Editor', [dataset('SELECT region FROM sales')]);
    await settle();

    monaco.editors[0].type(PARAMETER_SQL);
    await settle();

    expect(el().querySelector('app-parameters-panel .name')?.textContent).toBe('{{region}}');
    expect(el().textContent).toContain('Give region a default');
    expect(saveButton().disabled).toBe(true);
    expect(runButton().disabled).toBe(true);

    await setControl('Default of region', 'Wellington');

    expect(el().textContent).not.toContain('Give region a default');
    expect(saveButton().disabled).toBe(false);
    expect(runButton().disabled).toBe(false);
  });

  it('runs the SQL with the parameters declared, so the placeholders can be bound', async () => {
    load('Editor', [withParameters()]);
    await settle();
    await setControl('Default of region', 'Dunedin');

    runButton().click();

    const req = http.expectOne({ method: 'POST', url: sourceUrl(dataset(null).dataSourceId) });
    expect(req.request.body).toEqual({ sql: PARAMETER_SQL, maxRows: 100, parameters: [{ name: 'region', type: 'Text', default: 'Dunedin' }] });
    req.flush(rows);
    await settle();
  });

  it('counts a changed default as an unsaved change, and saves the parameters with the SQL', async () => {
    load('Editor', [withParameters()]);
    await settle();
    expect(saveButton().disabled).toBe(true);

    await setControl('Type of region', 'Number', 'change');
    await setControl('Default of region', '5');
    expect(el().querySelector('.tab .dot')).not.toBeNull();

    saveButton().click();
    const req = http.expectOne({ method: 'PUT', url: datasetUrl });
    expect(req.request.body).toEqual({
      name: 'Orders', description: 'All orders', sql: PARAMETER_SQL, parameters: [{ name: 'region', type: 'Number', default: 5 }],
    });
    req.flush({ ...withParameters(), parameters: [{ name: 'region', type: 'Number', default: 5 }] });
    await settle();
    expect(el().querySelector('.tab .dot')).toBeNull();
  });

  it('removes the parameters when the placeholders are taken out of the SQL', async () => {
    load('Editor', [withParameters()]);
    await settle();

    monaco.editors[0].type('SELECT region FROM sales');
    await settle();
    saveButton().click();

    const req = http.expectOne({ method: 'PUT', url: datasetUrl });
    expect(req.request.body).toEqual({ name: 'Orders', description: 'All orders', sql: 'SELECT region FROM sales' }); // none sent: none kept
    req.flush(dataset('SELECT region FROM sales'));
    await settle();
  });

  it('runs a new dataset with its parameters too', async () => {
    load('Owner', [], false);
    await startNew();

    monaco.editors[0].type('SELECT region FROM sales WHERE region = {{region}} ');
    await settle();
    expect(runButton().disabled).toBe(true); // no default yet
    await setControl('Default of region', 'Auckland');
    runButton().click();

    const req = http.expectOne({ method: 'POST', url: sourceUrl('src-1') });
    expect(req.request.body).toEqual({
      sql: 'SELECT region FROM sales WHERE region = {{region}}', maxRows: 100, parameters: [{ name: 'region', type: 'Text', default: 'Auckland' }],
    });
    req.flush(rows);
    await settle();
  });
});
