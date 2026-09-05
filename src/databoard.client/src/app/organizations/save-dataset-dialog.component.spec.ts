import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { SaveDatasetDialogComponent } from './save-dataset-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';

describe('SaveDatasetDialogComponent', () => {
  let fixture: ComponentFixture<SaveDatasetDialogComponent>;
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
  const submit = () => (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, dataSourceId: 'src-1', sql: 'SELECT region, amount FROM sales' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(SaveDatasetDialogComponent);
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('cannot be saved until a name is given', () => {
    expect(button('Save dataset').disabled).toBe(true);

    type('input[aria-label="Dataset name"]', 'Regional sales');

    expect(button('Save dataset').disabled).toBe(false);
  });

  it('creates the dataset from the tab\'s data source and SQL, and closes with it', () => {
    type('input[aria-label="Dataset name"]', 'Regional sales');
    type('input[aria-label="Description"]', 'Sales per region');

    submit();

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

  it('stays open and shows the message when the SQL or name is rejected', () => {
    type('input[aria-label="Dataset name"]', 'Bad');

    submit();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/datasets` })
      .flush({ errors: { Sql: ['Only SELECT queries are allowed.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')?.textContent).toContain('Only SELECT queries are allowed.');
    expect(button('Save dataset').disabled).toBe(false);
  });
});
