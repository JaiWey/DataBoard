import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AddDataSourceDialogComponent } from './add-data-source-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';

describe('AddDataSourceDialogComponent', () => {
  let fixture: ComponentFixture<AddDataSourceDialogComponent>;
  let http: HttpTestingController;
  const close = vi.fn();

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

  const set = (selector: string, value: string) => {
    const input = el().querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const fill = () => {
    set('input[formcontrolname=name]', 'Warehouse');
    set('input[formcontrolname=host]', 'db.example.com');
    set('input[formcontrolname=database]', 'wh');
    set('input[formcontrolname=username]', 'reader');
    set('input[formcontrolname=password]', 'pw');
    fixture.detectChanges();
  };

  const connectionString = 'Host=db.example.com;Port=5432;Database=wh;Username=reader;Password=pw;SSL Mode=Prefer';

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
    fixture = TestBed.createComponent(AddDataSourceDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('cannot test or save until the required fields are filled', () => {
    expect(button('Test connection').disabled).toBe(true);
    expect(button('Save').disabled).toBe(true);

    fill();

    expect(button('Test connection').disabled).toBe(false);
    expect(button('Save').disabled).toBe(false);
  });

  it('sends a connection string built from the form when testing', () => {
    fill();

    button('Test connection').click();

    const req = http.expectOne(`/api/organizations/${ORG}/data-sources/test`);
    expect(req.request.body).toEqual({ type: 'PostgreSql', connectionString });
    req.flush({ success: true, message: 'Connection successful.', serverVersion: '17.2', latencyMs: 12 });
    fixture.detectChanges();

    expect(el().textContent).toContain('Connection successful.');
    expect(el().textContent).toContain('PostgreSQL 17.2');
    expect(close).not.toHaveBeenCalled(); // testing leaves the dialog open
  });

  it('saves the data source and closes with it', () => {
    fill();

    (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources` });
    expect(req.request.body).toEqual({ name: 'Warehouse', type: 'PostgreSql', connectionString });
    const created = { id: 'x', organizationId: ORG, name: 'Warehouse', type: 'PostgreSql', createdUtc: '' };
    req.flush(created);

    expect(close).toHaveBeenCalledWith(created);
  });

  it('stays open and shows the message when saving fails', () => {
    fill();

    (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources` })
      .flush({ title: 'Conflict', detail: 'A data source with that name already exists.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')?.textContent).toContain('already exists');
    expect(button('Save').disabled).toBe(false); // the user can fix the name and try again
  });
});
