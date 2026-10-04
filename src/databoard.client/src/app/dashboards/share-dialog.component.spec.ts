import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ShareDialogComponent, shareUrl } from './share-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '44444444-4444-4444-4444-444444444444';
const sharesUrl = `/api/organizations/${ORG}/dashboards/${DASH}/shares`;
const orgSharesUrl = `/api/organizations/${ORG}/dashboards/${DASH}/organization-shares`;

const PARTNER = '22222222-2222-2222-2222-222222222222';
const mine = [{ id: ORG, name: 'Home', role: 'Owner' }, { id: PARTNER, name: 'Partner', role: 'Editor' }];

const share = (id: string, expiresUtc: string | null = '2099-01-01T00:00:00Z', expired = false, organizationIds: string[] = []) =>
  ({ id, createdUtc: '2026-10-09T10:00:00Z', expiresUtc, expired, organizationIds });

describe('ShareDialogComponent', () => {
  let fixture: ComponentFixture<ShareDialogComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (cls: string) => el().querySelector(`button.${cls}`) as HTMLButtonElement;
  const choose = (label: string) => {
    const select = el().querySelector('select[aria-label="Link lifetime"]') as HTMLSelectElement;
    select.value = String(Array.from(select.options).findIndex((o) => o.textContent?.trim() === label));
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };

  const open = (existing = [share('s1')], sharedWith: unknown[] = []) => {
    fixture = TestBed.createComponent(ShareDialogComponent);
    fixture.detectChanges();
    http.expectOne(sharesUrl).flush(existing);
    http.expectOne(orgSharesUrl).flush(sharedWith);
    http.expectOne('/api/organizations').flush(mine);
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { orgId: ORG, dashboardId: DASH, dashboardName: 'Sales' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('says what a link allows, and lists the links there are without any secret', () => {
    open([share('s1'), share('s2', null), share('s3', '2026-10-01T00:00:00Z', true)]);

    expect(el().textContent).toContain('Share "Sales"');
    expect(el().textContent).toContain('without signing in');
    expect(el().textContent).toContain('never expires');
    expect(el().querySelectorAll('.shares li').length).toBe(3);
    expect(el().querySelector('.shares .fail')?.textContent).toContain('expired');
    expect(el().querySelector('input[aria-label="Share link"]')).toBeNull();
  });

  it('starts at 30 days, so a link does not stay public forever by accident', () => {
    open([]);

    const select = el().querySelector('select[aria-label="Link lifetime"]') as HTMLSelectElement;
    expect(select.options[select.selectedIndex].textContent?.trim()).toBe('30 days');
    expect(el().textContent).toContain('No links yet.');
  });

  it('creates a link and shows its address once, ready to copy', () => {
    open();

    button('create').click();
    const post = http.expectOne({ method: 'POST', url: sharesUrl });
    expect(post.request.body).toEqual({ expiresInDays: 30, organizationIds: [] });
    post.flush({ id: 's9', token: 'T0KEN', createdUtc: '', expiresUtc: '2026-11-08T00:00:00Z' });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([share('s9'), share('s1')]);
    fixture.detectChanges();

    const input = el().querySelector('input[aria-label="Share link"]') as HTMLInputElement;
    expect(input.value).toBe(`${window.location.origin}/shared/T0KEN`);
    expect(el().textContent).toContain('cannot be shown again');
    expect(el().querySelectorAll('.shares li').length).toBe(2);
  });

  it('sends the lifetime that was chosen, and asks for "never" explicitly', () => {
    open();

    choose('7 days');
    button('create').click();
    http.expectOne({ method: 'POST', url: sharesUrl }).flush({ id: 'a', token: 'A', createdUtc: '', expiresUtc: '' });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
    fixture.detectChanges();
    choose('Never (until I revoke it)');
    button('create').click();

    const post = http.expectOne({ method: 'POST', url: sharesUrl });
    expect(post.request.body).toEqual({ neverExpires: true, organizationIds: [] });
    post.flush({ id: 'b', token: 'B', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
  });

  it('copies the address', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    open();
    button('create').click();
    http.expectOne({ method: 'POST', url: sharesUrl }).flush({ id: 's9', token: 'T0KEN', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
    fixture.detectChanges();

    button('copy-button').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith(shareUrl('T0KEN'));
    expect(button('copy-button').textContent?.trim()).toBe('Copied');
  });

  it('shows why a link could not be made', () => {
    open();

    button('create').click();
    http.expectOne({ method: 'POST', url: sharesUrl }).flush(
      { errors: { Shares: ['A dashboard can have at most 10 active links. Revoke one first.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('at most 10 active links');
    expect(button('create').disabled).toBe(false);
  });

  it('revokes a link after asking, and then lists what is left', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    open([share('s1'), share('s2')]);

    button('revoke').click();

    http.expectOne({ method: 'DELETE', url: `${sharesUrl}/s1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([share('s2')]);
    fixture.detectChanges();
    expect(el().querySelectorAll('.shares li').length).toBe(1);
  });

  it('does nothing when the revoke question is answered no', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    open();

    button('revoke').click();

    http.expectNone({ method: 'DELETE', url: `${sharesUrl}/s1` });
  });

  it('stops offering to copy a link that was just revoked, but not when another one is', () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    open([share('s1')]);
    button('create').click();
    http.expectOne({ method: 'POST', url: sharesUrl }).flush({ id: 's9', token: 'T0KEN', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([share('s9'), share('s1')]);
    fixture.detectChanges();

    (el().querySelectorAll('button.revoke')[1] as HTMLButtonElement).click(); // the old one
    http.expectOne({ method: 'DELETE', url: `${sharesUrl}/s1` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([share('s9')]);
    fixture.detectChanges();
    expect(el().querySelector('input[aria-label="Share link"]')).not.toBeNull();

    button('revoke').click(); // the new one
    http.expectOne({ method: 'DELETE', url: `${sharesUrl}/s9` }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
    fixture.detectChanges();
    expect(el().querySelector('input[aria-label="Share link"]')).toBeNull();
  });

  describe('limiting a link to organizations', () => {
    const radio = (label: string) => el().querySelector(`input[aria-label="${label}"]`) as HTMLInputElement;
    const org = (name: string) => el().querySelector(`input[aria-label="Members of ${name}"]`) as HTMLInputElement | null;
    const pick = (label: string) => {
      radio(label).click();
      fixture.detectChanges();
    };

    it('offers anyone with the link first, with no organizations to choose from', () => {
      open();

      expect(radio('Anyone with the link').checked).toBe(true);
      expect(radio('Only members of chosen organizations').checked).toBe(false);
      expect(org('Home')).toBeNull();
      expect(el().textContent).toContain('without signing in');
    });

    it('lists my organizations when limited, with the dashboard\'s own ticked, and changes what the warning says', () => {
      open();

      pick('Only members of chosen organizations');

      expect(org('Home')!.checked).toBe(true);
      expect(org('Partner')!.checked).toBe(false);
      expect(el().querySelector('.warning')?.textContent).toContain('signed in and belong to one of the organizations');
      expect(el().textContent).not.toContain('without signing in');
    });

    it('sends the ticked organizations, and nothing when it goes back to anyone', () => {
      open();
      pick('Only members of chosen organizations');
      org('Partner')!.click();
      fixture.detectChanges();

      button('create').click();
      const limited = http.expectOne({ method: 'POST', url: sharesUrl });
      expect(limited.request.body).toEqual({ expiresInDays: 30, organizationIds: [ORG, PARTNER] });
      limited.flush({ id: 'a', token: 'A', createdUtc: '', expiresUtc: null, organizationIds: [ORG, PARTNER] });
      http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
      fixture.detectChanges();

      pick('Anyone with the link');
      button('create').click();
      const open2 = http.expectOne({ method: 'POST', url: sharesUrl });
      expect(open2.request.body).toEqual({ expiresInDays: 30, organizationIds: [] });
      open2.flush({ id: 'b', token: 'B', createdUtc: '', expiresUtc: null });
      http.expectOne({ method: 'GET', url: sharesUrl }).flush([]);
    });

    it('will not make a limited link with no organization ticked', () => {
      open();
      pick('Only members of chosen organizations');

      org('Home')!.click();
      fixture.detectChanges();

      expect(button('create').disabled).toBe(true);
      org('Partner')!.click();
      fixture.detectChanges();
      expect(button('create').disabled).toBe(false);
    });

    it('says in the list who each link is for, naming only organizations the person is in', () => {
      const stranger = '33333333-3333-3333-3333-333333333333';
      open([share('s1'), share('s2', null, false, [PARTNER]), share('s3', null, false, [ORG, stranger])]);

      const texts = Array.from(el().querySelectorAll('.shares .who-can')).map((e) => e.textContent?.trim());

      expect(texts).toEqual([
        'anyone with the link',
        'signed-in members of Partner',
        'signed-in members of Home, another organization',
      ]);
    });

    it('shows why a limited link was refused', () => {
      open();
      pick('Only members of chosen organizations');

      button('create').click();
      http.expectOne({ method: 'POST', url: sharesUrl }).flush(
        { errors: { OrganizationIds: ['A link can only be limited to organizations you belong to.'] } }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('organizations you belong to');
    });
  });

  describe('showing the dashboard to other organizations', () => {
    const box = (name: string) => el().querySelector(`input[aria-label="Show to ${name}"]`) as HTMLInputElement | null;
    const orgSharesUrl2 = `/api/organizations/${ORG}/dashboards/${DASH}/organization-shares`;

    it('offers the organizations the person is in, apart from the dashboard\'s own, ticked where it is already shared', () => {
      open([], [{ organizationId: PARTNER, organizationName: 'Partner', sharedUtc: '2026-10-10T00:00:00Z' }]);

      expect(box('Home')).toBeNull();
      expect(box('Partner')!.checked).toBe(true);
      expect(el().textContent).toContain('Shared with us');
    });

    it('shares with an organization the moment its box is ticked, then asks again to show the result', () => {
      open([], []);
      expect(box('Partner')!.checked).toBe(false);

      box('Partner')!.click();

      const post = http.expectOne({ method: 'POST', url: orgSharesUrl2 });
      expect(post.request.body).toEqual({ organizationId: PARTNER });
      post.flush({ organizationId: PARTNER, organizationName: 'Partner', sharedUtc: '' });
      http.expectOne({ method: 'GET', url: orgSharesUrl2 }).flush([{ organizationId: PARTNER, organizationName: 'Partner', sharedUtc: '' }]);
      fixture.detectChanges();
      expect(box('Partner')!.checked).toBe(true);
    });

    it('stops sharing the moment the box is unticked', () => {
      open([], [{ organizationId: PARTNER, organizationName: 'Partner', sharedUtc: '' }]);

      box('Partner')!.click();

      http.expectOne({ method: 'DELETE', url: `${orgSharesUrl2}/${PARTNER}` }).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne({ method: 'GET', url: orgSharesUrl2 }).flush([]);
      fixture.detectChanges();
      expect(box('Partner')!.checked).toBe(false);
    });

    it('puts the box back and says why when it could not be done', () => {
      open([], []);

      box('Partner')!.click();
      http.expectOne({ method: 'POST', url: orgSharesUrl2 }).flush(
        { errors: { OrganizationId: ['A dashboard can only be shared with organizations you belong to.'] } }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(box('Partner')!.checked).toBe(false);
      expect(el().querySelector('[role=alert]')?.textContent).toContain('organizations you belong to');
    });

    it('lists a share with an organization the person is not in without a name, and lets it be stopped', () => {
      const stranger = '33333333-3333-3333-3333-333333333333';
      open([], [{ organizationId: stranger, organizationName: null, sharedUtc: '' }]);

      expect(el().querySelector('.unknown')?.textContent).toContain('Another organization');
      (el().querySelector('button.stop-sharing') as HTMLButtonElement).click();

      http.expectOne({ method: 'DELETE', url: `${orgSharesUrl2}/${stranger}` }).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne({ method: 'GET', url: orgSharesUrl2 }).flush([]);
      fixture.detectChanges();
      expect(el().querySelector('.unknown')).toBeNull();
    });
  });
});
