import { Component, computed, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AxisOptions, LabelPosition, TableAlign, TableOptions, TableSize, LegendPosition, LegendType, LineType, MarginOptions, SplitLineOptions, colorsOf, pieParts } from './chart-option';
import { ChartStyle, ChartType } from './charts.service';
import { NumberFormatOptions } from './number-format';
import { ColorFieldComponent, FontFieldsComponent } from './style-fields.component';

const MAX_COLORS = 12;
/** What new custom colours start as, so the first few differ. */
const STARTING_COLORS = ['#1971c2', '#e8590c', '#2b8a3e', '#d6336c', '#5f3dc4', '#f59f00', '#0c8599', '#c92a2a'];

const LEGEND_POSITIONS: { value: LegendPosition; label: string }[] = [
  { value: 'bottom', label: 'Bottom' }, { value: 'top', label: 'Top' }, { value: 'left', label: 'Left' }, { value: 'right', label: 'Right' },
];
const LINE_TYPES: { value: LineType; label: string }[] = [{ value: 'solid', label: 'Solid' }, { value: 'dashed', label: 'Dashed' }, { value: 'dotted', label: 'Dotted' }];
const LABEL_POSITIONS: { value: LabelPosition; label: string }[] = [
  { value: 'auto', label: 'Automatic' }, { value: 'top', label: 'Top' }, { value: 'bottom', label: 'Bottom' }, { value: 'left', label: 'Left' },
  { value: 'right', label: 'Right' }, { value: 'inside', label: 'Inside' }, { value: 'insideTop', label: 'Inside top' }, { value: 'insideBottom', label: 'Inside bottom' },
];
const LEGEND_TYPES: { value: LegendType; label: string }[] = [{ value: 'auto', label: 'Automatic' }, { value: 'scroll', label: 'Scroll' }, { value: 'plain', label: 'Plain' }];

const PIE_POSITIONS: { value: LabelPosition; label: string }[] = [
  { value: 'auto', label: 'Automatic' }, { value: 'outside', label: 'Outside' }, { value: 'inside', label: 'Inside' }, { value: 'center', label: 'Centre' },
];

/**
 * The finer style choices of a chart, in sections that only appear for the types they mean something to: titles and
 * axes, colours (your own, and one per series), how numbers are written, how series look, and pie options. It does not
 * change anything itself; it reports what the user chose and the editor stores it.
 */
