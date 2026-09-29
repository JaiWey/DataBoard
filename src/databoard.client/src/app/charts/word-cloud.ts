/**
 * Lays out a word cloud: the biggest words in the middle, the rest placed around them on a spiral where nothing is
 * already. Plain functions with no drawing, so they are tested without a page and give the same picture every time.
 */

export type CloudRotation = 'none' | 'mixed' | 'vertical';

/** How a Word cloud looks. Anything missing is the usual look. */
export interface WordCloudOptions {
  /** Only the heaviest words are drawn. */
  maxWords?: number;
  /** Font size of the lightest and of the heaviest word, in the units of the drawing. */
  minFontSize?: number;
  maxFontSize?: number;
  /** none: all words lie flat. mixed: some stand up. vertical: all stand up. */
  rotation?: CloudRotation;
}

export const CLOUD_DEFAULTS = { maxWords: 100, minFontSize: 12, maxFontSize: 60, rotation: 'mixed' as CloudRotation };
export const CLOUD_ROTATIONS: { value: CloudRotation; label: string }[] = [
  { value: 'none', label: 'Flat' },
  { value: 'mixed', label: 'Some upright' },
  { value: 'vertical', label: 'All upright' },
];

/** The size of the picture words are placed in. It is scaled to fit whatever it is shown in. */
export const CLOUD_WIDTH = 800;
export const CLOUD_HEIGHT = 500;

export interface CloudWord {
  text: string;
  value: number;
}

export interface PlacedWord extends CloudWord {
  /** The centre of the word. */
  x: number;
  y: number;
  size: number;
  /** 0 (flat) or 90 (upright, read from the bottom up). */
  rotate: 0 | 90;
  /** Position by weight, 0 for the heaviest: colours are given out in this order. */
  rank: number;
}

/** The words of a result: the first column is the word, the last one its weight. Zero and negative weights are left out. */
export function wordsOf(rows: unknown[][]): CloudWord[] {
  const byText = new Map<string, number>();
  for (const row of rows) {
    const text = String(row[0] ?? '').trim();
    const value = Number(row[row.length - 1]);
    if (!text || !Number.isFinite(value) || value <= 0) continue;
    byText.set(text, (byText.get(text) ?? 0) + value);
  }
  return [...byText].map(([text, value]) => ({ text, value }));
}

/** Wide letters (Chinese, Japanese, Korean, full-width forms) take about a font size; Latin letters about 0.58 of it. */
const WIDE = /[ᄀ-ᅟ⺀-〾ぁ-㏿㐀-䶿一-鿿가-힣豈-﫿＀-｠￠-￦]/;

export function textWidth(text: string, size: number): number {
  let em = 0;
  for (const ch of text) em += WIDE.test(ch) ? 1 : /[A-Z]/.test(ch) ? 0.68 : /[ilj.,;:'!|]/.test(ch) ? 0.3 : 0.58;
  return em * size;
}

interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

const GAP = 3;
const overlaps = (a: Box, b: Box): boolean =>
  Math.abs(a.x - b.x) * 2 < a.w + b.w + GAP && Math.abs(a.y - b.y) * 2 < a.h + b.h + GAP;

/** The choices kept in bounds and in the right order, so odd values cannot break the drawing. */
function settled(options: WordCloudOptions) {
  const maxWords = Math.round(Math.min(300, Math.max(5, options.maxWords ?? CLOUD_DEFAULTS.maxWords)));
  const minFont = Math.max(6, options.minFontSize ?? CLOUD_DEFAULTS.minFontSize);
  const maxFont = Math.max(minFont, options.maxFontSize ?? CLOUD_DEFAULTS.maxFontSize);
  return { maxWords, minFont, maxFont, rotation: options.rotation ?? CLOUD_DEFAULTS.rotation };
}

/** A word's font size: its weight between the lightest and the heaviest word, by square root so one huge word does not shrink the rest. */
export function fontSizeFor(value: number, lightest: number, heaviest: number, minFont: number, maxFont: number): number {
  if (heaviest <= lightest) return (minFont + maxFont) / 2;
  const share = (Math.sqrt(value) - Math.sqrt(lightest)) / (Math.sqrt(heaviest) - Math.sqrt(lightest));
  return minFont + share * (maxFont - minFont);
}

/** Every fifth word stands up when rotation is mixed; which ones does not change from one drawing to the next. */
const standsUp = (rotation: CloudRotation, index: number): boolean =>
  rotation === 'vertical' || (rotation === 'mixed' && index % 5 === 3);

export function layoutWords(
  words: CloudWord[], options: WordCloudOptions = {}, width = CLOUD_WIDTH, height = CLOUD_HEIGHT,
): PlacedWord[] {
  const { maxWords, minFont, maxFont, rotation } = settled(options);
  const chosen = [...words].sort((a, b) => b.value - a.value || a.text.localeCompare(b.text)).slice(0, maxWords);
  if (chosen.length === 0) return [];

  const heaviest = chosen[0].value;
  const lightest = chosen[chosen.length - 1].value;
  const placed: PlacedWord[] = [];
  const boxes: Box[] = [];

  chosen.forEach((word, index) => {
    const upright = standsUp(rotation, index);
    let size = fontSizeFor(word.value, lightest, heaviest, minFont, maxFont);

    // A word that finds no room is tried smaller a few times, then left out.
    for (let attempt = 0; attempt < 4; attempt++, size *= 0.8) {
      if (size < 6) break;
      const along = textWidth(word.text, size);
      const across = size * 1.15;
      const box = { x: 0, y: 0, w: upright ? across : along, h: upright ? along : across };
      if (box.w > width || box.h > height) continue;

      const spot = findSpot(box, boxes, width, height);
      if (!spot) continue;
      box.x = spot.x;
      box.y = spot.y;
      boxes.push(box);
      placed.push({ ...word, x: box.x, y: box.y, size, rotate: upright ? 90 : 0, rank: index });
      break;
    }
  });
  return placed;
}

/** The first place on an outward spiral from the centre where the box is inside the picture and touches no other word. */
function findSpot(box: Box, taken: Box[], width: number, height: number): { x: number; y: number } | null {
  const cx = width / 2;
  const cy = height / 2;
  const stretch = width / height; // the spiral is wider than tall, like the picture
  const limit = Math.hypot(width, height);

  for (let t = 0; ; ) {
    const radius = 1.6 * t;
    t += Math.min(0.4, Math.max(0.04, 8 / Math.max(radius, 1))); // about 8 pixels along the spiral at a time
    if (radius > limit) return null;
    const x = cx + radius * Math.cos(t) * stretch;
    const y = cy + radius * Math.sin(t);
    if (x - box.w / 2 < 0 || x + box.w / 2 > width || y - box.h / 2 < 0 || y + box.h / 2 > height) continue;
    const candidate = { ...box, x, y };
    if (!taken.some((other) => overlaps(candidate, other))) return { x, y };
  }
}
