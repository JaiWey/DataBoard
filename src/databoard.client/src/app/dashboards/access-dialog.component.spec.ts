import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { chooseOption, selectHost, selectOptions, selectedValue } from '../shared/select.testing';
import { AccessDialogComponent, AccessDialogData } from './access-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const ID = '44444444-4444-4444-4444-444444444444';
const OWNER = 'a0000000-0000-0000-0000-000000000001';
const EDITOR = 'a0000000-0000-0000-0000-000000000002';
const VIEWER = 'a0000000-0000-0000-0000-000000000003';
const OTHER = 'a0000000-0000-0000-0000-000000000004';

const member = (userId: string, displayName: string, role: string) =>
  ({ userId, displayName, email: `${displayName.toLowerCase()}@example.com`, role, joinedUtc: '2026-10-01T00:00:00Z' });
const people = [member(OWNER, 'Olga', 'Owner'), member(EDITOR, 'Ed', 'Editor'), member(VIEWER, 'Vic', 'Viewer'), member(OTHER, 'Wes', 'Viewer')];

describe('AccessDialogComponent', () => {
  let fixture: ComponentFixture<AccessDialogComponent>;
  let http: HttpTestingController;
  const close = vi.fn();

  const el = () => fixture.nativeElement as HTMLElement;
  const radio = (label: string) => el().querySelector(`input[aria-label="${label}"]`) as HTMLInputElement;
  const person = (name: string) => el().querySelector(`input[aria-label="Allow ${name}"]`) as HTMLInputElement;
  const role = () => selectHost(fixture, 'Minimum role');
  const save = () => el().querySelector('button.save') as HTMLButtonElement;

  const open = (current: object, data: Partial<AccessDialogData> = {}) => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, kind: 'dashboard', id: ID, name: 'Sales', ...data } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AccessDialogComponent);
    fixture.detectChanges();
    http.expectOne(`/api/organizations/${ORG}/members`).flush(people);
    const kind = (data.kind ?? 'dashboard') === 'dashboard' ? 'dashboards' : 'folders';
    http.expectOne(`/api/organizations/${ORG}/${kind}/${ID}/access`).flush({ minRole: null, memberIds: [], inherited: [], ...current });
    fixture.detectChanges();
  };

  beforeEach(() => close.mockReset());
  afterEach(() => http.verify());

  const click = (input: HTMLInputElement) => {
    input.click();
    fixture.detectChanges();
  };

  it('starts on everyone when nothing is limited, with nothing else to choose', () => {
    open({});

    expect(el().querySelector('h2')?.textContent).toContain('Sales');
    expect(radio('Everyone in the organization').checked).toBe(true);
    expect(role()).toBeNull();
    expect(el().querySelectorAll('.people li').length).toBe(0);
  });

  it('shows what is set: the role, the listed people, and Owners ticked as always allowed', () => {
    open({ minRole: 'Editor', memberIds: [VIEWER] });

    expect(radio('Only some people').checked).toBe(true);
    expect(selectedValue(fixture, 'Minimum role')).toBe('Editor');
    expect(selectOptions(fixture, 'Minimum role').map(([, text]) => text)).toEqual(['Nobody by role', 'Editors and Owners', 'Owners only']);
    expect(person('Vic').checked).toBe(true);
    expect(person('Wes').checked).toBe(false);
    expect(person('Olga').checked).toBe(true);
    expect(person('Olga').disabled).toBe(true);
    expect(el().querySelector('.people')!.textContent).toContain('always can');
    expect(el().querySelector('.people')!.textContent).toContain('by role'); // the editor is let in by the role
  });

  it('saves the role and the people that were chosen, without the owners who need no listing', () => {
    open({});
    click(radio('Only some people'));
    chooseOption(fixture, 'Minimum role', 'Editor');
    click(person('Wes'));
    click(person('Vic'));

    save().click();

    const put = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${ID}/access` });
    expect(put.request.body.minRole).toBe('Editor');
    expect([...put.request.body.memberIds].sort()).toEqual([OTHER, VIEWER].sort());
    put.flush({ minRole: 'Editor', memberIds: [], inherited: [] });
    expect(close).toHaveBeenCalledWith(true);
  });

  it('saves "everyone" as no role and nobody, whatever was ticked before', () => {
    open({ minRole: 'Owner', memberIds: [VIEWER] });
    click(radio('Everyone in the organization'));

    save().click();

    const put = http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${ID}/access` });
    expect(put.request.body).toEqual({ minRole: null, memberIds: [] });
    put.flush({ minRole: null, memberIds: [], inherited: [] });
  });

  it('will not save a limit that lets nobody in by role or by name', () => {
    open({});
    click(radio('Only some people'));

    expect(save().disabled).toBe(true);
    expect(el().textContent).toContain('Choose a role or at least one person');
    click(person('Vic'));
    expect(save().disabled).toBe(false);
  });

  it('says why it was refused and stays open', () => {
    open({});
    click(radio('Only some people'));
    click(person('Vic'));

    save().click();
    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/dashboards/${ID}/access` }).flush(
      { errors: { MinRole: ['That would lock you out of it. Choose a role you have, or add yourself to the list.'] } },
      { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('lock you out');
    expect(close).not.toHaveBeenCalled();
    expect(save().disabled).toBe(false);
  });

  it('tells about the limited folders above, because they apply too', () => {
    open({ inherited: [{ folderId: 'f1', folderName: 'Finance', minRole: 'Editor', people: 2 }] });

    expect(el().querySelector('.inherited')?.textContent).toContain('"Finance"');
    expect(el().querySelector('.inherited')?.textContent).toContain('Editors and Owners and 2 people');
  });

  it('works on a folder, saying a limit covers what is inside it', () => {
    open({}, { kind: 'folder', name: 'Finance' });

    expect(el().querySelector('h2')?.textContent).toContain('Finance');
    expect(el().textContent).toContain('covers everything in it');
    click(radio('Only some people'));
    chooseOption(fixture, 'Minimum role', 'Owner'); // the button is enabled once the rule lets somebody in
    save().click();

    http.expectOne({ method: 'PUT', url: `/api/organizations/${ORG}/folders/${ID}/access` }).flush({ minRole: 'Owner', memberIds: [], inherited: [] });
    expect(close).toHaveBeenCalledWith(true);
  });

  it('does not save before what is set has been read', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, kind: 'dashboard', id: ID, name: 'Sales' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AccessDialogComponent);
    fixture.detectChanges();

    expect(save().disabled).toBe(true);
    http.expectOne(`/api/organizations/${ORG}/members`).flush(people);
    http.expectOne(`/api/organizations/${ORG}/dashboards/${ID}/access`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el().querySelector('[role=alert]')).not.toBeNull();
    expect(save().disabled).toBe(true);
  });
});
