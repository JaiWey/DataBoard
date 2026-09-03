import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { DataSourcesComponent } from './data-sources.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const SOURCE = {
  id: '22222222-2222-2222-2222-222222222222',
  organizationId: ORG,
  name: 'Sales',
  type: 'PostgreSql',
  createdUtc: '2026-10-07T00:00:00Z',
};

describe('DataSourcesComponent', () => {
  let fixture: ComponentFixture<DataSourcesComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());

  const load = (role: 'Viewer' | 'Editor' | 'Owner') => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush([SOURCE]);
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
    fixture = TestBed.createComponent(DataSourcesComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lets viewers browse the schema but hides testing, querying and adding', () => {
    load('Viewer');

    expect(buttons()).toEqual(['Schema']);
    expect(el().textContent).not.toContain('Add a PostgreSQL data source');
  });

  it('shows editors the query, test and add controls', () => {
    load('Editor');

    expect(buttons()).toEqual(expect.arrayContaining(['Schema', 'Query', 'Test', 'Test connection', 'Save']));
  });

  it('loads and renders the schema tree when Schema is clicked', () => {
    load('Viewer');

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Schema') as HTMLButtonElement).click();
    http.expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/schema`).flush({
      schemas: [{ name: 'public', tables: [{ name: 'sales', kind: 'Table', columns: [{ name: 'id', dataType: 'integer', nullable: false }] }] }],
      truncated: false,
    });
    fixture.detectChanges();

    expect(el().textContent).toContain('sales');
    expect(el().textContent).toContain('integer');
  });

  it('shows the API validation message when a query is rejected', () => {
    load('Editor');
    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Query') as HTMLButtonElement).click();
    fixture.detectChanges();

    const textarea = el().querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'DELETE FROM sales';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();

    http
      .expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/preview`)
      .flush({ errors: { Sql: ['Only SELECT queries are allowed.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Only SELECT queries are allowed.');
  });

  it('sends a connection string built from the form when testing a new connection', () => {
    load('Owner');
    const set = (selector: string, value: string) => {
      const input = el().querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('input[formcontrolname=name]', 'Warehouse');
    set('input[formcontrolname=host]', 'db.example.com');
    set('input[formcontrolname=database]', 'wh');
    set('input[formcontrolname=username]', 'reader');
    set('input[formcontrolname=password]', 'pw');
    fixture.detectChanges();

    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Test connection') as HTMLButtonElement).click();

    const req = http.expectOne(`/api/organizations/${ORG}/data-sources/test`);
    expect(req.request.body.type).toBe('PostgreSql');
    expect(req.request.body.connectionString).toBe(
      'Host=db.example.com;Port=5432;Database=wh;Username=reader;Password=pw;SSL Mode=Prefer',
    );
    req.flush({ success: true, message: 'Connection successful.', serverVersion: '17.2', latencyMs: 12 });
    fixture.detectChanges();

    expect(el().textContent).toContain('Connection successful.');
  });

  it('saves the current query as a dataset once it has run', () => {
    load('Editor');
    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent === 'Query') as HTMLButtonElement).click();
    fixture.detectChanges();

    const sql = el().querySelector('textarea') as HTMLTextAreaElement;
    sql.value = 'SELECT region, amount FROM sales';
    sql.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form button[type=submit]') as HTMLButtonElement).click();
    http.expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/preview`).flush({
      columns: [{ name: 'region', dataType: 'text' }],
      rows: [['Auckland']],
      truncated: false,
    });
    fixture.detectChanges();

    const name = el().querySelector('input[aria-label="Dataset name"]') as HTMLInputElement;
    name.value = 'Regional sales';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelector('form.save button') as HTMLButtonElement).click();

    const req = http.expectOne(`/api/organizations/${ORG}/datasets`);
    expect(req.request.body).toEqual({
      dataSourceId: SOURCE.id,
      name: 'Regional sales',
      description: null,
      sql: 'SELECT region, amount FROM sales',
    });
    req.flush({ id: 'x', name: 'Regional sales', columns: [] });
    fixture.detectChanges();

    expect(el().textContent).toContain('Saved dataset "Regional sales"');
  });
});
