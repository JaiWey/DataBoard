/** Turning a query result into a file the person can keep: CSV text and the download itself. */

interface Tabular {
  columns: { name: string }[];
  rows: unknown[][];
}

/**
 * Spreadsheets run a cell that starts with one of these as a formula, so a text value that begins with one is
 * prefixed with an apostrophe (the usual defence against CSV injection). Numbers are never touched: -5 is a number.
 */
const FORMULA_START = /^[=+\-@\t\r]/;

function cell(value: unknown): string {
  if (value === null || value === undefined) return '';
  let text = typeof value === 'object' ? JSON.stringify(value) : String(value);
  if (typeof value === 'string' && FORMULA_START.test(text)) text = `'${text}`;
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** RFC 4180 CSV with CRLF line endings. The caller adds the byte order mark when the file is saved. */
export function toCsv(result: Tabular): string {
  const lines = [result.columns.map((c) => cell(c.name)).join(',')];
  for (const row of result.rows) lines.push(row.map(cell).join(','));
  return lines.join('\r\n') + '\r\n';
}

/** A name that is safe to save as: no path or reserved characters, not empty, and not too long. */
export function fileName(title: string, extension: string): string {
  const base = title.replace(/[\\/:*?"<>|\u0000-\u001f]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 80) || 'export';
  return `${base}.${extension}`;
}

function save(href: string, name: string): void {
  const link = document.createElement('a');
  link.href = href;
  link.download = name;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
}

/** Saves text as a file. The byte order mark lets Excel read UTF-8 (accents, Chinese) correctly. */
export function downloadCsv(title: string, result: Tabular): void {
  const url = URL.createObjectURL(new Blob(['﻿', toCsv(result)], { type: 'text/csv;charset=utf-8' }));
  save(url, fileName(title, 'csv'));
  // The click has started the download by now; the URL is only needed until then.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

export function downloadImage(title: string, dataUrl: string): void {
  save(dataUrl, fileName(title, 'png'));
}
