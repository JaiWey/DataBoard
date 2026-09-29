import { CdkDrag, CdkDragDrop, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { problemMessages } from '../organizations/data-sources.service';
import { DatasetParameter, parameterText, parseParameterValue } from '../organizations/dataset-parameters';
import { ChartResult, Dataset, DatasetColumn, DatasetsService } from '../organizations/datasets.service';
import {
  AGGREGATIONS,
  EditorState,
  FILTER_OPERATORS,
  FilterOperator,
  TIME_GRAINS,
  aggregationsFor,
  defaultZone,
  dimensionFor,
  emptyEditorState,
  fieldBadge,
  filterFor,
  fromChart,
  isAxisType,
  metricFor,
  problemsOf,
  resultNames,
  rowCountMetric,
  toQuery,
  toSaveChart,
} from './chart-editor-model';
import { ChartKind, seriesNamesOf } from './chart-option';
import { ChartStylePanelComponent } from './chart-style-panel.component';
import { ChartViewComponent } from './chart-view.component';
import { ChartTypePickerComponent } from './chart-type-picker.component';
import { ChartType, ChartsService, PALETTE_NAMES } from './charts.service';

type Zone = 'dimensions' | 'metrics' | 'filters';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

/**
 * The chart editor: the dataset's fields on the left, what the chart shows in the middle (Data, Style, Settings), and the
 * chart type and a live preview on the right. Fields are dragged into Dimensions, Metrics and Filters, or clicked.
 */
@Component({
  selector: 'app-chart-editor',
  imports: [CdkDropList, CdkDrag, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatMenuModule, MatTabsModule, ChartStylePanelComponent, ChartTypePickerComponent, ChartViewComponent],
  template: `
    <header class="top">
      <button type="button" class="back" aria-label="Back to dashboards" (click)="back()">←</button>
      <input class="name" aria-label="Chart name" placeholder="Name this chart" [value]="state().name"
             (input)="patch({ name: $any($event.target).value })" />
      <span class="status">
        @if (savedMessage()) { <span class="ok" role="status">{{ savedMessage() }}</span> }
        @else if (dirty()) { Unsaved changes }
      </span>
      <button mat-flat-button type="button" class="save" [disabled]="!canSave()" (click)="save()">Save</button>
    </header>

    @if (!canEdit() && organizationLoaded()) {
      <p class="notice" role="status">You can look at this chart, but changing charts needs Editor access.</p>
    }
    @for (message of saveErrors(); track message) {
      <p class="fail notice" role="alert">{{ message }}</p>
    }

    <div class="editor">
      <aside class="fields" aria-label="Fields">
        <label class="dataset">
          <span>Dataset</span>
          <select aria-label="Dataset" [disabled]="!canEdit()" (change)="chooseDataset($any($event.target).value)">
            <option value="" [selected]="!state().datasetId">Choose a dataset…</option>
            @for (d of datasets(); track d.id) { <option [value]="d.id" [selected]="d.id === state().datasetId">{{ d.name }}</option> }
          </select>
        </label>

        <div cdkDropList id="field-list" class="field-list" cdkDropListSortingDisabled
             [cdkDropListConnectedTo]="['zone-dimensions', 'zone-metrics', 'zone-filters']">
          @for (column of columns(); track column.name) {
            <div cdkDrag [cdkDragData]="column" class="field" [class.measure]="column.role === 'Measure'" [title]="column.dataType">
              <button type="button" class="field-main" [attr.aria-label]="'Add ' + column.name" (click)="addField(defaultZoneOf(column), column)">
                <span class="badge">{{ badge(column) }}</span>
                <span class="field-name">{{ column.name }}</span>
              </button>
              <button type="button" class="field-more" [matMenuTriggerFor]="addMenu" [matMenuTriggerData]="{ column }"
                      [attr.aria-label]="'More ways to add ' + column.name">⋮</button>
            </div>
          } @empty {
            <p class="hint"><em>{{ state().datasetId ? 'This dataset has no fields.' : 'Choose a dataset to see its fields.' }}</em></p>
          }
        </div>
        <mat-menu #addMenu="matMenu">
          <ng-template matMenuContent let-column="column">
            <button mat-menu-item type="button" (click)="addField('dimensions', column)">Add to Dimensions</button>
            <button mat-menu-item type="button" (click)="addField('metrics', column)">Add to Metrics</button>
            <button mat-menu-item type="button" (click)="addField('filters', column)">Add to Filters</button>
          </ng-template>
        </mat-menu>
      </aside>

      <section class="config">
        <mat-tab-group animationDuration="0ms">
          <mat-tab label="Data">
            <div class="tab">
              <h3>Dimensions</h3>
              @if (axisChart()) {
                <p class="zone-hint">The first dimension is the X-axis. Add a second one to draw one series per value, like one line per name.</p>
              }
              <div cdkDropList id="zone-dimensions" class="zone" (cdkDropListDropped)="dropped('dimensions', $event)">
                @for (item of state().dimensions; track $index) {
                  <div cdkDrag class="item dimension">
                    @if (axisChart()) { <span class="role">{{ $index === 0 ? 'X-axis' : 'Series' }}</span> }
                    <span class="item-name">{{ item.column }}</span>
                    @if (isDate(item.dataType)) {
                      <select class="small" [attr.aria-label]="'Group ' + item.column + ' by'" (change)="setGrain($index, $any($event.target).value)">
                        <option value="" [selected]="!item.grain">exact value</option>
                        @for (g of grains; track g) { <option [value]="g" [selected]="g === item.grain">{{ g }}</option> }
                      </select>
                    }
                    <button type="button" class="remove" [attr.aria-label]="'Remove ' + item.column + ' from Dimensions'" (click)="remove('dimensions', $index)">×</button>
                  </div>
                } @empty {
                  <p class="drop-hint">Drag a field here</p>
                }
              </div>

              <h3>Metrics</h3>
              <div cdkDropList id="zone-metrics" class="zone" (cdkDropListDropped)="dropped('metrics', $event)">
                @for (item of state().metrics; track $index) {
                  <div cdkDrag class="item metric">
                    <select class="small" [attr.aria-label]="'Aggregation of metric ' + ($index + 1)" (change)="setAggregation($index, $any($event.target).value)">
                      @for (a of aggregationOptions(item.dataType, item.column); track a.value) {
                        <option [value]="a.value" [selected]="a.value === item.aggregation">{{ a.label }}</option>
                      }
                    </select>
                    <span class="item-name">{{ item.column ?? 'rows' }}</span>
                    <button type="button" class="remove" [attr.aria-label]="'Remove metric ' + ($index + 1)" (click)="remove('metrics', $index)">×</button>
                  </div>
                } @empty {
                  <p class="drop-hint">Drag a field here</p>
                }
              </div>
              <button mat-button type="button" class="add-count" (click)="addRowCount()">Add row count</button>

              <h3>Filters</h3>
              <div cdkDropList id="zone-filters" class="zone" (cdkDropListDropped)="dropped('filters', $event)">
                @for (item of state().filters; track $index) {
                  <div cdkDrag class="item filter">
                    <span class="item-name">{{ item.column }}</span>
                    <select class="small" [attr.aria-label]="'Condition of filter ' + ($index + 1)" (change)="setOperator($index, $any($event.target).value)">
                      @for (op of operators; track op.value) { <option [value]="op.value" [selected]="op.value === item.operator">{{ op.label }}</option> }
                    </select>
                    @if (needsValue(item.operator)) {
                      <input class="value" [attr.aria-label]="'Value of filter ' + ($index + 1)" [value]="item.value"
                             [placeholder]="listOperator(item.operator) ? 'a, b, c' : 'value'" (input)="setFilterValue($index, $any($event.target).value)" />
                    }
                    <button type="button" class="remove" [attr.aria-label]="'Remove filter ' + ($index + 1)" (click)="remove('filters', $index)">×</button>
                  </div>
                } @empty {
                  <p class="drop-hint">Drag a field here</p>
                }
              </div>

              @if (datasetParameters().length > 0) {
                <h3>Parameters</h3>
                <p class="zone-hint">This dataset takes values. Leave one empty to use its default.</p>
                <div class="params">
                  @for (parameter of datasetParameters(); track parameter.name) {
                    <label class="param">
                      <span>{{ parameterLabel(parameter) }}</span>
                      @if (parameter.type === 'Boolean') {
                        <select [attr.aria-label]="'Value of ' + parameter.name" [disabled]="!canEdit()" (change)="setParameter(parameter, $any($event.target).value)">
                          <option value="" [selected]="parameterValue(parameter) === ''">default ({{ defaultText(parameter) }})</option>
                          <option value="true" [selected]="parameterValue(parameter) === 'true'">true</option>
                          <option value="false" [selected]="parameterValue(parameter) === 'false'">false</option>
                        </select>
                      } @else {
                        <input [attr.aria-label]="'Value of ' + parameter.name" [disabled]="!canEdit()"
                               [type]="parameter.type === 'Number' ? 'number' : parameter.type === 'Date' ? 'date' : 'text'"
                               [step]="parameter.type === 'Number' ? 'any' : null"
                               [placeholder]="'default: ' + defaultText(parameter)" [value]="parameterValue(parameter)"
                               (input)="setParameter(parameter, $any($event.target).value)" />
                      }
                    </label>
                  }
                </div>
              }
            </div>
          </mat-tab>

          <mat-tab label="Style">
            <div class="tab">
              <mat-checkbox [checked]="state().style.legend" (change)="patchStyle({ legend: $event.checked })">Show legend</mat-checkbox>
              <mat-checkbox [checked]="state().style.labels" (change)="patchStyle({ labels: $event.checked })">Show value labels</mat-checkbox>
              <mat-checkbox [checked]="state().style.stack" [disabled]="!axisChart()"
                            (change)="patchStyle($event.checked ? { stack: true } : { stack: false, percent: false })">Stack series</mat-checkbox>
              <mat-checkbox [checked]="state().style.percent" [disabled]="!axisChart()"
                            (change)="patchStyle($event.checked ? { stack: true, percent: true } : { percent: false })">Show as 100% (shares)</mat-checkbox>
              <label class="row">
                <span>Colours</span>
                <select aria-label="Colour palette" (change)="patchStyle({ palette: $any($event.target).value })">
                  @for (p of palettes; track p) { <option [value]="p" [selected]="p === state().style.palette">{{ p }}</option> }
                </select>
              </label>
              <app-chart-style-panel [style]="state().style" [type]="state().type" [seriesNames]="seriesNames()" (changed)="patchStyle($event)" />
            </div>
          </mat-tab>

          <mat-tab label="Settings">
            <div class="tab">
              <label class="row">
                <span>Row limit</span>
                <input type="number" min="1" max="1000" aria-label="Row limit" placeholder="100" [value]="state().limit ?? ''"
                       (input)="setLimit($any($event.target).value)" />
              </label>
              @if (axisChart() && state().dimensions.length >= 2) {
                <label class="row">
                  <span>Series limit</span>
                  <input type="number" min="1" max="100" aria-label="Series limit" placeholder="All series" [value]="state().seriesLimit ?? ''"
                         (input)="setSeriesLimit($any($event.target).value)" />
                </label>
                <p class="zone-hint">Keeps the series with the largest total of the first metric.</p>
              }
              <label class="row">
                <span>Sort by</span>
                <select aria-label="Sort by" (change)="patch({ sortBy: $any($event.target).value })">
                  <option value="" [selected]="!state().sortBy">No sorting</option>
                  @for (name of sortOptions(); track name) { <option [value]="name" [selected]="name === state().sortBy">{{ name }}</option> }
                </select>
              </label>
              <label class="row">
                <span>Direction</span>
                <select aria-label="Sort direction" [disabled]="!state().sortBy" (change)="patch({ sortDirection: $any($event.target).value })">
                  <option value="Desc" [selected]="state().sortDirection === 'Desc'">Highest first</option>
                  <option value="Asc" [selected]="state().sortDirection === 'Asc'">Lowest first</option>
                </select>
              </label>
              <label class="row">
                <span>Description</span>
                <input aria-label="Description" [value]="state().description" (input)="patch({ description: $any($event.target).value })" />
              </label>
            </div>
          </mat-tab>
        </mat-tab-group>
      </section>

      <section class="preview">
        <app-chart-type-picker [value]="state().type" (pick)="patch({ type: $event })" />

        <div class="preview-box">
          <mat-button-toggle-group class="views" aria-label="Preview" [value]="view()" (change)="view.set($event.value)">
            <mat-button-toggle value="chart">Chart</mat-button-toggle>
            <mat-button-toggle value="table">Table</mat-button-toggle>
            @if (result()?.sql) { <mat-button-toggle value="sql">SQL</mat-button-toggle> }
          </mat-button-toggle-group>

          @for (message of previewErrors(); track message) { <p class="fail" role="alert">{{ message }}</p> }
          @if (problems().length > 0) {
            <ul class="problems">@for (p of problems(); track p) { <li>{{ p }}</li> }</ul>
          } @else if (result(); as r) {
            @switch (view()) {
              @case ('sql') { <pre class="sql">{{ r.sql }}</pre> }
              @case ('table') { <app-chart-view type="Table" [result]="r" label="Result table" /> }
              @default { <app-chart-view [type]="state().type" [result]="r" [chartStyle]="state().style"
                                              [dimensions]="state().dimensions.length" [label]="state().name || 'Chart preview'" /> }
            }
            @if (r.truncated) { <p class="hint"><small>Showing the first {{ r.rows.length }} rows.</small></p> }
          } @else if (previewing()) {
            <p class="hint"><em>Loading…</em></p>
          }
        </div>
      </section>
    </div>
  `,
  styles: `
    :host { display: block; background: #fff; border-radius: 12px; overflow: hidden; }
    .top { display: flex; align-items: center; gap: 0.75rem; padding: 0.6rem 1rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .back { border: 0; background: none; font-size: 1.5rem; cursor: pointer; padding: 0 0.5rem; color: inherit; }
    .name { flex: 1; max-width: 28rem; border: 0; font: var(--mat-sys-title-large); padding: 0.25rem 0.4rem; background: transparent; }
    .name:focus { outline: 2px solid var(--mat-sys-primary); border-radius: 4px; }
    .status { margin-left: auto; color: var(--mat-sys-on-surface-variant); }
    .notice { margin: 0.5rem 1rem; }
    .editor { display: grid; grid-template-columns: 16rem 24rem 1fr; height: calc(100vh - 15rem); min-height: 28rem; }
    .fields, .config, .preview { min-height: 0; overflow: auto; padding: 0.75rem 1rem; }
    .fields, .config { border-right: 1px solid var(--mat-sys-outline-variant); }
    .dataset { display: grid; gap: 0.25rem; margin-bottom: 0.5rem; font: var(--mat-sys-label-medium); }
    select, input.value, .row input { font: inherit; padding: 0.35rem 0.4rem; border: 1px solid var(--mat-sys-outline); border-radius: 4px; background: #fff; }
    .field-list { display: grid; gap: 0.15rem; }
    .field { display: flex; align-items: center; border-radius: 4px; background: var(--mat-sys-surface-container-low); }
    .field:hover { background: var(--mat-sys-surface-container); }
    .field-main { flex: 1; display: flex; align-items: center; gap: 0.5rem; border: 0; background: none; padding: 0.4rem 0.5rem; cursor: grab; text-align: left; font: inherit; color: inherit; }
    .field-more { border: 0; background: none; cursor: pointer; padding: 0.3rem 0.5rem; font-size: 1.1rem; color: inherit; }
    .badge { min-width: 2.2rem; font: var(--mat-sys-label-small); color: var(--mat-sys-primary); }
    .measure .badge { color: #2b8a3e; }
    .tab { padding: 0.5rem 0; display: grid; gap: 0.5rem; }
    h3 { margin: 0.5rem 0 0; font: var(--mat-sys-title-small); }
    .config { overflow-x: hidden; }
    mat-tab-group { min-width: 0; }
    .zone { min-width: 0; min-height: 3.2rem; display: grid; gap: 0.35rem; padding: 0.4rem; background: var(--mat-sys-surface-container); border-radius: 6px; align-content: start; }
    .zone.cdk-drop-list-dragging, .zone.cdk-drop-list-receiving { outline: 2px dashed var(--mat-sys-primary); }
    .zone-hint { margin: 0; font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .params { display: grid; gap: 0.5rem; }
    .param { display: grid; grid-template-columns: 9rem 1fr; align-items: center; gap: 0.5rem; }
    .param input, .param select { padding: 0.3rem; min-width: 0; }
    .role { font: var(--mat-sys-label-small); color: var(--mat-sys-primary); min-width: 3.2rem; }
    .drop-hint { margin: 0.6rem 0; text-align: center; color: var(--mat-sys-on-surface-variant); }
    .item { min-width: 0; display: flex; align-items: center; gap: 0.4rem; padding: 0.3rem 0.5rem; background: #fff; border: 1px solid var(--mat-sys-outline-variant); border-radius: 4px; }
    .item-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .small { font-size: 0.85rem; max-width: 7.5rem; min-width: 0; }
    .value { flex: 1; min-width: 0; width: 100%; }
    .remove { border: 0; background: none; cursor: pointer; font-size: 1.1rem; color: inherit; }
    .row { display: grid; gap: 0.25rem; font: var(--mat-sys-label-medium); }
    .row input { font: var(--mat-sys-body-medium); }
    .preview-box { padding-top: 0.75rem; }
    .views { margin-bottom: 0.5rem; }
    .problems { margin: 0.5rem 0; padding-left: 1.2rem; color: var(--mat-sys-on-surface-variant); }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .sql { white-space: pre-wrap; background: var(--mat-sys-surface-container); padding: 0.75rem; border-radius: 6px; }
    @media (max-width: 70rem) {
      .editor { grid-template-columns: 1fr; height: auto; }
      .fields, .config { border-right: 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    }
  `,
})
export class ChartEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly chartsApi = inject(ChartsService);

  protected orgId = '';
  /** Null until the chart has been saved once. */
  protected readonly chartId = signal<string | null>(null);
  protected readonly organization = signal<Organization | null>(null);
  protected readonly organizationLoaded = signal(false);
  protected readonly canEdit = computed(() => this.organization()?.role === 'Editor' || this.organization()?.role === 'Owner');

  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly state = signal<EditorState>(emptyEditorState());
  private readonly savedJson = signal<string>('');
  protected readonly dataset = computed(() => this.datasets().find((d) => d.id === this.state().datasetId));
  protected readonly columns = computed<DatasetColumn[]>(() => this.dataset()?.columns ?? []);
  /** The values the dataset's SQL takes; a chart can choose them, and what it leaves alone uses the default. */
  protected readonly datasetParameters = computed<DatasetParameter[]>(() => this.dataset()?.parameters ?? []);

  protected readonly palettes = PALETTE_NAMES;
  protected readonly operators = FILTER_OPERATORS;
  protected readonly grains = TIME_GRAINS;

  /** Charts with an X axis (not a table, number or round chart): the ones where a second dimension makes series. */
  protected readonly axisChart = computed(() => isAxisType(this.state().type));
  /** What the preview colours (its series or slices), so each can be given a colour of its own. */
  protected readonly seriesNames = computed(() => {
    const r = this.result();
    const type = this.state().type;
    if (!r || type === 'Table' || type === 'Number' || type === 'WordCloud') return [];
    return seriesNamesOf(type as ChartKind, r, this.state().dimensions.length);
  });
  protected readonly problems = computed(() => problemsOf(this.state()));
  protected readonly dirty = computed(() => JSON.stringify(toSaveChart(this.state())) !== this.savedJson());
  protected readonly canSave = computed(() => this.canEdit() && !this.saving() && problemsOf(this.state(), { forSave: true }).length === 0);
  protected readonly sortOptions = computed(() => {
    const names = resultNames(this.state());
    return [...names.dimensions, ...names.metrics];
  });

  protected readonly view = signal<'chart' | 'table' | 'sql'>('chart');
  protected readonly result = signal<ChartResult | null>(null);
  protected readonly previewErrors = signal<string[]>([]);
  protected readonly previewing = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveErrors = signal<string[]>([]);
  protected readonly savedMessage = signal('');

  private previewTimer: ReturnType<typeof setTimeout> | undefined;
  private previewRequest: Subscription | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.previewTimer);
      this.previewRequest?.unsubscribe();
    });

    // Whenever what the chart shows changes, look again (a moment after the last change).
    effect(() => {
      const state = this.state();
      untracked(() => this.schedulePreview(state));
    });
  }

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    const param = this.route.snapshot.paramMap.get('chartId');
    const existing = param && param !== 'new' ? param : null;
    this.chartId.set(existing);

    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => {
        this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null);
        this.organizationLoaded.set(true);
      },
    });

    this.datasetsApi.list(this.orgId).subscribe({
      next: (items) => {
        this.datasets.set(items);
        if (existing) {
          this.chartsApi.get(this.orgId, existing).subscribe({
            next: (chart) => this.load(fromChart(chart, items.find((d) => d.id === chart.datasetId))),
            error: (e) => this.saveErrors.set(problemMessages(e, 'Could not load the chart.')),
          });
        } else {
          const wanted = this.route.snapshot.queryParamMap.get('dataset');
          const first = items.find((d) => d.id === wanted) ?? items[0];
          this.load(emptyEditorState(first?.id ?? ''));
        }
      },
      error: (e) => this.saveErrors.set(problemMessages(e, 'Could not load the datasets.')),
    });
  }

  /** Puts a chart in the editor as the starting point: nothing is unsaved yet. */
  private load(state: EditorState): void {
    this.state.set(state);
    this.savedJson.set(JSON.stringify(toSaveChart(state)));
  }

  // ---- what the template asks ---------------------------------------------------------------------------

  protected badge(column: DatasetColumn): string {
    return fieldBadge(column);
  }

  protected defaultZoneOf(column: DatasetColumn): Zone {
    return defaultZone(column);
  }

  protected isDate(dataType: string): boolean {
    return /^(date|timestamp)/i.test(dataType);
  }

  protected aggregationOptions(dataType: string, column: string | null) {
    const role = this.columns().find((c) => c.name === column)?.role ?? null;
    const allowed = aggregationsFor(column === null ? null : dataType, role);
    return AGGREGATIONS.filter((a) => allowed.includes(a.value));
  }

  protected needsValue(operator: FilterOperator): boolean {
    return operator !== 'IsNull' && operator !== 'IsNotNull';
  }

  protected listOperator(operator: FilterOperator): boolean {
    return operator === 'In' || operator === 'NotIn' || operator === 'Between';
  }

  // ---- editing --------------------------------------------------------------------------------------------

  protected patch(changes: Partial<EditorState>): void {
    this.savedMessage.set('');
    this.state.update((s) => ({ ...s, ...changes }));
  }

  protected patchStyle(changes: Partial<EditorState['style']>): void {
    this.patch({ style: { ...this.state().style, ...changes } });
  }

  /** Another dataset has other fields, so everything chosen from the old one is cleared. */
  protected chooseDataset(id: string): void {
    if (id === this.state().datasetId) return;
    // Parameters belong to a dataset, so the values chosen for the old one do not carry over.
    this.patch({ datasetId: id, dimensions: [], metrics: [], filters: [], sortBy: '', parameters: {} });
  }

  /** The value chosen for a parameter, as text for its input (empty: the default is used). */
  protected parameterValue(parameter: DatasetParameter): string {
    return parameterText(this.state().parameters[parameter.name] as string | number | boolean | null | undefined);
  }

  /** Choosing nothing puts the parameter back to its default; a value that is not valid for the type is not kept. */
  protected setParameter(parameter: DatasetParameter, text: string): void {
    const value = parseParameterValue(parameter.type, text);
    if (value === null && !(parameter.name in this.state().parameters)) return; // nothing was chosen, nothing changes
    const rest = { ...this.state().parameters };
    if (value === null) delete rest[parameter.name];
    else rest[parameter.name] = value;
    this.patch({ parameters: rest });
  }

  protected defaultText(parameter: DatasetParameter): string {
    return parameterText(parameter.default) || 'none';
  }

  protected parameterLabel(parameter: DatasetParameter): string {
    return parameter.label || parameter.name;
  }

  protected addField(zone: Zone, column: DatasetColumn): void {
    const s = this.state();
    switch (zone) {
      case 'dimensions':
        return this.patch({ dimensions: [...s.dimensions, dimensionFor(column)] });
      case 'metrics':
        return this.patch({ metrics: [...s.metrics, metricFor(column)] });
      case 'filters':
        return this.patch({ filters: [...s.filters, filterFor(column)] });
    }
  }

  protected addRowCount(): void {
    this.patch({ metrics: [...this.state().metrics, rowCountMetric()] });
  }

  /** A field dragged from the list is added; an item dragged within its own zone is moved. */
  protected dropped(zone: Zone, event: CdkDragDrop<unknown>): void {
    if (event.previousContainer === event.container) {
      const items = [...this.state()[zone]];
      moveItemInArray(items, event.previousIndex, event.currentIndex);
      this.patch({ [zone]: items } as Partial<EditorState>);
      return;
    }
    const column = event.item.data as DatasetColumn | undefined;
    if (column) this.addField(zone, column);
  }

  protected remove(zone: Zone, index: number): void {
    const items = this.state()[zone].filter((_, i) => i !== index);
    const changes: Partial<EditorState> = { [zone]: items };
    // A sort on something that is no longer in the result is dropped with it.
    const next = { ...this.state(), ...changes } as EditorState;
    const names = resultNames(next);
    if (this.state().sortBy && ![...names.dimensions, ...names.metrics].includes(this.state().sortBy)) changes.sortBy = '';
    this.patch(changes);
  }

  protected setGrain(index: number, value: string): void {
    this.patch({ dimensions: this.state().dimensions.map((d, i) => (i === index ? { ...d, grain: value as never } : d)) });
  }

  protected setAggregation(index: number, value: string): void {
    this.patch({ metrics: this.state().metrics.map((m, i) => (i === index ? { ...m, aggregation: value as never } : m)) });
  }

  protected setOperator(index: number, value: string): void {
    this.patch({ filters: this.state().filters.map((f, i) => (i === index ? { ...f, operator: value as FilterOperator } : f)) });
  }

  protected setFilterValue(index: number, value: string): void {
    this.patch({ filters: this.state().filters.map((f, i) => (i === index ? { ...f, value } : f)) });
  }

  protected setSeriesLimit(text: string): void {
    this.patch({ seriesLimit: text.trim() === '' ? null : Number(text) });
  }

  protected setLimit(text: string): void {
    this.patch({ limit: text.trim() === '' ? null : Number(text) });
  }

  // ---- the preview ----------------------------------------------------------------------------------------

  private schedulePreview(state: EditorState): void {
    clearTimeout(this.previewTimer);
    this.previewRequest?.unsubscribe();
    this.previewErrors.set([]);

    if (problemsOf(state).length > 0) {
      this.result.set(null);
      this.previewing.set(false);
      return;
    }
    this.previewing.set(true);
    this.previewTimer = setTimeout(() => {
      this.previewRequest = this.datasetsApi.query(this.orgId, state.datasetId, toQuery(state)).subscribe({
        next: (r) => {
          this.previewing.set(false);
          this.result.set(r);
          if (this.view() === 'sql' && !r.sql) this.view.set('chart');
        },
        error: (e) => {
          this.previewing.set(false);
          this.result.set(null);
          this.previewErrors.set(problemMessages(e, 'The preview failed.'));
        },
      });
    }, 300);
  }

  // ---- saving and leaving ---------------------------------------------------------------------------------

  protected save(): void {
    if (!this.canSave()) return;
    const state = this.state();
    const payload = toSaveChart(state);
    const id = this.chartId();
    this.saving.set(true);
    this.saveErrors.set([]);

    // A new chart goes in the folder it was started from (?folder=...); an existing one stays where it is.
    const folder = this.route.snapshot.queryParamMap.get('folder');
    const request = id
      ? this.chartsApi.update(this.orgId, id, payload)
      : this.chartsApi.create(this.orgId, folder ? { ...payload, folderId: folder } : payload);
    request.subscribe({
      next: (chart) => {
        this.saving.set(false);
        this.savedJson.set(JSON.stringify(payload));
        this.savedMessage.set('Saved');
        if (!id) {
          this.chartId.set(chart.id);
          // From now on this address names the chart, so a reload or a shared link opens it.
          void this.router.navigate(['/organizations', this.orgId, 'charts', chart.id], { replaceUrl: true });
        }
      },
      error: (e) => {
        this.saving.set(false);
        this.saveErrors.set(problemMessages(e, 'Could not save the chart.'));
      },
    });
  }

  protected back(): void {
    if (this.dirty() && !confirm('This chart has unsaved changes. Leave without saving?')) return;
    const id = this.chartId();
    void this.router.navigate(['/organizations', this.orgId, 'dashboards'], { queryParams: id ? { view: 'charts', chart: id } : { view: 'charts' } });
  }
}