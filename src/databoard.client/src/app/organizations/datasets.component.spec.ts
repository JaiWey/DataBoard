import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
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
  const load = (role: 'Viewer' | 'Editor' | 'Owner', items: unknown[], selectFirst = true) => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
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
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
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

    expect(el().querySelector('button.item [matListItemTitle]')?.textContent?.trim()).toBe('Orders');
    expect(el().querySelector('.detail')?.textContent).toContain('Select a dataset to see its details.');
  });

  it('opens each clicked dataset in its own tab and keeps the earlier one as it was', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    load('Viewer', [dataset(null), second]);
    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Explore') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('form.explore')).not.toBeNull();

    itemButton(1).click();
    fixture.detectChanges();

    expect(tabNames()).toEqual(['Orders', 'Customers']);
    expect(activeTab()).toBe('Customers');
    expect(el().querySelectorAll('app-dataset-tab.inactive').length).toBe(1);

    (el().querySelectorAll('[role=tab]')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(activeTab()).toBe('Orders');
    expect(el().querySelector('app-dataset-tab:not(.inactive) form.explore')).not.toBeNull(); // still open, as it was left
  });

  it('shows the tab of a dataset that is already open instead of opening it twice', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    load('Viewer', [dataset(null), second], false);
    itemButton(0).click();
    itemButton(1).click();
    itemButton(0).click();
    fixture.detectChanges();

    expect(tabNames()).toEqual(['Orders', 'Customers']);
    expect(activeTab()).toBe('Orders');
  });

  it('closes a tab and moves to its right neighbour, else its left', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    const third = { ...dataset(null), id: '66666666-6666-6666-6666-666666666666', name: 'Products' };
    load('Viewer', [dataset(null), second, third], false);
    [0, 1, 2].forEach((i) => itemButton(i).click());
    fixture.detectChanges();
    (el().querySelectorAll('[role=tab]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    (el().querySelector('button[aria-label="Close Customers"]') as HTMLButtonElement).click();
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
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    load('Viewer', [dataset(null), second], false);
    itemButton(0).click();
    itemButton(1).click();
    fixture.detectChanges();

    fixture.destroy();
    fixture = TestBed.createComponent(DatasetsComponent);
    load('Viewer', [dataset(null), second], false);

    expect(tabNames()).toEqual(['Orders', 'Customers']);
    expect(activeTab()).toBe('Customers');
  });

  // ---- adding a dataset ----

  const sources = [
    { id: 'src-1', organizationId: ORG, name: 'Shop DB', type: 'PostgreSql', createdUtc: '' },
    { id: 'src-2', organizationId: ORG, name: 'Warehouse', type: 'PostgreSql', createdUtc: '' },
  ];
  const addButton = () =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add dataset') as HTMLButtonElement;

  /** Clicks "Add dataset" and answers the data source list the new tab asks for. */
  const startNew = async () => {
    addButton().click();
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
    const created = { ...dataset('SELECT region FROM sales'), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(created) } as never);

    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement).click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG, dataSourceId: 'src-2', sql: 'SELECT region FROM sales' });
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([dataset(null), created]);
    await settle();

    expect(tabNames()).toEqual(['Customers']);
    expect(activeTab()).toBe('Customers');
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

  it('shows columns as dimensions or measures', () => {
    load('Viewer', [dataset(null)]);

    const chips = Array.from(el().querySelectorAll('.chip')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(chips).toEqual(['region dimension', 'amount measure']);
    expect(el().querySelectorAll('.chip.measure').length).toBe(1);
  });

  it('hides the SQL and the delete button from viewers', () => {
    load('Viewer', [dataset(null)]);

    expect(monaco.editors.length).toBe(0);
    expect(buttonText()).toEqual(['Preview', 'Explore']);
  });

  it('shows the SQL in an editor, with delete and save, to editors', async () => {
    load('Editor', [dataset('SELECT region, amount FROM sales')]);
    await settle();

    expect(monaco.editors[0].value).toBe('SELECT region, amount FROM sales');
    expect(monaco.editors[0].readOnly).toBe(false);
    expect(buttonText()).toEqual(['Preview', 'Explore', 'Delete', 'Save', 'Run query']);
  });

  it('shows the SQL read-only to a viewer who is allowed to see it', async () => {
    load('Viewer', [dataset('SELECT 1')]);
    await settle();

    expect(monaco.editors[0].readOnly).toBe(true);
    expect(buttonText()).toEqual(['Preview', 'Explore']);
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
    expect(el().querySelector('button.item [matListItemLine]')?.textContent).toContain('1 columns');
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

  // ---- explore ----

  const withDates = () => ({
    ...dataset('SELECT 1'),
    columns: [
      { name: 'region', dataType: 'text', role: 'Dimension' },
      { name: 'ordered_on', dataType: 'date', role: 'Dimension' },
      { name: 'amount', dataType: 'numeric(10, 2)', role: 'Measure' },
    ],
  });

  const pick = (label: string, value: string) => {
    const select = el().querySelector(`select[aria-label="${label}"]`) as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const open = () => {
    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Explore') as HTMLButtonElement).click();
    fixture.detectChanges();
  };

  const run = () => {
    (el().querySelector('form.explore button[type=submit]') as HTMLButtonElement).click();
  };

  const url = `/api/organizations/${ORG}/datasets/${dataset(null).id}/query`;

  it('runs a grouped count and draws a bar per row', () => {
    load('Viewer', [withDates()]);
    open();
    pick('Group by', 'region');

    run();
    const req = http.expectOne(url);
    expect(req.request.body).toEqual({
      dimensions: [{ column: 'region' }],
      measures: [{ aggregation: 'Count', alias: 'count' }],
      limit: 100,
    });
    req.flush({
      columns: [{ name: 'region', dataType: 'text' }, { name: 'count', dataType: 'bigint' }],
      rows: [['Auckland', 3], ['Wellington', 2]],
      truncated: false,
      sql: null,
    });
    fixture.detectChanges();

    const bars = Array.from(el().querySelectorAll('.bar')).map((b) => parseFloat((b as HTMLElement).style.width));
    expect(bars[0]).toBe(100);
    expect(bars[1]).toBeCloseTo(66.67, 1);
    expect(el().textContent).not.toContain('Generated SQL'); // viewers never receive it
  });

  it('offers a time grain only for date and timestamp columns and sends it', () => {
    load('Editor', [withDates()]);
    open();

    pick('Group by', 'region');
    expect(el().querySelector('select[aria-label="Time grain"]')).toBeNull();

    pick('Group by', 'ordered_on');
    pick('Time grain', 'Month');
    pick('Aggregation', 'Sum');
    pick('Measure column', 'amount');
    run();

    const req = http.expectOne(url);
    expect(req.request.body.dimensions).toEqual([{ column: 'ordered_on', grain: 'Month' }]);
    expect(req.request.body.measures).toEqual([{ aggregation: 'Sum', column: 'amount', alias: 'value' }]);
    req.flush({ columns: [], rows: [], truncated: false, sql: 'SELECT 1' });
    fixture.detectChanges();

    expect(el().textContent).toContain('Generated SQL'); // editors get it
  });

  it('only offers numeric columns for sum and average', () => {
    load('Editor', [withDates()]);
    open();

    pick('Aggregation', 'Sum');
    const options = () =>
      Array.from(el().querySelectorAll('select[aria-label="Measure column"] option')).map((o) => o.textContent?.trim());
    expect(options()).toEqual(['amount']);

    pick('Aggregation', 'CountDistinct');
    expect(options()).toEqual(['region', 'ordered_on', 'amount']);
  });

  it('shows the API message when the query is rejected', () => {
    load('Viewer', [withDates()]);
    open();

    run();
    http.expectOne(url).flush({ errors: { Limit: ['Limit must be between 1 and 1000.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Limit must be between 1 and 1000.');
  });

  // ---- charts and dashboards ----

  const grouped = {
    columns: [{ name: 'region', dataType: 'text' }, { name: 'count', dataType: 'bigint' }],
    rows: [['Auckland', 3], ['Wellington', 2]],
    truncated: false,
    sql: null,
  };

  it('draws a chart instead of bars when a chart type is chosen', () => {
    load('Viewer', [withDates()]);
    open();
    pick('Group by', 'region');
    pick('Chart type', 'Pie');

    run();
    http.expectOne(url).flush(grouped);
    fixture.detectChanges();

    expect(el().querySelector('app-chart')).not.toBeNull();
    expect(el().querySelectorAll('.bar').length).toBe(0);
    expect(el().querySelectorAll('tbody td').length).toBeGreaterThan(0); // the table stays for exact numbers
  });

  it('shows the table with bars by default', () => {
    load('Viewer', [withDates()]);
    open();
    pick('Group by', 'region');

    run();
    http.expectOne(url).flush(grouped);
    fixture.detectChanges();

    expect(el().querySelector('app-chart')).toBeNull();
    expect(el().querySelectorAll('.bar').length).toBe(2);
  });

  it('does not offer adding to a dashboard to viewers', () => {
    load('Viewer', [withDates()]);
    open();
    run();
    http.expectOne(url).flush(grouped);
    fixture.detectChanges();

    expect(Array.from(el().querySelectorAll('button')).some((b) => b.textContent?.trim() === 'Add to dashboard')).toBe(false);
  });

  it('adds the explored chart to a dashboard as a new widget', () => {
    load('Editor', [withDates()]);
    open();
    pick('Group by', 'region');
    pick('Chart type', 'Bar');
    run();
    http.expectOne(url).flush(grouped);
    fixture.detectChanges();

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add to dashboard') as HTMLButtonElement).click();
    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards` }).flush([
      { id: 'd1', name: 'Sales', description: null, widgetCount: 0, updatedUtc: '' },
    ]);
    fixture.detectChanges();

    const title = el().querySelector('input[aria-label="Chart title"]') as HTMLInputElement;
    title.value = 'Orders by region';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form.add-widget button[type=submit]') as HTMLButtonElement).click();

    http.expectOne({ method: 'GET', url: `/api/organizations/${ORG}/dashboards/d1` }).flush({
      id: 'd1', name: 'Sales', description: 'All sales', createdUtc: '', updatedUtc: '',
      definition: { widgets: [], filters: [{ id: 'f1', label: 'Region', column: 'region', operator: 'In' }] },
    });
    const put = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/d1` });
    expect(put.request.body.name).toBe('Sales');
    expect(put.request.body.description).toBe('All sales');
    expect(put.request.body.definition.filters.length).toBe(1); // existing filters are kept
    expect(put.request.body.definition.widgets).toEqual([{
      id: '00000000-0000-0000-0000-000000000000',
      title: 'Orders by region',
      datasetId: dataset(null).id,
      chartType: 'Bar',
      query: { dimensions: [{ column: 'region' }], measures: [{ aggregation: 'Count', alias: 'count' }], limit: 100 },
    }]);
    put.flush({ id: 'd1', name: 'Sales', description: null, createdUtc: '', updatedUtc: '', definition: { widgets: [], filters: [] } });
    fixture.detectChanges();

    expect(el().textContent).toContain('Added to "Sales"');
  });
});