@Component({
  selector: 'app-chart-style-panel',
  imports: [MatCheckboxModule, ColorFieldComponent, FontFieldsComponent],
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

    @if (isTable()) {
      <details class="section">
        <summary>Table</summary>
        <div class="body">
          <mat-checkbox [checked]="tableOf().fixedHeader !== false" (change)="patchTable({ fixedHeader: $event.checked })">Fixed header</mat-checkbox>
          <mat-checkbox [checked]="tableOf().border !== false" (change)="patchTable({ border: $event.checked })">Show border</mat-checkbox>
          <mat-checkbox [checked]="tableOf().rowNumber === true" (change)="patchTable({ rowNumber: $event.checked })">Row numbers</mat-checkbox>
          <mat-checkbox [checked]="tableOf().sortable !== false" (change)="patchTable({ sortable: $event.checked })">Sort by clicking a header</mat-checkbox>
          <label class="row"><span>Size</span>
            <select aria-label="Table size" (change)="patchTable({ size: $any($event.target).value })">
              @for (s of tableSizes; track s.value) { <option [value]="s.value" [selected]="(tableOf().size ?? 'small') === s.value">{{ s.label }}</option> }
            </select></label>
          <mat-checkbox [checked]="tableOf().pageSize != null" (change)="patchTable({ pageSize: $event.checked ? 20 : null })">Pages</mat-checkbox>
          @if (tableOf().pageSize != null) {
            <label class="row"><span>Rows per page</span>
              <input type="number" min="5" max="500" aria-label="Rows per page" [value]="tableOf().pageSize ?? ''"
                     (input)="patchTable({ pageSize: whole($event, 5, 500) ?? 20 })" /></label>
          }
        </div>
      </details>

      <details class="section">
        <summary>Header</summary>
        <div class="body">
          <div class="row"><span>Background</span>
            <app-color-field label="Header background" fallback="#f1f3f5" [value]="tableOf().headerBackground" (changed)="patchTable({ headerBackground: $event })" /></div>
          <div class="row"><span>Font</span>
            <app-font-fields label="Header font" [font]="tableOf().headerFont" (changed)="patchTable({ headerFont: $event })" /></div>
          <label class="row"><span>Align</span>
            <select aria-label="Header align" (change)="patchTable({ headerAlign: $any($event.target).value })">
              @for (a of tableAligns; track a.value) { <option [value]="a.value" [selected]="(tableOf().headerAlign ?? 'left') === a.value">{{ a.label }}</option> }
            </select></label>
        </div>
      </details>

      <details class="section">
        <summary>Body</summary>
        <div class="body">
          <div class="row"><span>Odd row text</span>
            <app-color-field label="Odd row text colour" [value]="tableOf().oddRowColor" (changed)="patchTable({ oddRowColor: $event })" /></div>
          <div class="row"><span>Odd row background</span>
            <app-color-field label="Odd row background" fallback="#ffffff" [value]="tableOf().oddRowBackground" (changed)="patchTable({ oddRowBackground: $event })" /></div>
          <div class="row"><span>Even row text</span>
            <app-color-field label="Even row text colour" [value]="tableOf().evenRowColor" (changed)="patchTable({ evenRowColor: $event })" /></div>
          <div class="row"><span>Even row background</span>
            <app-color-field label="Even row background" fallback="#f8f9fa" [value]="tableOf().evenRowBackground" (changed)="patchTable({ evenRowBackground: $event })" /></div>
          <div class="row"><span>Font</span>
            <app-font-fields label="Table font" [font]="tableOf().bodyFont" (changed)="patchTable({ bodyFont: $event })" /></div>
        </div>
      </details>
    }
    @if (axes()) {
      @for (axis of axisKeys; track axis) {
        <details class="section">
          <summary>{{ axis === 'x' ? 'X axis' : 'Y axis' }}</summary>
          <div class="body">
            <mat-checkbox [checked]="axisOf(axis).show !== false" (change)="patchAxis(axis, { show: $event.checked })">Show the axis</mat-checkbox>
            <mat-checkbox [checked]="axisOf(axis).line !== false" [disabled]="axisOf(axis).show === false" (change)="patchAxis(axis, { line: $event.checked })">Axis line</mat-checkbox>
            <mat-checkbox [checked]="axisOf(axis).ticks !== false" [disabled]="axisOf(axis).show === false" (change)="patchAxis(axis, { ticks: $event.checked })">Tick marks</mat-checkbox>
            <mat-checkbox [checked]="axisOf(axis).labels !== false" [disabled]="axisOf(axis).show === false" (change)="patchAxis(axis, { labels: $event.checked })">Labels</mat-checkbox>
            <div class="row"><span>Line colour</span>
              <app-color-field [label]="(axis === 'x' ? 'X' : 'Y') + ' axis line colour'" [value]="axisOf(axis).lineColor" (changed)="patchAxis(axis, { lineColor: $event })" /></div>
            <div class="row"><span>Label font</span>
              <app-font-fields [label]="(axis === 'x' ? 'X' : 'Y') + ' axis label font'" [font]="axisOf(axis).labelFont" (changed)="patchAxis(axis, { labelFont: $event })" /></div>
            <div class="row"><span>Title font</span>
              <app-font-fields [label]="(axis === 'x' ? 'X' : 'Y') + ' axis title font'" [font]="axisOf(axis).titleFont" (changed)="patchAxis(axis, { titleFont: $event })" /></div>
          </div>
        </details>
      }

      <details class="section">
        <summary>Split lines</summary>
        <div class="body">
          @for (axis of axisKeys; track axis) {
            <mat-checkbox [checked]="splitOf(axis).show ?? usualSplit(axis)" (change)="patchSplit(axis, { show: $event.checked })">
              {{ axis === 'x' ? 'X' : 'Y' }} axis lines
            </mat-checkbox>
            <div class="row"><span>Colour</span>
              <app-color-field [label]="(axis === 'x' ? 'X' : 'Y') + ' split line colour'" fallback="#dee2e6" [value]="splitOf(axis).color" (changed)="patchSplit(axis, { color: $event })" /></div>
            <label class="row"><span>Width</span>
              <input type="number" min="1" max="10" [attr.aria-label]="(axis === 'x' ? 'X' : 'Y') + ' split line width'" placeholder="1" [value]="splitOf(axis).width ?? ''"
                     (input)="patchSplit(axis, { width: whole($event, 1, 10) })" /></label>
            <label class="row"><span>Style</span>
              <select [attr.aria-label]="(axis === 'x' ? 'X' : 'Y') + ' split line style'" (change)="patchSplit(axis, { lineType: lineTypeOf($event) })">
                <option value="" [selected]="!splitOf(axis).lineType">Default</option>
                @for (t of lineTypes; track t.value) { <option [value]="t.value" [selected]="splitOf(axis).lineType === t.value">{{ t.label }}</option> }
              </select></label>
          }
        </div>
      </details>

    }

    @if (axes() || round()) {
      <details class="section">
        <summary>Margin</summary>
        <div class="body">
          <label class="row"><span>Unit</span>
            <select aria-label="Margin unit" (change)="patchMarginUnit($any($event.target).value)">
              <option value="px" [selected]="marginUnit() === 'px'">Pixels</option>
              <option value="%" [selected]="marginUnit() === '%'">Percent</option>
            </select></label>
          @if (axes()) {
            <mat-checkbox [checked]="style().margin?.containLabel !== false" (change)="patchMargin('containLabel', $event.checked)">Contain label</mat-checkbox>
          }
          @for (side of sides; track side) {
            <label class="row"><span>{{ side[0].toUpperCase() + side.slice(1) }} ({{ marginUnit() }})</span>
              <input type="number" min="0" [max]="marginMax()" [attr.aria-label]="side + ' margin'" placeholder="auto" [value]="marginValue(side)"
                     (input)="patchMarginSide(side, $event)" /></label>
          }
        </div>
      </details>
    }

    @if (bars()) {
      <details class="section">
        <summary>Bars</summary>
        <div class="body">
          <label class="row"><span>Bar width (%)</span>
            <input type="number" min="10" max="100" aria-label="Bar width" placeholder="auto" [value]="style().barWidth ?? ''" (input)="changed.emit({ barWidth: whole($event, 10, 100) })" /></label>
          <label class="row"><span>Rounded corners</span>
            <input type="number" min="0" max="20" aria-label="Bar corner radius" [value]="style().barRadius ?? 0" (input)="changed.emit({ barRadius: whole($event, 0, 20) ?? 0 })" /></label>
          <label class="row"><span>Border style</span>
            <select aria-label="Bar border style" (change)="changed.emit({ barBorderStyle: $any($event.target).value })">
              @for (t of lineTypes; track t.value) { <option [value]="t.value" [selected]="(style().barBorderStyle ?? 'solid') === t.value">{{ t.label }}</option> }
            </select></label>
          <label class="row"><span>Border width</span>
            <input type="number" min="0" max="10" aria-label="Bar border width" [value]="style().barBorderWidth ?? 0" (input)="changed.emit({ barBorderWidth: whole($event, 0, 10) ?? 0 })" /></label>
          <div class="row"><span>Border colour</span>
            <app-color-field label="Bar border colour" [value]="style().barBorderColor" (changed)="changed.emit({ barBorderColor: $event })" /></div>
          <label class="row"><span>Gap in a group (%)</span>
            <input type="number" min="0" max="100" aria-label="Bar gap" placeholder="auto" [value]="style().barGap ?? ''" (input)="changed.emit({ barGap: whole($event, 0, 100) })" /></label>
          <label class="row"><span>Gap between groups (%)</span>
            <input type="number" min="0" max="100" aria-label="Group gap" placeholder="auto" [value]="style().barCategoryGap ?? ''" (input)="changed.emit({ barCategoryGap: whole($event, 0, 100) })" /></label>
        </div>
      </details>
    }

    @if (titled()) {
      <details class="section">
        <summary>Labels</summary>
        <div class="body">
          <label class="row"><span>Position</span>
            <select aria-label="Label position" (change)="changed.emit({ labelPosition: $any($event.target).value })">
              @for (p of positionChoices(); track p.value) { <option [value]="p.value" [selected]="(style().labelPosition ?? 'auto') === p.value">{{ p.label }}</option> }
            </select></label>
          @if (round()) {
            <mat-checkbox [checked]="parts().name" (change)="patchParts({ name: $event.checked })">Show name</mat-checkbox>
            <mat-checkbox [checked]="parts().value" (change)="patchParts({ value: $event.checked })">Show value</mat-checkbox>
            <mat-checkbox [checked]="parts().percent" (change)="patchParts({ percent: $event.checked })">Show percentage</mat-checkbox>
            <mat-checkbox [checked]="style().pieForceLabels === true" (change)="changed.emit({ pieForceLabels: $event.checked })">Force display of all labels</mat-checkbox>
          }
          <div class="row"><span>Font</span>
            <app-font-fields label="Label font" [font]="style().labelFont" (changed)="changed.emit({ labelFont: $event })" /></div>
          <p class="hint">Labels show when "Show value labels" is on.</p>
        </div>
      </details>
    }

    @if (legendable()) {
      <details class="section">
        <summary>Legend</summary>
        <div class="body">
          <label class="row"><span>Position</span>
            <select aria-label="Legend position" (change)="changed.emit({ legendPosition: $any($event.target).value })">
              @for (p of legendPositions; track p.value) { <option [value]="p.value" [selected]="(style().legendPosition ?? 'bottom') === p.value">{{ p.label }}</option> }
            </select></label>
          <label class="row"><span>Type</span>
            <select aria-label="Legend type" (change)="changed.emit({ legendType: $any($event.target).value })">
              @for (t of legendTypes; track t.value) { <option [value]="t.value" [selected]="(style().legendType ?? 'auto') === t.value">{{ t.label }}</option> }
            </select></label>
          <mat-checkbox [checked]="style().legendSelector === true" (change)="changed.emit({ legendSelector: $event.checked })">Select all and invert buttons</mat-checkbox>
          <label class="row"><span>Height (px)</span>
            <input type="number" min="20" max="1000" aria-label="Legend height" placeholder="auto" [value]="style().legendHeight ?? ''"
                   (input)="changed.emit({ legendHeight: whole($event, 20, 1000) })" /></label>
          <div class="row"><span>Font</span>
            <app-font-fields label="Legend font" [font]="style().legendFont" (changed)="changed.emit({ legendFont: $event })" /></div>
        </div>
      </details>
    }

    @if (lines()) {
      <details class="section">
        <summary>Look</summary>
        <div class="body">
          <mat-checkbox [checked]="style().smooth === true" (change)="changed.emit({ smooth: $event.checked })">Smooth lines</mat-checkbox>
          <mat-checkbox [checked]="style().symbols !== false" (change)="changed.emit({ symbols: $event.checked })">Show points</mat-checkbox>
          <label class="row"><span>Line width</span>
            <input type="number" min="1" max="8" aria-label="Line width" [value]="style().lineWidth ?? 2" (input)="changed.emit({ lineWidth: whole($event, 1, 8) ?? 2 })" /></label>
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
  protected readonly lineTypes = LINE_TYPES;
  protected readonly labelPositions = LABEL_POSITIONS;
  protected readonly legendTypes = LEGEND_TYPES;
  protected readonly axisKeys: ('x' | 'y')[] = ['x', 'y'];
  protected readonly sides: (keyof MarginOptions)[] = ['left', 'top', 'right', 'bottom'];
  protected readonly positionChoices = computed(() => (this.round() ? PIE_POSITIONS : LABEL_POSITIONS));

  protected readonly isTable = computed(() => this.type() === 'Table');
  protected readonly tableOf = computed<TableOptions>(() => this.style().table ?? {});
  protected readonly tableSizes: { value: TableSize; label: string }[] = [{ value: 'small', label: 'Small' }, { value: 'medium', label: 'Medium' }, { value: 'large', label: 'Large' }];
  protected readonly tableAligns: { value: TableAlign; label: string }[] = [{ value: 'left', label: 'Left' }, { value: 'center', label: 'Centre' }, { value: 'right', label: 'Right' }];

  protected patchTable(changes: Partial<TableOptions>): void {
    this.changed.emit({ table: { ...this.tableOf(), ...changes } });
  }

  protected readonly round = computed(() => this.type() === 'Pie' || this.type() === 'Donut');
  /** Axis charts: they have a category axis and a value axis. */
  protected readonly axes = computed(() => ['Bar', 'BarHorizontal', 'Line', 'Area', 'Heatmap', 'Scatter'].includes(this.type()));
  protected readonly categoryAxis = computed(() => ['Bar', 'BarHorizontal', 'Line', 'Area', 'Heatmap'].includes(this.type()));
  protected readonly titled = computed(() => this.axes() || this.round());
  protected readonly coloured = computed(() => this.titled());
  protected readonly legendable = computed(() => this.titled() && this.type() !== 'Heatmap');
  protected readonly bars = computed(() => this.type() === 'Bar' || this.type() === 'BarHorizontal');
  protected readonly lines = computed(() => this.type() === 'Line' || this.type() === 'Area');
  protected readonly area = computed(() => this.type() === 'Area');

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

  // ---- axes, split lines and margin ---

  protected axisOf(axis: 'x' | 'y'): AxisOptions {
    return (axis === 'x' ? this.style().xAxis : this.style().yAxis) ?? {};
  }

  protected patchAxis(axis: 'x' | 'y', changes: Partial<AxisOptions>): void {
    const next = { ...this.axisOf(axis), ...changes };
    this.changed.emit(axis === 'x' ? { xAxis: next } : { yAxis: next });
  }

  protected splitOf(axis: 'x' | 'y'): SplitLineOptions {
    return (axis === 'x' ? this.style().xSplitLine : this.style().ySplitLine) ?? {};
  }

  protected patchSplit(axis: 'x' | 'y', changes: Partial<SplitLineOptions>): void {
    const next = { ...this.splitOf(axis), ...changes };
    this.changed.emit(axis === 'x' ? { xSplitLine: next } : { ySplitLine: next });
  }

  /** Whether the lines of an axis show when nothing was chosen: only a value axis has them (unless Grid lines is off). */
  protected usualSplit(axis: 'x' | 'y'): boolean {
    const type = this.type();
    const valueAxis = type === 'Scatter' || (axis === 'x' ? type === 'BarHorizontal' : type === 'Bar' || type === 'Line' || type === 'Area');
    return valueAxis && this.style().grid !== false;
  }

  protected lineTypeOf(event: Event): LineType | null {
    return (this.text(event) as LineType | null);
  }

  protected readonly marginUnit = computed(() => this.style().margin?.unit ?? 'px');
  /** The most a side can be: 300 pixels, or half of the chart. */
  protected readonly marginMax = computed(() => (this.marginUnit() === '%' ? 50 : 300));

  protected marginValue(side: keyof MarginOptions): number | string {
    const value = this.style().margin?.[side];
    return typeof value === 'number' ? value : '';
  }

  protected patchMarginSide(side: keyof MarginOptions, event: Event): void {
    this.patchMargin(side, this.whole(event, 0, this.marginMax()));
  }

  protected patchMargin(key: keyof MarginOptions, value: number | boolean | null): void {
    this.changed.emit({ margin: { ...(this.style().margin ?? {}), [key]: value } });
  }

  /** Another unit keeps the sides that fit it and brings the others down to the most it allows. */
  protected patchMarginUnit(unit: 'px' | '%'): void {
    const most = unit === '%' ? 50 : 300;
    const margin = { ...(this.style().margin ?? {}), unit };
    for (const side of this.sides) {
      const value = margin[side];
      if (typeof value === 'number' && value > most) (margin as Record<string, unknown>)[side] = most;
    }
    this.changed.emit({ margin });
  }

  // ---- a pie's label ---

  protected readonly parts = computed(() => pieParts(this.style()));

  /** Writes all three choices, so the older single choice no longer decides. */
  protected patchParts(changes: Partial<{ name: boolean; value: boolean; percent: boolean }>): void {
    const next = { ...this.parts(), ...changes };
    this.changed.emit({ pieShowName: next.name, pieShowValue: next.value, pieShowPercent: next.percent });
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