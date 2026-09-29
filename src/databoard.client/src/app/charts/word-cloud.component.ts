import { Component, computed, input } from '@angular/core';
import { ChartStyleOptions, ResultShape, colorsOf } from './chart-option';
import { formatNumber } from './number-format';
import { CLOUD_HEIGHT, CLOUD_WIDTH, layoutWords, wordsOf } from './word-cloud';

/** The colours used when the chart has no palette or colours of its own. */
const CLOUD_COLORS = ['#1971c2', '#e8590c', '#2b8a3e', '#c2255c', '#5f3dc4', '#0c8599', '#e67700', '#364fc7'];

/**
 * A word cloud drawn as SVG: one text per word, sized by its weight. The first result column is the word and the last
 * one the weight. The picture has a fixed size and is scaled to fit, so it looks the same however big the widget is.
 */
@Component({
  selector: 'app-word-cloud',
  template: `
    <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="xMidYMid meet" role="img" [attr.aria-label]="label()">
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
    svg { display: block; width: 100%; height: auto; max-height: 100%; }
    :host(.fill) { height: 100%; }
    :host(.fill) svg { height: 100%; }
    text { cursor: default; }
  `,
  host: { '[class.fill]': 'fill()' },
})
export class WordCloudComponent {
  readonly result = input.required<ResultShape>();
  readonly chartStyle = input<ChartStyleOptions>({});
  readonly label = input('Word cloud');
  readonly fill = input(false);

  protected readonly width = CLOUD_WIDTH;
  protected readonly height = CLOUD_HEIGHT;

  protected readonly family = computed(() => this.chartStyle().labelFont?.family ?? null);
  protected readonly weight = computed(() => this.chartStyle().labelFont?.weight ?? 'bold');

  protected readonly words = computed(() => {
    const style = this.chartStyle();
    const colors = colorsOf(style) ?? CLOUD_COLORS;
    return layoutWords(wordsOf(this.result().rows), style.wordCloud ?? {}).map((w) => ({
      ...w,
      color: style.seriesColors?.[w.text] ?? colors[w.rank % colors.length],
      shown: formatNumber(w.value, style.numberFormat),
    }));
  });
}
