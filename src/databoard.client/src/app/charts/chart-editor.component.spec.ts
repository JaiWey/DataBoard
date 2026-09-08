import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ECHARTS_LOADER } from './chart.component';
import { ChartEditorComponent } from './chart-editor.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DS = '33333333-3333-3333-3333-333333333333';
const DS2 = '44444444-4444-4444-4444-444444444444';

const datasets = [
  {
    id: DS, dataSourceId: 'src', name: 'Orders', description: null, sql: null, kind: 'Sql', definition: null, createdUtc: '', updatedUtc: '',
    columns: [
      { name: 'region', dataType: 'text', role: 'Dimension' },
      { name: 'price', dataType: 'numeric(10,2)', role: 'Measure' },
      { name: 'ordered_on', dataType: 'date', role: 'Dimension' },
    ],
  },
  {
    id: DS2, dataSourceId: 'src', name: 'Customers', description: null, sql: null, kind: 'Sql', definition: null, createdUtc: '', updatedUtc: '',
    columns: [{ name: 'name', dataType: 'text', role: 'Dimension' }],
  },
];

const result = (sql: string | null = 'SELECT 1') => ({
  columns: [{ name: 'region', dataType: 'text' }, { name: 'sum_price', dataType: 'numeric' }],
  rows: [['Auckland', 2480], ['Wellington', 1400]],
  truncated: false,
  sql,
});

const saved = () => ({
  id: 'c1', name: 'Revenue', description: 'Per region', datasetId: DS, type: 'Line', createdUtc: '', updatedUtc: '',
  definition: {
    style: { legend: false, labels: true, palette: 'warm' },
    query: {
      dimensions: [{ column: 'region', alias: 'region' }],
      measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }],
      orderBy: [{ field: 'sum_price', direction: 'Desc' }],
      limit: 20,
    },
  },
});

