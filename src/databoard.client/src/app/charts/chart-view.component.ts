import { Component, computed, input } from '@angular/core';
import { ChartKind, ChartStyleOptions, ResultShape } from './chart-option';
import { ChartComponent } from './chart.component';
import { DataTableComponent } from './data-table.component';
import { ChartType } from './charts.service';
import { formatNumber } from './number-format';
import { WordCloudComponent } from './word-cloud.component';

/**
 * Shows a query result the way a saved chart wants it: as a table, as one big number, or as an ECharts chart. Used by
 * dashboards, the chart preview card and the chart editor, so a chart looks the same everywhere.
 */
@Component({
  selector: 'app-chart-view',
  imports: [ChartComponent, DataTableComponent, WordCloudComponent],
  template: `
    @if (result().rows.length === 0) {
      <p class="empty"><em>No data.</em></p>
    } @else if (type() === 'Table') {
      <app-data-table [result]="result()" [tableStyle]="chartStyle().table" [numberFormat]="chartStyle().numberFormat" [fill]="fill()" />
    } @else if (type() === 'Number') {
      <div class="number" role="img" [attr.aria-label]="label() + ': ' + number()">
        <span class="value">{{ number() }}</span>
        <span class="caption">{{ numberCaption() }}</span>
      </div>
    } @else if (type() === 'WordCloud') {
      <app-word-cloud [result]="result()" [chartStyle]="chartStyle()" [label]="label()" [fill]="fill()" />
    } @else {
      <app-chart [kind]="kind()" [result]="result()" [chartStyle]="chartStyle()" [dimensions]="dimensions()" [label]="label()" [fill]="fill()" />
    }
  `,
  styles: `
    :host { display: block; }
    .empty { margin: 1rem 0; color: var(--mat-sys-on-surface-variant); }
    .number { display: grid; place-items: center; min-height: 12rem; text-align: center; }
    .value { font: var(--mat-sys-display-medium); color: var(--mat-sys-primary); }
    .caption { color: var(--mat-sys-on-surface-variant); }
    /* On a dashboard the widget decides how tall the view is; a long table scrolls inside it. */
    :host(.fill) { height: 100%; }
    :host(.fill) .number { min-height: 0; height: 100%; }
  `,
  host: { '[class.fill]': 'fill()' },
})
export class ChartViewComponent {
  /** Take the height of the parent instead of a fixed one. */
  readonly fill = input(false);
  readonly type = input.required<ChartType>();
  readonly result = input.required<ResultShape>();
  readonly chartStyle = input<ChartStyleOptions>({});
  /** How many leading result columns are dimensions (the query's dimension count); see `buildChartOption`. */
  readonly dimensions = input<number | undefined>(undefined);
  /** Text alternative announced by screen readers. */
  readonly label = input('Chart');

  /** Table and Number are shown without ECharts; the type is only used when drawing a real chart. */
  protected readonly kind = computed(() => this.type() as ChartKind);

  /** The first row's last column: with no dimension and one metric, that is the value. */
  protected readonly number = computed(() => {
    const row = this.result().rows[0] ?? [];
    const value = row[row.length - 1];
    if (value === null || value === undefined) return '∅';
    const n = Number(value);
    return Number.isFinite(n) ? formatNumber(n, this.chartStyle().numberFormat) : String(value);
  });

  protected readonly numberCaption = computed(() => this.result().columns[this.result().columns.length - 1]?.name ?? '');
}
