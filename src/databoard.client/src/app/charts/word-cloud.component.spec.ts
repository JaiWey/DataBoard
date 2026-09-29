import { TestBed } from '@angular/core/testing';
import { ChartStyleOptions } from './chart-option';
import { WordCloudComponent } from './word-cloud.component';

const result = {
  columns: [{ name: 'word' }, { name: 'SUM(n)' }],
  rows: [['alpha', 90], ['beta', 40], ['gamma', 12], ['delta', 1234567]],
};

function draw(style: ChartStyleOptions = {}, rows: unknown[][] = result.rows) {
  const fixture = TestBed.createComponent(WordCloudComponent);
  fixture.componentRef.setInput('result', { columns: result.columns, rows });
  fixture.componentRef.setInput('chartStyle', style);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const texts = (el: HTMLElement) => Array.from(el.querySelectorAll('text'));

describe('WordCloudComponent', () => {
  it('draws every word once, bigger for a heavier word', () => {
    const el = draw();
    const words = texts(el);

    expect(words.map((t) => t.textContent!.replace(/\s+/g, ' ').trim().split(':')[0])).toEqual(['delta', 'alpha', 'beta', 'gamma']);
    const size = (name: string) => Number(words.find((t) => t.textContent!.includes(name))!.getAttribute('font-size'));
    expect(size('delta')).toBeGreaterThan(size('alpha'));
    expect(size('alpha')).toBeGreaterThan(size('gamma'));
  });

  it('is a picture with a text alternative', () => {
    const svg = draw().querySelector('svg')!;

    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('Word cloud');
    expect(svg.getAttribute('viewBox')).toBe('0 0 800 500');
  });

  it('tells the weight, written the way the number format says, when pointing at a word', () => {
    const el = draw({ numberFormat: { thousands: true, prefix: '$' } });

    expect(Array.from(el.querySelectorAll('title')).map((t) => t.textContent)).toContain('delta: $1,234,567');
  });

  it('takes colours from the palette, or from colours chosen, or from a colour chosen for a word', () => {
    const fills = (el: HTMLElement) => texts(el).map((t) => t.getAttribute('fill'));

    expect(new Set(fills(draw())).size).toBeGreaterThan(1);
    expect(new Set(fills(draw({ colors: ['#112233'] })))).toEqual(new Set(['#112233']));

    const chosen = draw({ colors: ['#112233'], seriesColors: { alpha: '#ff0000' } });
    expect(texts(chosen).find((t) => t.textContent!.includes('alpha'))!.getAttribute('fill')).toBe('#ff0000');
  });

  it('stands words up when asked, and keeps them flat otherwise', () => {
    const rotated = (style: ChartStyleOptions) => texts(draw(style)).filter((t) => t.getAttribute('transform')).length;

    expect(rotated({ wordCloud: { rotation: 'none' } })).toBe(0);
    expect(rotated({ wordCloud: { rotation: 'vertical' } })).toBe(4);
  });

  it('uses the chosen font', () => {
    const el = draw({ labelFont: { family: 'serif', weight: 'normal' } });

    expect(texts(el)[0].getAttribute('font-family')).toBe('serif');
    expect(texts(el)[0].getAttribute('font-weight')).toBe('normal');
  });

  it('draws nothing for rows that have no usable word', () => {
    expect(texts(draw({}, [['', 5], ['a', 0]])).length).toBe(0);
  });

  it('shows words written in other scripts', () => {
    expect(texts(draw({}, [['数据看板', 5], ['仪表盘', 3]])).map((t) => t.textContent!.trim().split(':')[0])).toEqual(['数据看板', '仪表盘']);
  });
});
