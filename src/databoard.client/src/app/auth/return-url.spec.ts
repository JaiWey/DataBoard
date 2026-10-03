import { isAuthPage, returnQuery, safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/', '/organizations', '/shared/abc_DEF-123', '/organizations/1/dashboards/2?tab=x#top'])('keeps a page of this app: %s', (url) => {
    expect(safeReturnUrl(url)).toBe(url);
  });

  it.each([
    'https://evil.example/x', 'http://evil.example', '//evil.example', '///evil.example', '/\\evil.example', '\\evil',
    'evil', 'javascript:alert(1)', '', '   ', '/a\nb', '/a\u0000b',
  ])('drops anything that could lead away: %j', (url) => {
    expect(safeReturnUrl(url)).toBeNull();
  });

  it('drops nothing, null and very long addresses', () => {
    expect(safeReturnUrl(null)).toBeNull();
    expect(safeReturnUrl(undefined)).toBeNull();
    expect(safeReturnUrl('/' + 'a'.repeat(600))).toBeNull();
  });
});

describe('isAuthPage', () => {
  it('knows the sign-in pages, with or without a query', () => {
    expect(['/login', '/register', '/login?returnUrl=%2Fx', '/register/'].every(isAuthPage)).toBe(true);
    expect(['/', '/loginx', '/organizations', '/shared/login'].some(isAuthPage)).toBe(false);
  });
});

describe('returnQuery', () => {
  it('carries a page worth coming back to', () => {
    expect(returnQuery('/shared/abc')).toEqual({ returnUrl: '/shared/abc' });
  });

  it('carries nothing for the home page, the sign-in pages or something unsafe', () => {
    expect(returnQuery('/')).toBeUndefined();
    expect(returnQuery('/login')).toBeUndefined();
    expect(returnQuery('//evil.example')).toBeUndefined();
    expect(returnQuery(null)).toBeUndefined();
  });
});
