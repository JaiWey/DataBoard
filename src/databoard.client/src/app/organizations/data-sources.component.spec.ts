import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DataSourcesComponent } from './data-sources.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const SOURCE = {
  id: '22222222-2222-2222-2222-222222222222',
  organizationId: ORG,
  name: 'Sales',
  type: 'PostgreSql',
  createdUtc: '2026-10-07T00:00:00Z',
};
const OTHER = { ...SOURCE, id: '33333333-3333-3333-3333-333333333333', name: 'Warehouse' };

describe('DataSourcesComponent', () => {
  let fixture: ComponentFixture<DataSourcesComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(el().querySelectorAll('button')).map((b) => b.textContent?.trim());
  const button = (text: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;
  const listItems = () => Array.from(el().querySelectorAll('button.item')) as HTMLButtonElement[];

  const load = (role: 'Viewer' | 'Editor' | 'Owner', items: unknown[] = [SOURCE]) => {
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush(items);
    fixture.detectChanges();
  };

  const select = (index = 0) => {
    listItems()[index].click();
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

  it('lists the data sources on the left and asks for a selection on the right', () => {
    load('Viewer', [SOURCE, OTHER]);

    expect(listItems().map((b) => b.querySelector('[matListItemTitle]')?.textContent?.trim())).toEqual(['Sales', 'Warehouse']);
    expect(el().querySelector('.detail')?.textContent).toContain('Select a data source to see its details.');
  });

  it('shows the details of the clicked data source and switches between sources', () => {
    load('Viewer', [SOURCE, OTHER]);

    select(0);
    expect(el().querySelector('.detail')?.textContent).toContain('Sales');
    expect(el().querySelector('.detail')?.textContent).toContain('PostgreSql');
    expect(el().querySelector('.detail')?.textContent).not.toContain('Select a data source');

    select(1);
    expect(el().querySelector('.detail mat-card-title')?.textContent).toContain('Warehouse');
  });

  it('lets viewers browse the schema but hides testing, querying and adding', () => {
    load('Viewer');
    select();

    expect(buttons()).toEqual(['SalesPostgreSql', 'Schema']);
    expect(buttons()).not.toContain('Add data source');
  });

  it('shows editors the add, query and test controls', () => {
    load('Editor');
    select();

    expect(buttons()).toEqual(expect.arrayContaining(['Add data source', 'Schema', 'Query', 'Test']));
  });

  it('loads and renders the schema tree when Schema is clicked', () => {
    load('Viewer');
    select();

    button('Schema').click();
    http.expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/schema`).flush({
      schemas: [{ name: 'public', tables: [{ name: 'sales', kind: 'Table', columns: [{ name: 'id', dataType: 'integer', nullable: false }] }] }],
      truncated: false,
    });
    fixture.detectChanges();

    expect(el().querySelector('.detail')?.textContent).toContain('sales');
    expect(el().querySelector('.detail')?.textContent).toContain('integer');
  });

  it('clears what was open when another data source is selected', () => {
    load('Viewer', [SOURCE, OTHER]);
    select(0);
    button('Schema').click();
    http.expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/schema`).flush({
      schemas: [{ name: 'public', tables: [] }],
      truncated: false,
    });
    fixture.detectChanges();

    select(1);

    expect(el().querySelector('details')).toBeNull();
    expect(el().querySelector('.detail')?.textContent).toContain('Choose Schema');
  });

  it('shows the API validation message when a query is rejected', () => {
    load('Editor');
    select();
    button('Query').click();
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

  it('shows the result of testing a saved connection', () => {
    load('Editor');
    select();

    button('Test').click();
    http.expectOne(`/api/organizations/${ORG}/data-sources/${SOURCE.id}/test`)
      .flush({ success: true, message: 'Connection successful.', serverVersion: '17.2', latencyMs: 5 });
    fixture.detectChanges();

    expect(el().querySelector('[role=status]')?.textContent).toContain('Connection successful.');
  });

  it('opens the add dialog and selects the data source it created', () => {
    load('Owner');
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(OTHER) } as never);

    button('Add data source').click();

    expect(open).toHaveBeenCalled();
    expect((open.mock.calls[0][1] as { data: unknown }).data).toEqual({ orgId: ORG });
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush([SOURCE, OTHER]);
    fixture.detectChanges();

    expect(el().querySelector('.detail mat-card-title')?.textContent).toContain('Warehouse');
  });

  it('does nothing when the add dialog is cancelled', () => {
    load('Owner');
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of(undefined) } as never);

    button('Add data source').click();

    http.expectNone(`/api/organizations/${ORG}/data-sources`);
  });

  it('saves the current query as a dataset once it has run', () => {
    load('Editor');
    select();
    button('Query').click();
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
