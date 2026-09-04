import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AddDatasetDialogComponent } from './add-dataset-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const SOURCES = [
  { id: 'src-1', organizationId: ORG, name: 'Shop DB', type: 'PostgreSql', createdUtc: '' },
  { id: 'src-2', organizationId: ORG, name: 'Warehouse', type: 'PostgreSql', createdUtc: '' },
];

describe('AddDatasetDialogComponent', () => {
  let fixture: ComponentFixture<AddDatasetDialogComponent>;
  let http: HttpTestingController;
  const close = vi.fn();

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

  const type = (selector: string, value: string) => {
    const input = el().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };

  const start = (sources: unknown[] = SOURCES) => {
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/data-sources`).flush(sources);
    fixture.detectChanges();
  };

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(AddDatasetDialogComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('offers the organization data sources and picks the first', () => {
    start();

    const options = Array.from(el().querySelectorAll('select[aria-label="Data source"] option')).map((o) => o.textContent?.trim());
    expect(options).toEqual(['Shop DB', 'Warehouse']);
    expect((el().querySelector('select[aria-label="Data source"]') as HTMLSelectElement).value).toBe('src-1');
  });

  it('says a data source is needed first when there are none', () => {
    start([]);

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Add a data source first');
    expect(button('Save dataset').disabled).toBe(true);
  });

  it('cannot be saved until a name and SQL are given', () => {
    start();
    expect(button('Save dataset').disabled).toBe(true);

    type('input[aria-label="Dataset name"]', 'Regional sales');
    expect(button('Save dataset').disabled).toBe(true);

    type('textarea', 'SELECT region, amount FROM sales');
    expect(button('Save dataset').disabled).toBe(false);
  });

  it('creates the dataset and closes with it', () => {
    start();
    type('input[aria-label="Dataset name"]', 'Regional sales');
    type('input[aria-label="Description"]', 'Sales per region');
    type('textarea', 'SELECT region, amount FROM sales');

    (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/datasets` });
    expect(req.request.body).toEqual({
      dataSourceId: 'src-1',
      name: 'Regional sales',
      description: 'Sales per region',
      sql: 'SELECT region, amount FROM sales',
    });
    const created = { id: 'ds-1', name: 'Regional sales', columns: [] };
    req.flush(created);

    expect(close).toHaveBeenCalledWith(created);
  });

  it('stays open and shows the validation message when the SQL is rejected', () => {
    start();
    type('input[aria-label="Dataset name"]', 'Bad');
    type('textarea', 'DELETE FROM sales');

    (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/datasets` })
      .flush({ errors: { Sql: ['Only SELECT queries are allowed.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')?.textContent).toContain('Only SELECT queries are allowed.');
    expect(button('Save dataset').disabled).toBe(false); // the user can fix the query and try again
  });
});
