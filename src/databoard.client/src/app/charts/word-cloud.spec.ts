import { CLOUD_HEIGHT, CLOUD_WIDTH, PlacedWord, fontSizeFor, layoutWords, textWidth, wordsOf } from './word-cloud';

const many = (n: number) => Array.from({ length: n }, (_, i) => ({ text: `word${i}`, value: n - i }));

const inside = (w: PlacedWord) => {
  const along = textWidth(w.text, w.size);
  const across = w.size * 1.15;
  const [bw, bh] = w.rotate ? [across, along] : [along, across];
  return w.x - bw / 2 >= 0 && w.x + bw / 2 <= CLOUD_WIDTH && w.y - bh / 2 >= 0 && w.y + bh / 2 <= CLOUD_HEIGHT;
};

const boxOf = (w: PlacedWord) => {
  const along = textWidth(w.text, w.size);
  const across = w.size * 1.15;
  return w.rotate ? { w: across, h: along } : { w: along, h: across };
};

describe('wordsOf', () => {
  it('takes the first column as the word and the last as its weight', () => {
    expect(wordsOf([['apple', 'x', 5], ['pear', 'y', 2]])).toEqual([{ text: 'apple', value: 5 }, { text: 'pear', value: 2 }]);
  });

  it('adds up a word that comes twice and trims it', () => {
    expect(wordsOf([['apple', 2], [' apple ', 3]])).toEqual([{ text: 'apple', value: 5 }]);
  });

  it('leaves out empty words and weights that are zero, negative or not numbers', () => {
    expect(wordsOf([['', 4], [null, 4], ['a', 0], ['b', -1], ['c', 'many'], ['d', null], ['e', '7']])).toEqual([{ text: 'e', value: 7 }]);
  });
});

describe('textWidth', () => {
  it('counts a Chinese character as a full size and a Latin letter as less', () => {
    expect(textWidth('数据', 20)).toBe(40);
    expect(textWidth('ab', 20)).toBeLessThan(textWidth('数据', 20));
    expect(textWidth('', 20)).toBe(0);
  });
});

describe('fontSizeFor', () => {
  it('goes from the smallest size for the lightest word to the largest for the heaviest', () => {
    expect(fontSizeFor(1, 1, 100, 12, 60)).toBeCloseTo(12);
    expect(fontSizeFor(100, 1, 100, 12, 60)).toBeCloseTo(60);
    expect(fontSizeFor(25, 1, 100, 12, 60)).toBeGreaterThan(12);
    expect(fontSizeFor(25, 1, 100, 12, 60)).toBeLessThan(60);
  });

  it('uses the middle size when all words weigh the same', () => {
    expect(fontSizeFor(5, 5, 5, 10, 50)).toBe(30);
  });
});

describe('layoutWords', () => {
  it('has nothing to place when there are no words', () => {
    expect(layoutWords([])).toEqual([]);
  });

  it('puts the heaviest word biggest and nearest the middle', () => {
    const placed = layoutWords(many(20));

    expect(placed[0].text).toBe('word0');
    expect(placed[0].size).toBe(Math.max(...placed.map((w) => w.size)));
    expect(Math.abs(placed[0].x - CLOUD_WIDTH / 2)).toBeLessThan(5);
    expect(Math.abs(placed[0].y - CLOUD_HEIGHT / 2)).toBeLessThan(5);
  });

  it('never lets two words overlap or leave the picture', () => {
    const placed = layoutWords(many(120), { maxWords: 120 });

    expect(placed.length).toBeGreaterThan(40);
    expect(placed.every(inside)).toBe(true);
    for (let i = 0; i < placed.length; i++) {
      for (let j = i + 1; j < placed.length; j++) {
        const a = boxOf(placed[i]);
        const b = boxOf(placed[j]);
        const apart = Math.abs(placed[i].x - placed[j].x) * 2 >= a.w + b.w || Math.abs(placed[i].y - placed[j].y) * 2 >= a.h + b.h;
        expect(apart, `${placed[i].text} and ${placed[j].text}`).toBe(true);
      }
    }
  });

  it('draws only as many words as asked, the heaviest ones', () => {
    const placed = layoutWords(many(50), { maxWords: 10 });

    expect(placed.length).toBeLessThanOrEqual(10);
    expect(placed.every((w) => Number(w.text.slice(4)) < 10)).toBe(true);
  });

  it('keeps words flat with none, and stands them all up with vertical', () => {
    expect(layoutWords(many(15), { rotation: 'none' }).every((w) => w.rotate === 0)).toBe(true);
    expect(layoutWords(many(15), { rotation: 'vertical' }).every((w) => w.rotate === 90)).toBe(true);
  });

  it('stands only some words up with mixed', () => {
    const rotated = layoutWords(many(30), { rotation: 'mixed' }).filter((w) => w.rotate === 90).length;

    expect(rotated).toBeGreaterThan(0);
    expect(rotated).toBeLessThan(30);
  });

  it('keeps sizes inside the range asked for, and copes with a range given the wrong way round', () => {
    const sizes = layoutWords(many(10), { minFontSize: 20, maxFontSize: 30 }).map((w) => w.size);
    expect(Math.min(...sizes)).toBeGreaterThanOrEqual(20 * 0.8 ** 3 - 0.001);
    expect(Math.max(...sizes)).toBeLessThanOrEqual(30);

    expect(layoutWords(many(5), { minFontSize: 50, maxFontSize: 10 }).length).toBeGreaterThan(0);
  });

  it('gives the same picture every time', () => {
    expect(layoutWords(many(40))).toEqual(layoutWords(many(40)));
  });

  it('shrinks or leaves out a word too long for the picture instead of drawing outside it', () => {
    const placed = layoutWords([{ text: 'x'.repeat(400), value: 5 }, { text: 'short', value: 4 }]);

    expect(placed.map((w) => w.text)).toEqual(['short']);
  });
});
