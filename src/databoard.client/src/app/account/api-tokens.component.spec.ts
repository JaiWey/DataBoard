import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiTokensComponent, mcpUrl } from './api-tokens.component';
import { answerConfirm } from '../shared/confirm.testing';
import { chooseOption, selectOptions, selectedValue } from '../shared/select.testing';

const token = (id: string, over: Record<string, unknown> = {}) => ({
  id, name: `Token ${id}`, scope: 'Write', createdUtc: '2026-10-09T10:00:00Z', expiresUtc: '2099-01-01T00:00:00Z', lastUsedUtc: null, expired: false, ...over,
});

describe('ApiTokensComponent', () => {
  let fixture: ComponentFixture<ApiTokensComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (cls: string) => el().querySelector(`button.${cls}`) as HTMLButtonElement;
  const type = (value: string) => {
    const input = el().querySelector('input[aria-label="Token name"]') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const choose = (label: string, text: string) => {
    const [value] = selectOptions(fixture, label).find(([, t]) => t === text)!;
    chooseOption(fixture, label, value);
  };

  const open = (existing = [token('t1')]) => {
    fixture = TestBed.createComponent(ApiTokensComponent);
    fixture.detectChanges();
    http.expectOne('/api/me/tokens').flush(existing);
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('explains what a token is for, and lists the tokens with what they can do and when they were last used', () => {
    open([token('t1', { lastUsedUtc: '2026-10-09T11:00:00Z' }), token('t2', { scope: 'Read', expiresUtc: null }), token('t3', { expired: true, expiresUtc: '2026-10-01T00:00:00Z' })]);

    expect(el().textContent).toContain('cannot delete anything');
    const rows = Array.from(el().querySelectorAll('.tokens li')).map((li) => li.textContent ?? '');
    expect(rows.length).toBe(3);
    expect(rows[0]).toContain('Read and create');
    expect(rows[0]).toContain('last used');
    expect(rows[1]).toContain('Read only');
    expect(rows[1]).toContain('never expires');
    expect(rows[1]).toContain('never used');
    expect(rows[2]).toContain('expired');
  });

  it('starts with the wider scope and a 90-day lifetime, and needs a name', () => {
    open([]);

    expect(selectedValue(fixture, 'Token scope')).toBe('Write');
    const lifetime = selectOptions(fixture, 'Token lifetime').find(([value]) => value === selectedValue(fixture, 'Token lifetime'));
    expect(lifetime?.[1]).toBe('90 days');
    expect(button('create').disabled).toBe(true);
    type('Claude Desktop');
    expect(button('create').disabled).toBe(false);
    expect(el().textContent).toContain('You have no tokens yet.');
  });

  it('makes a token, shows its secret once, and says how to connect with it', () => {
    open([]);
    type('  Claude Desktop ');

    button('create').click();
    const post = http.expectOne({ method: 'POST', url: '/api/me/tokens' });
    expect(post.request.body).toEqual({ name: 'Claude Desktop', scope: 'Write', expiresInDays: 90 });
    post.flush({ id: 't9', name: 'Claude Desktop', scope: 'Write', token: 'dbp_SECRET', createdUtc: '', expiresUtc: '2027-01-01T00:00:00Z' });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([token('t9', { name: 'Claude Desktop' })]);
    fixture.detectChanges();

    expect((el().querySelector('input[aria-label="Access token"]') as HTMLInputElement).value).toBe('dbp_SECRET');
    expect(el().textContent).toContain('cannot be shown again');
    const command = el().querySelector('[aria-label="Claude Code command"]')!.textContent!;
    expect(command).toBe(`claude mcp add --transport http databoard ${mcpUrl()} --header "Authorization: Bearer dbp_SECRET"`);
    const settings = JSON.parse(el().querySelector('[aria-label="MCP client settings"]')!.textContent!);
    expect(settings.mcpServers.databoard).toEqual({ type: 'http', url: mcpUrl(), headers: { Authorization: 'Bearer dbp_SECRET' } });
    expect((el().querySelector('input[aria-label="Token name"]') as HTMLInputElement).value).toBe(''); // ready for another
  });

  it('sends the scope and lifetime that were chosen, and asks for "never" explicitly', () => {
    open([]);
    type('CI');
    choose('Token scope', 'Read only');
    choose('Token lifetime', 'Never (until I revoke it)');

    button('create').click();

    const post = http.expectOne({ method: 'POST', url: '/api/me/tokens' });
    expect(post.request.body).toEqual({ name: 'CI', scope: 'Read', neverExpires: true });
    post.flush({ id: 't9', name: 'CI', scope: 'Read', token: 'dbp_X', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([]);
  });

  it('copies the token', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    open([]);
    type('x');
    button('create').click();
    http.expectOne({ method: 'POST', url: '/api/me/tokens' }).flush({ id: 't9', name: 'x', scope: 'Write', token: 'dbp_COPY', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([]);
    fixture.detectChanges();

    button('copy-token').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith('dbp_COPY');
    expect(button('copy-token').textContent?.trim()).toBe('Copied');
  });

  it('shows why a token could not be made', () => {
    open([]);
    type('x');

    button('create').click();
    http.expectOne({ method: 'POST', url: '/api/me/tokens' }).flush(
      { errors: { Tokens: ['You can have at most 20 tokens. Revoke one first.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('at most 20 tokens');
    expect(el().querySelector('input[aria-label="Access token"]')).toBeNull();
  });

  it('revokes a token after asking, and then lists what is left', () => {
    answerConfirm(true);
    open([token('t1'), token('t2')]);

    button('revoke').click();

    http.expectOne({ method: 'DELETE', url: '/api/me/tokens/t1' }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([token('t2')]);
    fixture.detectChanges();
    expect(el().querySelectorAll('.tokens li').length).toBe(1);
  });

  it('does nothing when the revoke question is answered no', () => {
    answerConfirm(false);
    open();

    button('revoke').click();

    http.expectNone({ method: 'DELETE', url: '/api/me/tokens/t1' });
  });

  it('stops offering to copy a token that was just revoked', () => {
    answerConfirm(true);
    open([]);
    type('x');
    button('create').click();
    http.expectOne({ method: 'POST', url: '/api/me/tokens' }).flush({ id: 't9', name: 'x', scope: 'Write', token: 'dbp_X', createdUtc: '', expiresUtc: null });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([token('t9')]);
    fixture.detectChanges();

    button('revoke').click();
    http.expectOne({ method: 'DELETE', url: '/api/me/tokens/t9' }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: '/api/me/tokens' }).flush([]);
    fixture.detectChanges();

    expect(el().querySelector('input[aria-label="Access token"]')).toBeNull();
  });
});
