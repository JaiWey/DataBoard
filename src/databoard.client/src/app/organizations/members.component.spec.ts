import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { MembersComponent } from './members.component';
import { answerConfirm, ConfirmStub } from '../shared/confirm.testing';
import { chooseOption, selectDisabled, selectOptions, selectedValue } from '../shared/select.testing';

const ORG = '11111111-1111-1111-1111-111111111111';
const ME = 'a0000000-0000-0000-0000-000000000001';
const BEA = 'a0000000-0000-0000-0000-000000000002';
const CAL = 'a0000000-0000-0000-0000-000000000003';
const base = `/api/organizations/${ORG}/members`;

const member = (userId: string, displayName: string, role: string) => ({
  userId, displayName, email: `${displayName.toLowerCase()}@example.com`, role, joinedUtc: '2026-10-01T00:00:00Z',
});
const everyone = () => [member(ME, 'Me', 'Owner'), member(BEA, 'Bea', 'Editor'), member(CAL, 'Cal', 'Viewer')];

describe('MembersComponent', () => {
  let fixture: ComponentFixture<MembersComponent>;
  let http: HttpTestingController;
  let confirmSpy: ConfirmStub;

  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('tbody tr'));
  const row = (name: string) => rows().find((r) => r.querySelector('td')?.textContent?.includes(name))!;
  const roleOf = (name: string) => selectedValue(fixture, `Role of ${name}`);
  const roleDisabled = (name: string) => selectDisabled(fixture, `Role of ${name}`);
  const button = (name: string, kind: 'remove' | 'leave') => row(name).querySelector(`button.${kind}`) as HTMLButtonElement | null;

  /** Opens the page as someone with this role; `members` is what the server lists. */
  const open = (role: 'Viewer' | 'Editor' | 'Owner', members: object[] = everyone()) => {
    fixture = TestBed.createComponent(MembersComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(base).flush(members);
    fixture.detectChanges();
  };

  const settle = (members: object[] = everyone(), role: string = 'Owner') => {
    http.expectOne('/api/organizations').flush([{ id: ORG, name: 'Acme', role }]);
    http.expectOne(base).flush(members);
    fixture.detectChanges();
  };

  beforeEach(() => {
    confirmSpy = answerConfirm(true);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
        { provide: AuthService, useValue: { user: signal({ id: ME, email: 'me@example.com', displayName: 'Me' }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    confirmSpy.ask.mockRestore();
  });

  it('lists everyone with their name, email, role and when they joined, and marks who you are', () => {
    open('Viewer');

    expect(el().querySelector('h2')?.textContent).toContain('Acme');
    expect(rows().length).toBe(3);
    expect(row('Bea').textContent).toContain('bea@example.com');
    expect(row('Bea').textContent).toContain('Editor');
    expect(row('Bea').textContent).toMatch(/Oct 1, 2026/);
    expect(row('Me').textContent).toContain('(you)');
    expect(row('Bea').textContent).not.toContain('(you)');
  });

  it('shows a viewer or an editor the list with no way to change anyone, only to leave', () => {
    for (const role of ['Viewer', 'Editor'] as const) {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
          { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: ORG }) } } },
          { provide: AuthService, useValue: { user: signal({ id: CAL, email: 'cal@example.com', displayName: 'Cal' }) } },
        ],
      });
      http = TestBed.inject(HttpTestingController);
      open(role);

      expect(el().querySelectorAll('mat-select').length).toBe(0);
      expect(el().querySelector('form')).toBeNull();
      expect(el().querySelectorAll('button.remove').length).toBe(0);
      expect(button('Cal', 'leave')).not.toBeNull();
      expect(button('Bea', 'leave')).toBeNull();
      http.verify();
    }
  });

  it('gives an owner a role choice for everyone, remove for the others, and leave for themselves', () => {
    open('Owner');

    expect(roleOf('Bea')).toBe('Editor');
    expect(roleOf('Cal')).toBe('Viewer');
    expect(selectOptions(fixture, 'Role of Bea').map(([value]) => value)).toEqual(['Viewer', 'Editor', 'Owner']);
    expect(button('Bea', 'remove')).not.toBeNull();
    expect(button('Cal', 'remove')).not.toBeNull();
    expect(button('Me', 'remove')).toBeNull();
    expect(button('Me', 'leave')).not.toBeNull();
    expect(el().querySelector('form')).not.toBeNull();
  });

  it('changes a role with the one that was chosen, and shows what the server says afterwards', () => {
    open('Owner');
    chooseOption(fixture, 'Role of Cal', 'Editor');

    const put = http.expectOne({ method: 'PUT', url: `${base}/${CAL}` });
    expect(put.request.body).toEqual({ role: 'Editor' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    settle([member(ME, 'Me', 'Owner'), member(BEA, 'Bea', 'Editor'), member(CAL, 'Cal', 'Editor')]);

    expect(roleOf('Cal')).toBe('Editor');
  });

  it('puts the choice back and says why when a role could not be changed', () => {
    open('Owner');
    chooseOption(fixture, 'Role of Cal', 'Owner');

    http.expectOne({ method: 'PUT', url: `${base}/${CAL}` }).flush({ detail: 'nope' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')).not.toBeNull();
    expect(roleOf('Cal')).toBe('Viewer');
  });

  it('does not offer to demote, remove or leave for the only owner', () => {
    open('Owner');

    expect(roleDisabled('Me')).toBe(true);
    expect(button('Me', 'leave')!.disabled).toBe(true);
    expect(roleDisabled('Bea')).toBe(false);
  });

  it('offers it again once there is a second owner', () => {
    open('Owner', [member(ME, 'Me', 'Owner'), member(BEA, 'Bea', 'Owner'), member(CAL, 'Cal', 'Viewer')]);

    expect(roleDisabled('Me')).toBe(false);
    expect(button('Me', 'leave')!.disabled).toBe(false);
  });

  it('asks before an owner gives up their own ownership, and does nothing if they say no', () => {
    open('Owner', [member(ME, 'Me', 'Owner'), member(BEA, 'Bea', 'Owner')]);
    confirmSpy.answer(false);
    chooseOption(fixture, 'Role of Me', 'Viewer');

    expect(confirmSpy.ask).toHaveBeenCalledTimes(1);
    http.expectNone({ method: 'PUT', url: `${base}/${ME}` });
    expect(roleOf('Me')).toBe('Owner');
  });

  it('removes a member after asking, with their name in the question, and shows the list without them', () => {
    open('Owner');

    button('Bea', 'remove')!.click();

    expect(confirmSpy.messages[0]).toContain('Bea');
    expect(confirmSpy.messages[0]).toContain('bea@example.com');
    http.expectOne({ method: 'DELETE', url: `${base}/${BEA}` }).flush(null, { status: 204, statusText: 'No Content' });
    settle([member(ME, 'Me', 'Owner'), member(CAL, 'Cal', 'Viewer')]);
    expect(rows().length).toBe(2);
  });

  it('does not remove anyone when the question is answered no', () => {
    open('Owner');
    confirmSpy.answer(false);

    button('Bea', 'remove')!.click();

    http.expectNone({ method: 'DELETE', url: `${base}/${BEA}` });
  });

  it('shows why a removal was refused', () => {
    open('Owner');

    button('Bea', 'remove')!.click();
    http.expectOne({ method: 'DELETE', url: `${base}/${BEA}` }).flush(
      { detail: 'An organization needs at least one Owner. Make someone else an Owner first.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('at least one Owner');
  });

  it('leaves after asking and goes back to the list of organizations', () => {
    open('Viewer', [member(BEA, 'Bea', 'Owner'), member(ME, 'Me', 'Viewer')]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    button('Me', 'leave')!.click();

    expect(confirmSpy.messages[0]).toContain('Acme');
    http.expectOne({ method: 'DELETE', url: `${base}/${ME}` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/organizations').flush([]);
    http.expectOne(base).flush([], { status: 404, statusText: 'Not Found' });
    expect(navigate).toHaveBeenCalledWith(['/organizations']);
  });

  it('adds a member by email with a role, and clears the email afterwards', () => {
    open('Owner');
    const email = el().querySelector('input[aria-label="Member email"]') as HTMLInputElement;
    email.value = ' new@example.com ';
    email.dispatchEvent(new Event('input'));
    chooseOption(fixture, 'New member role', 'Editor');

    (el().querySelector('button.add') as HTMLButtonElement).click();

    const post = http.expectOne({ method: 'POST', url: base });
    expect(post.request.body).toEqual({ email: 'new@example.com', role: 'Editor' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    settle();
    expect(email.value).toBe('');
  });

  it('will not add without an email that looks like one', () => {
    open('Owner');
    const email = el().querySelector('input[aria-label="Member email"]') as HTMLInputElement;
    email.value = 'not-an-email';
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect((el().querySelector('button.add') as HTMLButtonElement).disabled).toBe(true);
  });

  it('says so when nobody has that email', () => {
    open('Owner');
    const email = el().querySelector('input[aria-label="Member email"]') as HTMLInputElement;
    email.value = 'ghost@example.com';
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (el().querySelector('button.add') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: base }).flush({ detail: 'No user with that email.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('No user with that email.');
    expect(email.value).toBe('ghost@example.com');
  });

  it('says so when the members cannot be loaded', () => {
    fixture = TestBed.createComponent(MembersComponent);
    fixture.detectChanges();
    http.expectOne('/api/organizations').flush([]);
    http.expectOne(base).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')).not.toBeNull();
  });
});