describe('ChartEditorComponent', () => {
  let fixture: ComponentFixture<ChartEditorComponent>;
  let http: HttpTestingController;
  let routeParams: Record<string, string>;
  let queryParams: Record<string, string>;
  const el = () => fixture.nativeElement as HTMLElement;
  const queryUrl = (dataset = DS) => `/api/organizations/${ORG}/datasets/${dataset}/query`;
  // The editor's methods are protected for the template; tests drive them the way the template would.
  const editor = () => fixture.componentInstance as unknown as Record<string, (...args: never[]) => unknown>;

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
  const typeInto = (label: string, value: string) => {
    const input = byLabel<HTMLInputElement>(label);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const saveButton = () => Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Save') as HTMLButtonElement;
  const zone = (id: string) => Array.from(el().querySelectorAll(`#zone-${id} .item`));
  /** Each metric as "Aggregation field", e.g. "Sum price". */
  const metrics = () =>
    zone('metrics').map((i) => `${(i.querySelector('select') as HTMLSelectElement).selectedOptions[0].textContent?.trim()} ${i.querySelector('.item-name')?.textContent}`);
  /** Opens a tab of the config panel. Real timers are faked in these tests, so the tab body is given a moment by hand. */
  const openTab = (index: number) => {
    (el().querySelectorAll('[role=tab]')[index] as HTMLElement).click();
    fixture.detectChanges();
    vi.advanceTimersByTime(50);
    fixture.detectChanges();
  };

  /** Opens the editor as an editor/owner would, answers its first requests, and leaves the timers faked. */
  const open = (role: 'Viewer' | 'Editor' | 'Owner' = 'Editor') => {
    fixture = TestBed.createComponent(ChartEditorComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    if (routeParams['chartId'] && routeParams['chartId'] !== 'new') {
      http.expectOne(`/api/organizations/${ORG}/charts/${routeParams['chartId']}`).flush(saved());
    }
    fixture.detectChanges();
  };

  /** Lets the (debounced) preview ask for its data, and answers it. */
  const preview = (body = result(), dataset = DS) => {
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl(dataset) }).flush(body);
    fixture.detectChanges();
  };

  beforeEach(() => {
    vi.useFakeTimers();
    routeParams = { id: ORG, chartId: 'new' };
    queryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get paramMap() { return convertToParamMap(routeParams); },
              get queryParamMap() { return convertToParamMap(queryParams); },
            },
          },
        },
        { provide: ECHARTS_LOADER, useValue: async () => ({ init: () => ({ setOption() {}, resize() {}, dispose() {} }) }) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    // There is no router outlet here; navigation is only observed, never performed.
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  // ---- the fields on the left ----

  it('lists the fields of the first dataset with a badge for text, number and date', () => {
    open();

    const badges = Array.from(el().querySelectorAll('.field .badge')).map((f) => f.textContent?.trim());
    const names = Array.from(el().querySelectorAll('.field .field-name')).map((f) => f.textContent?.trim());
    expect(badges).toEqual(['Str', '123', 'Date']);
    expect(names).toEqual(['region', 'price', 'ordered_on']);
    expect(byLabel<HTMLSelectElement>('Dataset').selectedOptions[0].textContent?.trim()).toBe('Orders');
  });

  it('starts with the dataset named in the address', () => {
    queryParams = { dataset: DS2 };

    open();

    expect(Array.from(el().querySelectorAll('.field-name')).map((f) => f.textContent)).toEqual(['name']);
  });

  it('asks for something to show and keeps Save off until there is a name and something to show', () => {
    open();

    expect(el().textContent).toContain('Drag a field into Dimensions or Metrics.');
    expect(saveButton().disabled).toBe(true);
  });

  // ---- adding fields ----

  it('adds a number to the metrics as a sum and any other field to the dimensions when it is clicked', () => {
    open();

    click('Add price');
    click('Add region');

    expect(metrics()).toEqual(['Sum price']);
    expect(zone('dimensions').map((i) => i.querySelector('.item-name')?.textContent)).toEqual(['region']);
  });

  it('groups a date by month to begin with and lets the grain be changed', () => {
    open();
    click('Add ordered_on');

    const grain = byLabel<HTMLSelectElement>('Group ordered_on by');
    expect(grain.value).toBe('Month');
    choose('Group ordered_on by', 'Year');
    click('Add price');
    preview();
  });

  it('adds a field dragged from the list to the zone it was dropped in, and moves items within a zone', () => {
    open();
    const column = datasets[0].columns[0];
    const outside = {} as never;
    const inside = {} as never;

    editor()['dropped']('dimensions' as never, { previousContainer: outside, container: inside, item: { data: column } } as never);
    editor()['dropped']('dimensions' as never, { previousContainer: outside, container: inside, item: { data: datasets[0].columns[2] } } as never);
    fixture.detectChanges();
    expect(zone('dimensions').map((i) => i.querySelector('.item-name')?.textContent)).toEqual(['region', 'ordered_on']);

    const same = {} as never;
    editor()['dropped']('dimensions' as never, { previousContainer: same, container: same, previousIndex: 0, currentIndex: 1, item: {} } as never);
    fixture.detectChanges();
    expect(zone('dimensions').map((i) => i.querySelector('.item-name')?.textContent)).toEqual(['ordered_on', 'region']);
  });

  it('ignores a drop that carries no field (an item dragged from one zone to another)', () => {
    open();

    editor()['dropped']('metrics' as never, { previousContainer: {}, container: {}, item: { data: undefined } } as never);
    fixture.detectChanges();

    expect(zone('metrics')).toEqual([]);
  });

  it('adds a row count as a metric', () => {
    open();

    (el().querySelector('.add-count') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(zone('metrics')[0].textContent).toContain('rows');
    preview({ ...result(), columns: [{ name: 'count', dataType: 'bigint' }], rows: [[8]] });
  });

  it('removes an item, and a sort that no longer names anything goes with it', () => {
    open();
    click('Add price');
    editor()['patch']({ sortBy: 'sum_price' } as never);

    click('Remove metric 1');

    expect(zone('metrics')).toEqual([]);
    expect((fixture.componentInstance as unknown as { state: () => { sortBy: string } }).state().sortBy).toBe('');
  });

  it('clears what was chosen when another dataset is picked', () => {
    open();
    click('Add price');
    click('Add region');

    choose('Dataset', DS2);

    expect(zone('metrics')).toEqual([]);
    expect(zone('dimensions')).toEqual([]);
    expect(Array.from(el().querySelectorAll('.field-name')).map((f) => f.textContent)).toEqual(['name']);
  });

  // ---- the preview ----

  it('asks for the preview a moment after the last change, once, with the query the settings describe', () => {
    open();
    click('Add region');
    click('Add price');
    vi.advanceTimersByTime(200);
    click('Add region'); // another change inside the delay restarts it

    vi.advanceTimersByTime(299);
    http.expectNone({ method: 'POST', url: queryUrl() });
    vi.advanceTimersByTime(1);
    const req = http.expectOne({ method: 'POST', url: queryUrl() });
    expect(req.request.body).toEqual({
      dimensions: [{ column: 'region', alias: 'region' }, { column: 'region', alias: 'region_2' }],
      measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }],
    });
    req.flush(result());
  });

  it('draws the result as the chosen chart type, and the table and SQL views are one click away', () => {
    open();
    click('Add region');
    click('Add price');
    preview();
    expect(el().querySelector('app-chart')).not.toBeNull();

    (Array.from(el().querySelectorAll('mat-button-toggle button')).find((b) => b.textContent?.trim() === 'Table') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.preview-box tbody td')?.textContent).toBe('Auckland');

    (Array.from(el().querySelectorAll('mat-button-toggle button')).find((b) => b.textContent?.trim() === 'SQL') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('pre.sql')?.textContent).toBe('SELECT 1');
  });

  it('offers no SQL view when the server did not send the SQL (viewers)', () => {
    open();
    click('Add region');
    click('Add price');
    preview(result(null));

    const toggles = Array.from(el().querySelectorAll('mat-button-toggle')).map((b) => b.textContent?.trim());
    expect(toggles).toEqual(['Chart', 'Table']);
  });

  it('changes how the same result is drawn when a chart type is picked, without asking again', () => {
    open();
    click('Add region');
    click('Add price');
    preview();

    click('Donut');

    expect(byLabel('Donut').getAttribute('aria-pressed')).toBe('true');
    expect(byLabel('Bar').getAttribute('aria-pressed')).toBe('false');
    expect(el().querySelector('app-chart')).not.toBeNull();
  });

  it('shows a table or a number without ECharts', () => {
    open();
    click('Add price');
    click('Number');
    preview({ ...result(), columns: [{ name: 'sum_price', dataType: 'numeric' }], rows: [[3880]] });

    expect(el().querySelector('.number .value')?.textContent).toBe((3880).toLocaleString());

    click('Table');
    expect(el().querySelector('.preview-box tbody td')?.textContent).toBe('3880');
  });

  it('says what is wrong for the chosen type instead of asking the server', () => {
    open();
    click('Add region');
    click('Number');

    vi.advanceTimersByTime(500);
    http.expectNone({ method: 'POST', url: queryUrl() });
    expect(el().querySelector('.problems')?.textContent).toContain('A number shows exactly one metric and no dimensions.');
  });

  it('shows why the preview failed', () => {
    open();
    click('Add region');
    click('Add price');
    fixture.detectChanges();
    vi.advanceTimersByTime(300);

    http.expectOne({ method: 'POST', url: queryUrl() })
      .flush({ errors: { Measures: ["Sum cannot be applied to 'region' (text)."] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('cannot be applied');
  });

  // ---- metrics and filters ----

  it('sends the aggregation chosen for a metric', () => {
    open();
    click('Add region');
    click('Add price');
    choose('Aggregation of metric 1', 'Avg');

    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    const req = http.expectOne({ method: 'POST', url: queryUrl() });
    expect(req.request.body.measures).toEqual([{ aggregation: 'Avg', column: 'price', alias: 'avg_price' }]);
    req.flush(result());
  });

  it('only offers counting for a text field and the full set for a number', () => {
    open();
    editor()['addField']('metrics' as never, datasets[0].columns[0] as never);
    editor()['addField']('metrics' as never, datasets[0].columns[1] as never);
    fixture.detectChanges();

    const options = (label: string) => Array.from(byLabel<HTMLSelectElement>(label).options).map((o) => o.value);
    expect(options('Aggregation of metric 1')).toEqual(['Count', 'CountDistinct']);
    expect(options('Aggregation of metric 2')).toEqual(['Sum', 'Avg', 'Min', 'Max', 'Count', 'CountDistinct']);
  });

  it('sends a filter once it has a value, and hides the value for an empty check', () => {
    open();
    click('Add price');
    editor()['addField']('filters' as never, datasets[0].columns[0] as never);
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result()); // a filter without a value is not sent yet

    choose('Condition of filter 1', 'In');
    typeInto('Value of filter 1', 'Auckland, Dunedin');
    vi.advanceTimersByTime(300);
    const req = http.expectOne({ method: 'POST', url: queryUrl() });
    expect(req.request.body.filters).toEqual([{ column: 'region', operator: 'In', values: ['Auckland', 'Dunedin'] }]);
    req.flush(result());

    choose('Condition of filter 1', 'IsNull');
    expect(el().querySelector('[aria-label="Value of filter 1"]')).toBeNull();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());
  });

  // ---- style and settings ----

  it('sends the row limit and the sort from the Settings tab', () => {
    open();
    click('Add region');
    click('Add price');
    preview();

    openTab(2);
    typeInto('Row limit', '25');
    choose('Sort by', 'sum_price');
    choose('Sort direction', 'Asc');

    vi.advanceTimersByTime(300);
    const req = http.expectOne({ method: 'POST', url: queryUrl() });
    expect(req.request.body.limit).toBe(25);
    expect(req.request.body.orderBy).toEqual([{ field: 'sum_price', direction: 'Asc' }]);
    req.flush(result());
  });

  it('lists the dimensions and metrics as the things a sort can use', () => {
    open();
    click('Add region');
    click('Add price');
    preview();

    openTab(2);

    const options = Array.from(byLabel<HTMLSelectElement>('Sort by').options).map((o) => o.value);
    expect(options).toEqual(['', 'region', 'sum_price']);
  });

  it('refuses a row limit the server would reject', () => {
    open();
    click('Add price');
    preview({ ...result(), columns: [{ name: 'sum_price', dataType: 'numeric' }], rows: [[1]] });

    openTab(2);
    typeInto('Row limit', '5000');

    expect(el().querySelector('.problems')?.textContent).toContain('whole number from 1 to 1000');
  });

  it('keeps the style choices in what is saved', () => {
    open();
    click('Add region');
    click('Add price');
    typeInto('Chart name', 'Revenue');
    editor()['patchStyle']({ legend: false, labels: true, palette: 'cool' } as never);
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());

    saveButton().click();

    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts` });
    expect(req.request.body.definition.style).toEqual({ legend: false, labels: true, palette: 'cool' });
    req.flush({ ...saved(), id: 'new-id' });
  });

  // ---- saving ----

  it('saves a new chart, then names it in the address so the next save updates it', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    open();
    click('Add region');
    click('Add price');
    typeInto('Chart name', '  Revenue by region  ');
    editor()['patch']({ description: 'Money per region' } as never);
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());
    expect(saveButton().disabled).toBe(false);

    saveButton().click();
    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts` });
    expect(req.request.body).toEqual({
      name: 'Revenue by region',
      description: 'Money per region',
      datasetId: DS,
      type: 'Bar',
      definition: {
        query: {
          dimensions: [{ column: 'region', alias: 'region' }],
          measures: [{ aggregation: 'Sum', column: 'price', alias: 'sum_price' }],
        },
        style: { legend: true, labels: false, palette: 'default' },
      },
    });
    req.flush({ ...saved(), id: 'c9' });
    fixture.detectChanges();

    expect(el().textContent).toContain('Saved');
    expect(navigate).toHaveBeenCalledWith(['/organizations', ORG, 'charts', 'c9'], { replaceUrl: true });

    typeInto('Chart name', 'Revenue v2');
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());
    saveButton().click();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/charts/c9` }).flush(saved());
  });

  it('opens a saved chart as it was made, and saving changes it in place', () => {
    routeParams = { id: ORG, chartId: 'c1' };
    open();

    expect((byLabel<HTMLInputElement>('Chart name')).value).toBe('Revenue');
    expect(byLabel('Line').getAttribute('aria-pressed')).toBe('true');
    expect(zone('dimensions').map((i) => i.querySelector('.item-name')?.textContent)).toEqual(['region']);
    expect(metrics()).toEqual(['Sum price']);
    expect(el().textContent).not.toContain('Unsaved changes');
    preview();

    typeInto('Chart name', 'Revenue (2026)');
    expect(el().textContent).toContain('Unsaved changes');
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());

    saveButton().click();
    const req = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/charts/c1` });
    expect(req.request.body.name).toBe('Revenue (2026)');
    expect(req.request.body.definition.query.limit).toBe(20);
    expect(req.request.body.definition.style).toEqual({ legend: false, labels: true, palette: 'warm' });
    req.flush(saved());
    fixture.detectChanges();

    expect(el().textContent).not.toContain('Unsaved changes');
  });

  it('keeps the work and shows the message when the name is taken', () => {
    open();
    click('Add region');
    click('Add price');
    typeInto('Chart name', 'Revenue');
    fixture.detectChanges();
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result());

    saveButton().click();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/charts` })
      .flush({ title: 'Conflict', detail: 'A chart with that name already exists.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el().querySelector('.notice[role=alert]')?.textContent).toContain('already exists');
    expect(saveButton().disabled).toBe(false);
    expect(zone('metrics').length).toBe(1);
  });

  it('lets a viewer look but not save', () => {
    open('Viewer');
    click('Add region');
    click('Add price');
    typeInto('Chart name', 'Revenue');

    expect(el().querySelector('.notice')?.textContent).toContain('needs Editor access');
    expect(saveButton().disabled).toBe(true);
    expect(byLabel<HTMLSelectElement>('Dataset').disabled).toBe(true);
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result(null));
  });

  // ---- leaving ----

  it('goes back to the Charts list, asking first when there are unsaved changes', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    open();
    click('Add price');
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);
    vi.advanceTimersByTime(300);
    http.expectOne({ method: 'POST', url: queryUrl() }).flush(result(null));

    click('Back to dashboards');
    expect(navigate).not.toHaveBeenCalled();

    confirmSpy.mockReturnValue(true);
    click('Back to dashboards');
    expect(navigate).toHaveBeenCalledWith(['/organizations', ORG, 'dashboards'], { queryParams: { view: 'charts' } });
  });

  it('goes back without asking when nothing changed, and selects the chart it was editing', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    routeParams = { id: ORG, chartId: 'c1' };
    open();
    preview();
    const confirmSpy = vi.spyOn(window, 'confirm');

    click('Back to dashboards');

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/organizations', ORG, 'dashboards'], { queryParams: { view: 'charts', chart: 'c1' } });
  });

  it('says so when the chart cannot be loaded', () => {
    routeParams = { id: ORG, chartId: 'missing' };
    fixture = TestBed.createComponent(ChartEditorComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role: 'Editor' }]);
    http.expectOne(`/api/organizations/${ORG}/datasets`).flush(datasets);
    http.expectOne(`/api/organizations/${ORG}/charts/missing`)
      .flush({ title: 'Not found', detail: 'Chart not found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('.notice[role=alert]')?.textContent).toContain('Chart not found.');
  });
});
