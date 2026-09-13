import { CACHE_CHOICES, ago, cacheLabel } from './relative-time';

describe('ago', () => {
  const now = Date.parse('2026-10-10T12:00:00Z');

  it.each([
    ['2026-10-10T11:59:50Z', 'less than a minute ago'],
    ['2026-10-10T12:00:30Z', 'less than a minute ago'], // a clock a little ahead is not "in the future"
    ['2026-10-10T11:59:00Z', '1 min ago'],
    ['2026-10-10T11:15:00Z', '45 min ago'],
    ['2026-10-10T11:00:00Z', '1 h ago'],
    ['2026-10-09T13:00:00Z', '23 h ago'],
    ['2026-10-08T12:00:00Z', '2 days ago'],
    ['2026-09-30T12:00:00Z', '10 days ago'],
  ])('says how old %s is', (utc, expected) => expect(ago(utc, now)).toBe(expected));

  it('says nothing for a date it cannot read', () => expect(ago('not a date', now)).toBe(''));
});

describe('cacheLabel', () => {
  it('names each choice', () => {
    expect(CACHE_CHOICES.map((c) => cacheLabel(c.seconds))).toEqual([
      'Off (always live)', '1 minute', '5 minutes', '15 minutes', '1 hour', '6 hours', '24 hours',
    ]);
  });

  it('still labels a setting that is not one of the choices', () => {
    expect(cacheLabel(120)).toBe('2 minutes');
    expect(cacheLabel(90)).toBe('90 seconds');
  });
});
