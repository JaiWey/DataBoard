import { Component, ElementRef, computed, effect, input, signal, viewChild } from '@angular/core';
import { ChartStyleOptions, ResultShape, colorsOf } from './chart-option';
import { formatNumber } from './number-format';
import { CLOUD_HEIGHT, CLOUD_WIDTH, cloudBounds, cloudSize, layoutWords, wordsOf } from './word-cloud';

/** The colours used when the chart has no palette or colours of its own. */
const CLOUD_COLORS = ['#1971c2', '#e8590c', '#2b8a3e', '#c2255c', '#5f3dc4', '#0c8599', '#e67700', '#364fc7'];

/**
 * A word cloud drawn as SVG: one text per word, sized by its weight. The first result column is the word and the last
 * one the weight. Words are placed in a picture shaped like the widget it fills, and only the part with words is shown,
 * scaled to fit, so there is no empty margin round the cloud however wide or tall the widget is.
 */
@Component({
  selector: 'app-word-cloud',
  template: `
    @if (title(); as heading) { <p class="title">{{ heading }}</p> }
    <svg #svg [attr.viewBox]="viewBox()" preserveAspectRatio="xMidYMid meet" role="img" [attr.aria-label]="title() || label()">
      @for (w of words(); track w.text) {
        <text [attr.x]="w.x" [attr.y]="w.y" [attr.font-size]="w.size" [attr.fill]="w.color"
              [attr.transform]="w.rotate ? 'rotate(-' + w.rotate + ' ' + w.x + ' ' + w.y + ')' : null"
              [attr.font-family]="family()" [attr.font-weight]="weight()" text-anchor="middle" dominant-baseline="central">
          <title>{{ w.text }}: {{ w.shown }}</title>{{ w.text }}
        </text>
      }
    </svg>
  `,
  styles: `
    :host { display: block; }
    .title { margin: 0 0 0.25rem; text-align: center; font: var(--mat-sys-title-small); }
    svg { display: block; width: 100%; height: auto; max-height: 100%; }
    :host(.fill) { height: 100%; display: flex; flex-direction: column; }
    :host(.fill) .title { flex: none; }
    :host(.fill) svg { flex: 1; min-height: 0; height: auto; }
    text { cursor: default; }
  `,
  host: { '[class.fill]': 'fill()' },
})
export class WordCloudComponent {
  readonly result = input.required<ResultShape>();
  readonly chartStyle = input<ChartStyleOptions>({});
  readonly label = input('Word cloud');
  readonly fill = input(false);

  /** How much wider than tall the place for the cloud is, once known (only when it fills its widget). */
  private readonly aspect = signal<number | null>(null);

  private readonly svg = viewChild<ElementRef<SVGSVGElement>>('svg');

  /** The title above the cloud, if the chart has one. */
  protected readonly title = computed(() => this.chartStyle().title?.trim() || null);

  constructor() {
    // The picture is measured, not the widget: a title takes some of the widget's height.
    effect((onCleanup) => {
      const picture = this.svg()?.nativeElement;
      if (!picture || typeof ResizeObserver === 'undefined') return; // no measuring (a test page): the usual shape is used
      const observer = new ResizeObserver(([entry]) => {
        const { width, height } = entry.contentRect;
        // In steps, so a little resizing does not lay the words out again each time.
        if (width > 0 && height > 0) this.aspect.set(Math.round((width / height) * 10) / 10);
      });
      observer.observe(picture);
      onCleanup(() => observer.disconnect());
    });
  }

  protected readonly family = computed(() => this.chartStyle().labelFont?.family ?? null);
  protected readonly weight = computed(() => this.chartStyle().labelFont?.weight ?? 'bold');

  private readonly placed = computed(() => {
    const { width, height } = cloudSize(this.fill() ? this.aspect() : null);
    return layoutWords(wordsOf(this.result().rows), this.chartStyle().wordCloud ?? {}, width, height);
  });

  /** Just the part of the picture the words are in. */
  protected readonly viewBox = computed(() => {
    const bounds = cloudBounds(this.placed());
    return bounds ? `${bounds.x} ${bounds.y} ${bounds.width} ${bounds.height}` : `0 0 ${CLOUD_WIDTH} ${CLOUD_HEIGHT}`;
  });

  protected readonly words = computed(() => {
    const style = this.chartStyle();
    const colors = colorsOf(style) ?? CLOUD_COLORS;
    return this.placed().map((w) => ({
      ...w,
      color: style.seriesColors?.[w.text] ?? colors[w.rank % colors.length],
      shown: formatNumber(w.value, style.numberFormat),
    }));
  });
}
