/** How numbers are written on labels, axes, tooltips and in a Number chart. Mirrors the stored `numberFormat` of a chart style. */
export interface NumberFormatOptions {
  /** Digits after the point (0 to 6). Missing or null leaves the number as it is. */
  decimals?: number | null;
  /** Group digits: 1,234,567. On unless turned off. */
  thousands?: boolean;
  /** Short forms: 1.2K, 3.4M. */
  compact?: boolean;
  prefix?: string | null;
  suffix?: string | null;
}

/** Writes a number the chosen way. Without a format it is written as the browser would (grouped, up to 3 decimals). */
export function formatNumber(value: number, format?: NumberFormatOptions | null): string {
  if (!format) return value.toLocaleString();

  const options: Intl.NumberFormatOptions = { useGrouping: format.thousands !== false };
  if (format.compact) options.notation = 'compact';
  if (format.decimals !== null && format.decimals !== undefined) {
    options.minimumFractionDigits = format.compact ? 0 : format.decimals;
    options.maximumFractionDigits = format.decimals;
  } else if (!format.compact) {
    options.maximumFractionDigits = 20; // as it is
  }
  const text = new Intl.NumberFormat('en-US', options).format(value);
  return `${format.prefix ?? ''}${text}${format.suffix ?? ''}`;
}

/** Writes any cell: a number the chosen way, anything else as text. */
export function formatValue(value: unknown, format?: NumberFormatOptions | null): string {
  if (value === null || value === undefined) return '∅';
  if (typeof value === 'number' && Number.isFinite(value)) return formatNumber(value, format);
  return String(value);
}
