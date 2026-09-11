import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ShareDialogComponent, shareUrl } from './share-dialog.component';

const ORG = '11111111-1111-1111-1111-111111111111';
const DASH = '44444444-4444-4444-4444-444444444444';
const sharesUrl = `/api/organizations/${ORG}/dashboards/${DASH}/shares`;

const share = (id: string, expiresUtc: string | null = '2099-01-01T00:00:00Z', expired = false) => ({ id, createdUtc: '2026-10-09T10:00:00Z', expiresUtc, expired });

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

  const open = (existing = [share('s1')]) => {
    fixture = TestBed.createComponent(ShareDialogComponent);
    fixture.detectChanges();
    http.expectOne(sharesUrl).flush(existing);
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
    expect(post.request.body).toEqual({ expiresInDays: 30 });
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
    expect(post.request.body).toEqual({ neverExpires: true });
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
});
