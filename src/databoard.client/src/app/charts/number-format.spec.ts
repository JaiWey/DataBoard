import { formatNumber, formatValue } from './number-format';

describe('formatNumber', () => {
  it('writes a number the browser way when there is no format', () => {
    expect(formatNumber(1234.5)).toBe((1234.5).toLocaleString());
    expect(formatNumber(1234.5, null)).toBe((1234.5).toLocaleString());
  });

  it('groups thousands unless told not to', () => {
    expect(formatNumber(1234567, {})).toBe('1,234,567');
    expect(formatNumber(1234567, { thousands: true })).toBe('1,234,567');
    expect(formatNumber(1234567, { thousands: false })).toBe('1234567');
  });

  it('fixes the number of decimals, padding and rounding', () => {
    expect(formatNumber(3.14159, { decimals: 2 })).toBe('3.14');
    expect(formatNumber(3, { decimals: 2 })).toBe('3.00');
    expect(formatNumber(2.5, { decimals: 0 })).toBe('3');
    expect(formatNumber(1234.5678, { decimals: 1, thousands: false })).toBe('1234.6');
  });

  it('keeps every digit when decimals are left as they are', () => {
    expect(formatNumber(0.123456, {})).toBe('0.123456');
    expect(formatNumber(1.5, { decimals: null })).toBe('1.5');
  });

  it('writes short forms, without forcing decimals on them', () => {
    expect(formatNumber(1200, { compact: true })).toBe('1.2K');
    expect(formatNumber(3_400_000, { compact: true })).toBe('3.4M');
    expect(formatNumber(1500, { compact: true, decimals: 0 })).toBe('2K');
    expect(formatNumber(999, { compact: true })).toBe('999');
  });

  it('puts a prefix and a suffix around the number, also a negative one', () => {
    expect(formatNumber(1234.5, { decimals: 2, prefix: '$' })).toBe('$1,234.50');
    expect(formatNumber(12, { suffix: ' kg' })).toBe('12 kg');
    expect(formatNumber(-5, { prefix: '$', decimals: 0 })).toBe('$-5');
    expect(formatNumber(1200, { compact: true, prefix: '€', suffix: '/mo' })).toBe('€1.2K/mo');
  });
});

describe('formatValue', () => {
  it('formats numbers, shows missing values as a hollow circle and leaves text alone', () => {
    expect(formatValue(1234, { prefix: '#' })).toBe('#1,234');
    expect(formatValue(null)).toBe('∅');
    expect(formatValue(undefined)).toBe('∅');
    expect(formatValue('Auckland', { prefix: '#' })).toBe('Auckland');
    expect(formatValue(Number.NaN)).toBe('NaN');
  });
});
