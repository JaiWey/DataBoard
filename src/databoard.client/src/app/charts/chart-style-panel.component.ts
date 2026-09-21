import { Component, computed, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { LegendPosition, PieLabel, colorsOf } from './chart-option';
import { ChartStyle, ChartType } from './charts.service';
import { NumberFormatOptions } from './number-format';

const MAX_COLORS = 12;
/** What new custom colours start as, so the first few differ. */
const STARTING_COLORS = ['#1971c2', '#e8590c', '#2b8a3e', '#d6336c', '#5f3dc4', '#f59f00', '#0c8599', '#c92a2a'];

const LEGEND_POSITIONS: { value: LegendPosition; label: string }[] = [
  { value: 'bottom', label: 'Bottom' }, { value: 'top', label: 'Top' }, { value: 'left', label: 'Left' }, { value: 'right', label: 'Right' },
];
const PIE_LABELS: { value: PieLabel; label: string }[] = [
  { value: 'name', label: 'Name' }, { value: 'value', label: 'Value' }, { value: 'percent', label: 'Percent' },
  { value: 'name-value', label: 'Name and value' }, { value: 'name-percent', label: 'Name and percent' },
];

/**
 * The finer style choices of a chart, in sections that only appear for the types they mean something to: titles and
 * axes, colours (your own, and one per series), how numbers are written, how series look, and pie options. It does not
 * change anything itself; it reports what the user chose and the editor stores it.
 */
@Component({
  selector: 'app-chart-style-panel',
  imports: [MatCheckboxModule],
  template: `
    @if (titled()) {
      <details class="section" open>
        <summary>Title{{ axes() ? ' and axes' : '' }}</summary>
        <div class="body">
          <label class="row"><span>Title</span>
            <input type="text" maxlength="100" aria-label="Chart title" [value]="style().title ?? ''" (input)="changed.emit({ title: text($event) })" /></label>
          @if (axes()) {
            <label class="row"><span>Horizontal axis title</span>
              <input type="text" maxlength="60" aria-label="Horizontal axis title" [value]="style().xAxisTitle ?? ''" (input)="changed.emit({ xAxisTitle: text($event) })" /></label>
            <label class="row"><span>Vertical axis title</span>
              <input type="text" maxlength="60" aria-label="Vertical axis title" [value]="style().yAxisTitle ?? ''" (input)="changed.emit({ yAxisTitle: text($event) })" /></label>
            <mat-checkbox [checked]="style().grid !== false" (change)="changed.emit({ grid: $event.checked })">Grid lines</mat-checkbox>
            <label class="row"><span>Axis minimum</span>
              <input type="number" aria-label="Axis minimum" placeholder="auto" [value]="style().yMin ?? ''" [disabled]="style().percent"
                     (input)="changed.emit({ yMin: num($event) })" /></label>
            <label class="row"><span>Axis maximum</span>
              <input type="number" aria-label="Axis maximum" placeholder="auto" [value]="style().yMax ?? ''" [disabled]="style().percent"
                     (input)="changed.emit({ yMax: num($event) })" /></label>
            @if (categoryAxis()) {
              <label class="row"><span>Turn category labels</span>
                <input type="number" min="-90" max="90" step="15" aria-label="Label rotation" [value]="style().labelRotate ?? 0"
                       (input)="changed.emit({ labelRotate: rotation($event) })" /></label>
            }
          }
        </div>
      </details>
    }

    @if (coloured()) {
      <details class="section">
        <summary>Colours</summary>
        <div class="body">
          <div class="swatches" role="group" aria-label="Your colours">
            @for (c of style().colors ?? []; track $index) {
              <span class="swatch">
                <input type="color" [attr.aria-label]="'Colour ' + ($index + 1)" [value]="expand(c)" (input)="setColor($index, $event)" />
                <button type="button" class="x" [attr.aria-label]="'Remove colour ' + ($index + 1)" (click)="removeColor($index)">×</button>
              </span>
            }
            @if ((style().colors ?? []).length < maxColors) {
              <button type="button" class="add" (click)="addColor()">Add colour</button>
            }
          </div>
          <p class="hint">{{ (style().colors ?? []).length > 0 ? 'Used in this order, instead of the palette.' : 'Add your own colours to use instead of the palette.' }}</p>

          @if (seriesNames().length > 0) {
            <div class="series" role="group" aria-label="Colour of each series">
              @for (name of seriesNames(); track name) {
                <span class="series-colour">
                  <input type="color" [attr.aria-label]="'Colour of ' + name" [value]="seriesColor(name)" (input)="setSeriesColor(name, $event)" />
                  <span class="name">{{ name }}</span>
                  @if (style().seriesColors?.[name]) {
                    <button type="button" class="x" [attr.aria-label]="'Reset colour of ' + name" (click)="resetSeriesColor(name)">×</button>
                  }
                </span>
              }
            </div>
          }
        </div>
      </details>
    }

    @if (numeric()) {
      <details class="section">
        <summary>Numbers</summary>
        <div class="body">
          <label class="row"><span>Decimals</span>
            <select aria-label="Decimals" (change)="setFormat({ decimals: decimals($event) })">
              <option value="" [selected]="format().decimals == null">as they are</option>
              @for (d of [0, 1, 2, 3, 4, 5, 6]; track d) { <option [value]="d" [selected]="format().decimals === d">{{ d }}</option> }
            </select></label>
          <mat-checkbox [checked]="format().thousands !== false" (change)="setFormat({ thousands: $event.checked })">Thousands separator</mat-checkbox>
          <mat-checkbox [checked]="format().compact === true" (change)="setFormat({ compact: $event.checked })">Short form (1.2K, 3.4M)</mat-checkbox>
          <label class="row"><span>Prefix</span>
            <input type="text" maxlength="12" aria-label="Number prefix" placeholder="$" [value]="format().prefix ?? ''" (input)="setFormat({ prefix: text($event) })" /></label>
          <label class="row"><span>Suffix</span>
            <input type="text" maxlength="12" aria-label="Number suffix" placeholder="kg" [value]="format().suffix ?? ''" (input)="setFormat({ suffix: text($event) })" /></label>
          <p class="hint">Example: {{ example() }}</p>
        </div>
      </details>
    }

    @if (looks()) {
      <details class="section">
        <summary>Look</summary>
        <div class="body">
          @if (legendable()) {
            <label class="row"><span>Legend position</span>
              <select aria-label="Legend position" (change)="changed.emit({ legendPosition: $any($event.target).value })">
                @for (p of legendPositions; track p.value) { <option [value]="p.value" [selected]="(style().legendPosition ?? 'bottom') === p.value">{{ p.label }}</option> }
              </select></label>
          }
          @if (bars()) {
            <label class="row"><span>Bar width (%)</span>
              <input type="number" min="10" max="100" aria-label="Bar width" placeholder="auto" [value]="style().barWidth ?? ''" (input)="changed.emit({ barWidth: whole($event, 10, 100) })" /></label>
            <label class="row"><span>Rounded corners</span>
              <input type="number" min="0" max="20" aria-label="Bar corner radius" [value]="style().barRadius ?? 0" (input)="changed.emit({ barRadius: whole($event, 0, 20) ?? 0 })" /></label>
          }
          @if (lines()) {
            <mat-checkbox [checked]="style().smooth === true" (change)="changed.emit({ smooth: $event.checked })">Smooth lines</mat-checkbox>
            <mat-checkbox [checked]="style().symbols !== false" (change)="changed.emit({ symbols: $event.checked })">Show points</mat-checkbox>
            <label class="row"><span>Line width</span>
              <input type="number" min="1" max="8" aria-label="Line width" [value]="style().lineWidth ?? 2" (input)="changed.emit({ lineWidth: whole($event, 1, 8) ?? 2 })" /></label>
          }
          @if (area()) {
            <label class="row"><span>Area opacity (%)</span>
              <input type="number" min="0" max="100" aria-label="Area opacity" [value]="style().areaOpacity ?? 70" (input)="changed.emit({ areaOpacity: whole($event, 0, 100) ?? 70 })" /></label>
          }
        </div>
      </details>
    }

    @if (round()) {
      <details class="section">
        <summary>Pie</summary>
        <div class="body">
          <label class="row"><span>Hole size (%)</span>
            <input type="number" min="0" max="90" aria-label="Hole size" [placeholder]="type() === 'Donut' ? '45' : '0'"
                   [value]="style().innerRadius ?? ''" (input)="changed.emit({ innerRadius: whole($event, 0, 90) })" /></label>
          <mat-checkbox [checked]="style().rose === true" (change)="changed.emit({ rose: $event.checked })">Slice radius follows the value</mat-checkbox>
          <label class="row"><span>Slice label</span>
            <select aria-label="Slice label" (change)="changed.emit({ pieLabel: $any($event.target).value })">
              @for (p of pieLabels; track p.value) { <option [value]="p.value" [selected]="(style().pieLabel ?? 'name') === p.value">{{ p.label }}</option> }
            </select></label>
          <p class="hint">Labels show when "Show value labels" is on.</p>
        </div>
      </details>
    }
  `,
  styles: `
    :host { display: block; }
    .section { border-top: 1px solid var(--mat-sys-outline-variant); padding: 0.5rem 0; }
    summary { cursor: pointer; font: var(--mat-sys-title-small); padding: 0.25rem 0; }
    .body { display: flex; flex-direction: column; gap: 0.4rem; padding: 0.4rem 0 0.2rem; }
    .row { display: grid; grid-template-columns: 10rem 1fr; align-items: center; gap: 0.5rem; }
    .row input[type=text], .row input[type=number], .row select { min-width: 0; padding: 0.3rem 0.4rem; font: inherit; }
    .swatches, .series { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .swatch, .series-colour { display: inline-flex; align-items: center; gap: 0.25rem; }
    .series { flex-direction: column; align-items: stretch; }
    input[type=color] { width: 2rem; height: 1.8rem; padding: 0; border: 1px solid var(--mat-sys-outline-variant); border-radius: 0.3rem; background: none; }
    .x { border: 0; background: transparent; cursor: pointer; color: var(--mat-sys-on-surface-variant); font-size: 1.1rem; line-height: 1; }
    .add { border: 1px dashed var(--mat-sys-outline); background: transparent; border-radius: 0.3rem; padding: 0.25rem 0.6rem; cursor: pointer; font: inherit; color: inherit; }
    .hint { margin: 0; font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `,
})
export class ChartStylePanelComponent {
  readonly style = input.required<ChartStyle>();
  readonly type = input.required<ChartType>();
  /** The series (or slices) the chart draws now, so each can have a colour of its own. */
  readonly seriesNames = input<string[]>([]);
  /** The choices that changed; the editor merges them into the style. */
  readonly changed = output<Partial<ChartStyle>>();

  protected readonly maxColors = MAX_COLORS;
  protected readonly legendPositions = LEGEND_POSITIONS;
  protected readonly pieLabels = PIE_LABELS;

  protected readonly round = computed(() => this.type() === 'Pie' || this.type() === 'Donut');
  /** Axis charts: they have a category axis and a value axis. */
  protected readonly axes = computed(() => ['Bar', 'BarHorizontal', 'Line', 'Area', 'Heatmap', 'Scatter'].includes(this.type()));
  protected readonly categoryAxis = computed(() => ['Bar', 'BarHorizontal', 'Line', 'Area', 'Heatmap'].includes(this.type()));
  protected readonly titled = computed(() => this.axes() || this.round());
  protected readonly coloured = computed(() => this.titled());
  protected readonly numeric = computed(() => this.type() !== 'Table');
  protected readonly legendable = computed(() => this.titled() && this.type() !== 'Heatmap');
  protected readonly bars = computed(() => this.type() === 'Bar' || this.type() === 'BarHorizontal');
  protected readonly lines = computed(() => this.type() === 'Line' || this.type() === 'Area');
  protected readonly area = computed(() => this.type() === 'Area');
  protected readonly looks = computed(() => this.legendable() || this.bars() || this.lines());

  protected readonly format = computed<NumberFormatOptions>(() => this.style().numberFormat ?? {});

  /** A number written the chosen way, so the choices can be checked at a glance. */
  protected readonly example = computed(() => this.formatExample());

  private formatExample(): string {
    const f = this.format();
    const options: Intl.NumberFormatOptions = { useGrouping: f.thousands !== false };
    if (f.compact) options.notation = 'compact';
    if (f.decimals !== null && f.decimals !== undefined) {
      options.minimumFractionDigits = f.compact ? 0 : f.decimals;
      options.maximumFractionDigits = f.decimals;
    }
    return `${f.prefix ?? ''}${new Intl.NumberFormat('en-US', options).format(1234567.891)}${f.suffix ?? ''}`;
  }

  // ---- reading inputs -------------------------------------------------------------------------------------

  protected text(event: Event): string | null {
    const value = ((event.target as HTMLInputElement).value ?? '').trim();
    return value === '' ? null : value;
  }

  protected num(event: Event): number | null {
    const raw = (event.target as HTMLInputElement).value;
    if (raw === '') return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
  }

  /** A whole number kept inside the allowed range; null when empty. */
  protected whole(event: Event, min: number, max: number): number | null {
    const n = this.num(event);
    return n === null ? null : Math.min(max, Math.max(min, Math.round(n)));
  }

  protected rotation(event: Event): number {
    return this.whole(event, -90, 90) ?? 0;
  }

  protected decimals(event: Event): number | null {
    const raw = (event.target as HTMLSelectElement).value;
    return raw === '' ? null : Number(raw);
  }

  protected setFormat(changes: Partial<NumberFormatOptions>): void {
    this.changed.emit({ numberFormat: { ...this.format(), ...changes } });
  }

  // ---- colours --------------------------------------------------------------------------------------------

  /** #rgb written as #rrggbb, which is what a colour input takes. */
  protected expand(color: string): string {
    return /^#[0-9a-f]{3}$/i.test(color) ? `#${color[1]}${color[1]}${color[2]}${color[2]}${color[3]}${color[3]}` : color;
  }

  protected addColor(): void {
    const colors = this.style().colors ?? [];
    if (colors.length >= MAX_COLORS) return;
    this.changed.emit({ colors: [...colors, STARTING_COLORS[colors.length % STARTING_COLORS.length]] });
  }

  protected setColor(index: number, event: Event): void {
    const colors = [...(this.style().colors ?? [])];
    colors[index] = (event.target as HTMLInputElement).value;
    this.changed.emit({ colors });
  }

  protected removeColor(index: number): void {
    const colors = (this.style().colors ?? []).filter((_, i) => i !== index);
    this.changed.emit({ colors: colors.length > 0 ? colors : null });
  }

  /** The colour a series has now: its own, or the one the palette or your list gives it by position. */
  protected seriesColor(name: string): string {
    const own = this.style().seriesColors?.[name];
    if (own) return this.expand(own);
    const index = this.seriesNames().indexOf(name);
    const list = colorsOf(this.style()) ?? ['#5470c6', '#91cc75', '#fac858', '#ee6666', '#73c0de', '#3ba272', '#fc8452', '#9a60b4', '#ea7ccc'];
    return this.expand(list[index % list.length]);
  }

  protected setSeriesColor(name: string, event: Event): void {
    this.changed.emit({ seriesColors: { ...(this.style().seriesColors ?? {}), [name]: (event.target as HTMLInputElement).value } });
  }

  protected resetSeriesColor(name: string): void {
    const { [name]: _removed, ...rest } = this.style().seriesColors ?? {};
    this.changed.emit({ seriesColors: Object.keys(rest).length > 0 ? rest : null });
  }
}
