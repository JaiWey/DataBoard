/**
 * Where to go after signing in: a page of this app and nothing else. Anything that could lead away (another site, a
 * protocol-relative address, a backslash trick, control characters) is ignored, so a sign-in link cannot be made to send
 * people elsewhere.
 */
export function safeReturnUrl(url: string | null | undefined): string | null {
  if (!url || url.length > 500) return null;
  if (url[0] !== '/' || url.startsWith('//') || url.includes('\\')) return null;
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(url)) return null;
  return url;
}

/** The sign-in pages themselves are not somewhere to come back to. */
export function isAuthPage(url: string): boolean {
  return /^\/(login|register)(\?|\/|$)/.test(url);
}

/** The query that carries where to go back to, or none when there is nowhere worth going back to. */
export function returnQuery(url: string | null | undefined): { returnUrl: string } | undefined {
  const safe = safeReturnUrl(url);
  return safe && safe !== '/' && !isAuthPage(safe) ? { returnUrl: safe } : undefined;
}
