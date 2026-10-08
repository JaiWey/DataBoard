import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { chooseOption } from '../shared/select.testing';
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
        { provide: MatDialogRef, useValue: { close, disableClose: false } },
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

  describe('a MySQL database', () => {
    const chooseMySql = () => chooseOption(fixture, 'Type', 'mysql');
    const mysqlString = 'Server=db.example.com;Port=3306;Database=wh;User ID=reader;Password=pw;SSL Mode=Preferred';

    it('uses the same fields and switches the port to the one MySQL listens on', () => {
      chooseMySql();

      expect((el().querySelector('input[formcontrolname=port]') as HTMLInputElement).value).toBe('3306');
      expect(el().querySelector('input[formcontrolname=host]')).not.toBeNull();
      expect(el().querySelector('input[type=file]')).toBeNull();
    });

    it('keeps a port the person typed', () => {
      set('input[formcontrolname=port]', '3307');
      fixture.detectChanges();

      chooseMySql();

      expect((el().querySelector('input[formcontrolname=port]') as HTMLInputElement).value).toBe('3307');
    });

    it('tests with a MySQL connection string and says which database answered', () => {
      chooseMySql();
      fill();

      button('Test connection').click();

      const req = http.expectOne(`/api/organizations/${ORG}/data-sources/test`);
      expect(req.request.body).toEqual({ type: 'MySql', connectionString: mysqlString });
      req.flush({ success: true, message: 'Connection successful.', serverVersion: '8.4.0', latencyMs: 7 });
      fixture.detectChanges();

      expect(el().textContent).toContain('MySQL 8.4.0');
    });

    it('saves it as a MySQL data source', () => {
      chooseMySql();
      fill();

      (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources` });
      expect(req.request.body).toEqual({ name: 'Warehouse', type: 'MySql', connectionString: mysqlString });
      const created = { id: 'x', organizationId: ORG, name: 'Warehouse', type: 'MySql', createdUtc: '' };
      req.flush(created);

      expect(close).toHaveBeenCalledWith(created);
    });
  });

  describe('a SQL Server database', () => {
    const chooseSqlServer = () => chooseOption(fixture, 'Type', 'mssql');
    // "Prefer" encrypts without insisting on a certificate the server may not have
    const mssqlString = 'Server=db.example.com,1433;Database=wh;User ID=reader;Password=pw;Encrypt=True;Trust Server Certificate=True';

    it('switches the port to the one SQL Server listens on', () => {
      chooseSqlServer();

      expect((el().querySelector('input[formcontrolname=port]') as HTMLInputElement).value).toBe('1433');
    });

    it('moves the port back and forth between databases but keeps one that was typed', () => {
      chooseSqlServer();
      chooseOption(fixture, 'Type', 'mysql');
      expect((el().querySelector('input[formcontrolname=port]') as HTMLInputElement).value).toBe('3306');

      set('input[formcontrolname=port]', '14330');
      fixture.detectChanges();
      chooseSqlServer();

      expect((el().querySelector('input[formcontrolname=port]') as HTMLInputElement).value).toBe('14330');
    });

    it('tests with a SQL Server connection string and names the product', () => {
      chooseSqlServer();
      fill();

      button('Test connection').click();

      const req = http.expectOne(`/api/organizations/${ORG}/data-sources/test`);
      expect(req.request.body).toEqual({ type: 'SqlServer', connectionString: mssqlString });
      req.flush({ success: true, message: 'Connection successful.', serverVersion: '16.00.4175', latencyMs: 9 });
      fixture.detectChanges();

      expect(el().textContent).toContain('SQL Server 16.00.4175');
    });

    it('saves it as a SQL Server data source', () => {
      chooseSqlServer();
      fill();

      (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources` });
      expect(req.request.body).toEqual({ name: 'Warehouse', type: 'SqlServer', connectionString: mssqlString });
      req.flush({ id: 'x', organizationId: ORG, name: 'Warehouse', type: 'SqlServer', createdUtc: '' });
    });
  });

  describe('a CSV file', () => {
    const csvFile = () => new File(['a,b\n1,2\n'], 'Q1 sales.csv', { type: 'text/csv' });

    const chooseType = (value: string) => chooseOption(fixture, 'Type', value);

    const pick = (file: File) => {
      const input = el().querySelector('input[type=file]') as HTMLInputElement;
      Object.defineProperty(input, 'files', { value: { item: () => file, length: 1 }, configurable: true });
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    const result = {
      dataSource: { id: 'x', organizationId: ORG, name: 'Q1 sales', type: 'Csv', createdUtc: '' },
      tableName: 'q1_sales',
      rowCount: 1,
      columns: [{ name: 'a', dataType: 'bigint' }, { name: 'b', dataType: 'bigint' }],
    };

    it('offers the type and swaps the form for a file form', () => {
      expect(el().querySelector('input[type=file]')).toBeNull();

      chooseType('csv');

      expect(el().querySelector('input[type=file]')).not.toBeNull();
      expect(el().querySelector('input[formcontrolname=host]')).toBeNull();
      expect(button('Test connection')).toBeUndefined(); // there is nothing to connect to
    });

    it('cannot upload until there is a file, and takes the name from the file', () => {
      chooseType('csv');
      expect(button('Upload').disabled).toBe(true);

      pick(csvFile());

      expect((el().querySelector('input[formcontrolname=name]') as HTMLInputElement).value).toBe('Q1 sales');
      expect(button('Upload').disabled).toBe(false);
    });

    it('keeps a name that was typed', () => {
      chooseType('csv');
      set('input[formcontrolname=name]', 'Mine');

      pick(csvFile());

      expect((el().querySelector('input[formcontrolname=name]') as HTMLInputElement).value).toBe('Mine');
    });

    it('uploads the file with its options, shows what was read, and closes with the data source on Done', () => {
      chooseType('csv');
      pick(csvFile());
      set('input[formcontrolname=tableName]', ' sales ');
      chooseOption(fixture, 'Delimiter', 'tab');

      (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources/csv` });
      const body = req.request.body as FormData;
      expect(body.get('Name')).toBe('Q1 sales');
      expect((body.get('File') as File).name).toBe('Q1 sales.csv');
      expect(body.get('TableName')).toBe('sales');
      expect(body.get('Delimiter')).toBe('tab');
      expect(body.has('HasHeader')).toBe(false); // the first row is the header unless said otherwise
      req.flush(result);
      fixture.detectChanges();

      expect(close).not.toHaveBeenCalled();
      expect(el().textContent).toContain('Read 1 row into the table q1_sales');
      expect(el().textContent).toContain('SELECT * FROM q1_sales');
      expect(el().querySelectorAll('table.columns tbody tr').length).toBe(2);

      button('Done').click();
      expect(close).toHaveBeenCalledWith(result.dataSource);
    });

    it('says when the first row is not a header', () => {
      chooseType('csv');
      pick(csvFile());
      (el().querySelector('mat-checkbox input') as HTMLInputElement).click();
      fixture.detectChanges();

      (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

      const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/data-sources/csv` });
      expect((req.request.body as FormData).get('HasHeader')).toBe('false');
      req.flush(result);
    });

    it('shows why a file was refused and lets the person choose another', () => {
      chooseType('csv');
      pick(csvFile());

      (el().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      http.expectOne(`/api/organizations/${ORG}/data-sources/csv`)
        .flush({ errors: { File: ['Row 3 has 3 values but there are 2 columns.'] } }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('Row 3 has 3 values');
      expect(close).not.toHaveBeenCalled();
      expect(button('Upload').disabled).toBe(false);
    });
  });
});
