import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ECHARTS_LOADER } from '../charts/chart.component';
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
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

  it('switches the details when another dataset is clicked and clears what was open', () => {
    const second = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    load('Viewer', [dataset(null), second]);
    (Array.from(el().querySelectorAll('.detail button')).find((b) => b.textContent?.trim() === 'Explore') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('form.explore')).not.toBeNull();

    (el().querySelectorAll('button.item')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(el().querySelector('.detail mat-card-title')?.textContent).toContain('Customers');
    expect(el().querySelector('form.explore')).toBeNull();
  });

  it('opens the add dialog and selects the dataset it created', () => {
    load('Owner', [dataset(null)], false);
    const created = { ...dataset(null), id: '55555555-5555-5555-5555-555555555555', name: 'Customers' };
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(created) } as never);

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Add dataset') as HTMLButtonElement).click();

    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG });
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush([dataset(null), created]);
    fixture.detectChanges();

    expect(el().querySelector('.detail mat-card-title')?.textContent).toContain('Customers');
  });

  it('shows columns as dimensions or measures', () => {
    load('Viewer', [dataset(null)]);

    const chips = Array.from(el().querySelectorAll('.chip')).map((c) => c.textContent?.replace(/\s+/g, ' ').trim());
    expect(chips).toEqual(['region dimension', 'amount measure']);
    expect(el().querySelectorAll('.chip.measure').length).toBe(1);
  });

  it('hides the SQL and the delete button from viewers', () => {
    load('Viewer', [dataset(null)]);

    expect(el().querySelector('.sql')).toBeNull();
    expect(buttonText()).toEqual(['Preview', 'Explore']);
  });

  it('shows the SQL and delete to editors', () => {
    load('Editor', [dataset('SELECT region, amount FROM sales')]);

    expect(el().querySelector('.sql')?.textContent).toContain('SELECT region, amount FROM sales');
    expect(buttonText()).toEqual(['Preview', 'Explore', 'Delete']);
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
