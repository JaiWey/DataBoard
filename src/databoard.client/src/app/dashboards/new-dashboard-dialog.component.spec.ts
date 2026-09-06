import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NewDashboardDialogComponent } from './new-dashboard-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';

describe('NewDashboardDialogComponent', () => {
  let fixture: ComponentFixture<NewDashboardDialogComponent>;
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
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    fixture = TestBed.createComponent(NewDashboardDialogComponent);
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('cannot be created until a name is given', () => {
    expect(button('Create dashboard').disabled).toBe(true);

    type('input[aria-label="Dashboard name"]', 'Sales');

    expect(button('Create dashboard').disabled).toBe(false);
  });

  it('creates an empty dashboard and closes with it', () => {
    type('input[aria-label="Dashboard name"]', 'Sales');
    type('input[aria-label="Description"]', 'Weekly numbers');

    submit();

    const req = http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dashboards` });
    expect(req.request.body).toEqual({ name: 'Sales', description: 'Weekly numbers', definition: { widgets: [], filters: [] } });
    const created = { id: 'd1', name: 'Sales' };
    req.flush(created);

    expect(close).toHaveBeenCalledWith(created);
  });

  it('stays open and shows the message when the name is taken', () => {
    type('input[aria-label="Dashboard name"]', 'Sales');

    submit();
    http.expectOne({ method: 'POST', url: `/api/organizations/${ORG}/dashboards` })
      .flush({ title: 'Conflict', detail: 'A dashboard with that name already exists.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el().querySelector('[role=alert]')?.textContent).toContain('already exists');
    expect(button('Create dashboard').disabled).toBe(false);
  });
});
