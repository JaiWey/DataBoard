import { CLOUD_HEIGHT, CLOUD_WIDTH, PlacedWord, cloudBounds, cloudSize, fontSizeFor, layoutWords, textWidth, wordsOf } from './word-cloud';

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

describe('cloudSize', () => {
  it('is the usual picture when the place is not known', () => {
    expect(cloudSize(null)).toEqual({ width: CLOUD_WIDTH, height: CLOUD_HEIGHT });
    expect(cloudSize(0)).toEqual({ width: CLOUD_WIDTH, height: CLOUD_HEIGHT });
    expect(cloudSize(Number.NaN)).toEqual({ width: CLOUD_WIDTH, height: CLOUD_HEIGHT });
  });

  it('follows the shape of the place, within reason', () => {
    expect(cloudSize(2)).toEqual({ width: 1000, height: CLOUD_HEIGHT });
    expect(cloudSize(0.2).width).toBe(300); // a very tall place is not narrower than this
    expect(cloudSize(20).width).toBe(2000); // nor a very wide one wider
  });
});

describe('cloudBounds', () => {
  it('has nothing to say about no words', () => {
    expect(cloudBounds([])).toBeNull();
  });

  it('is the room the words take, plus a margin, and no more', () => {
    const placed = layoutWords(many(30));
    const bounds = cloudBounds(placed, 10)!;

    expect(bounds.width).toBeLessThan(CLOUD_WIDTH);
    expect(bounds.height).toBeLessThan(CLOUD_HEIGHT);
    for (const w of placed) {
      expect(w.x - w.boxWidth / 2).toBeGreaterThanOrEqual(bounds.x + 10 - 0.001);
      expect(w.x + w.boxWidth / 2).toBeLessThanOrEqual(bounds.x + bounds.width - 10 + 0.001);
      expect(w.y - w.boxHeight / 2).toBeGreaterThanOrEqual(bounds.y + 10 - 0.001);
      expect(w.y + w.boxHeight / 2).toBeLessThanOrEqual(bounds.y + bounds.height - 10 + 0.001);
    }
    // Something touches each side, so nothing is wasted.
    expect(Math.min(...placed.map((w) => w.x - w.boxWidth / 2))).toBeCloseTo(bounds.x + 10, 5);
    expect(Math.max(...placed.map((w) => w.y + w.boxHeight / 2))).toBeCloseTo(bounds.y + bounds.height - 10, 5);
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

  it('places words in a picture of the shape it is given, and keeps them inside it', () => {
    const wide = layoutWords(many(100), {}, 1600, 500);

    expect(wide.length).toBeGreaterThan(40);
    expect(wide.every((w) => w.x - w.boxWidth / 2 >= 0 && w.x + w.boxWidth / 2 <= 1600 && w.y - w.boxHeight / 2 >= 0 && w.y + w.boxHeight / 2 <= 500)).toBe(true);
  });

  it('spreads out to fill a wide picture instead of staying an oval in the middle', () => {
    const bounds = cloudBounds(layoutWords(many(100), {}, 1600, 500))!;

    expect(bounds.width / bounds.height).toBeGreaterThan(2); // the picture is 3.2 times as wide as tall
  });

  it('gives the same picture every time', () => {
    expect(layoutWords(many(40))).toEqual(layoutWords(many(40)));
  });

  it('shrinks or leaves out a word too long for the picture instead of drawing outside it', () => {
    const placed = layoutWords([{ text: 'x'.repeat(400), value: 5 }, { text: 'short', value: 4 }]);

    expect(placed.map((w) => w.text)).toEqual(['short']);
  });
});
