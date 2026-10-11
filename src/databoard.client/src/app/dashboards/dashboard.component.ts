import { Component, OnInit, WritableSignal, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ChartViewComponent } from '../charts/chart-view.component';
import { WidgetMenuComponent } from './widget-menu.component';
import { downloadJson } from '../charts/export';
import { Observable } from 'rxjs';
import { Chart, ChartsService } from '../charts/charts.service';
import { problemMessages } from '../organizations/data-sources.service';
import { AddChartsDialogComponent, AddChartsDialogData } from './add-charts-dialog.component';
import { BackgroundDialogComponent, BackgroundDialogData } from './background-dialog.component';
import { BackgroundLayerComponent } from './background-layer.component';
import { MarkdownComponent } from '../shared/markdown.component';
import { DashboardTabBarComponent } from './dashboard-tab-bar.component';
import { MoveToTabComponent } from './move-to-tab.component';
import { TabNameDialogComponent, TabNameDialogData } from './tab-name-dialog.component';
import { arrangeTab, placeAll, tabOf } from './dashboard-tabs';
import { ImageWidgetComponent } from './image-widget.component';
import { ImageWidgetDialogComponent, ImageWidgetDialogData } from './image-widget-dialog.component';
import { TextWidgetDialogComponent, TextWidgetDialogData } from './text-widget-dialog.component';
import { DashboardGridComponent } from './dashboard-grid.component';
import { AccessDialogComponent, AccessDialogData } from './access-dialog.component';
import { CopyDashboardDialogComponent, CopyDashboardDialogData } from './copy-dashboard-dialog.component';
import { ShareDialogComponent, ShareDialogData } from './share-dialog.component';
import { OrgContext } from '../organizations/org-context.service';
import { REFRESH_CHOICES, ago, intervalLabel } from '../shared/relative-time';
import { autoReload, clock, reloadChoices, reloadLabel } from '../shared/auto-reload';
import { LayoutItem, sameLayout } from './dashboard-layout';
import { Dataset, DatasetsService } from '../organizations/datasets.service';
import {
  Dashboard,
  DashboardTab,
  DashboardDefinition,
  DashboardFilter,
  DashboardRun,
  DashboardWidget,
  DashboardsService,
  FilterTarget,
  FilterValues,
  NEW_ID,
  SHARED_FILTER_OPERATORS,
  SharedFilterOperator,
  WidgetResult,
} from './dashboards.service';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { IconComponent } from '../shared/icon.component';
import { ConfirmService } from '../shared/confirm.service';

/** In and Between take several values, typed as a comma-separated list; the rest take one. */
export function parseFilterInput(operator: SharedFilterOperator, text: string): string[] {
  const trimmed = text.trim();
  if (!trimmed) return [];
  return operator === 'In' || operator === 'Between'
    ? trimmed.split(',').map((v) => v.trim()).filter((v) => v.length > 0)
    : [trimmed];
}

/** How many moves and resizes can be undone in one go. */
const MAX_UNDO = 100;

@Component({
  selector: 'app-dashboard',
  host: { '(document:keydown)': 'onKey($event)' },
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatMenuModule, MatButtonModule, MatTooltipModule, IconComponent, AppSelectComponent, ChartViewComponent, DashboardGridComponent, WidgetMenuComponent, BackgroundLayerComponent, ImageWidgetComponent, MarkdownComponent, DashboardTabBarComponent, MoveToTabComponent],
  template: `
    <mat-card appearance="raised" class="detail">
    @if (dashboard(); as d) {
      <mat-card-header>
        <mat-card-title>{{ d.name }}</mat-card-title>
        <span class="actions no-print">
          @if (d.definition.widgets.length > 0 && !editingLayout()) {
            <button mat-icon-button type="button" class="print" aria-label="Print / PDF" matTooltip="Print / PDF" (click)="print()"><app-icon name="print" /></button>
          }
          @if (canEdit()) {
            @if (previewing()) {
              <span class="preview-note" role="status">Preview: as people who can only view it see it</span>
              <button mat-icon-button type="button" class="exit-preview" aria-label="Back to editing" matTooltip="Back to editing" (click)="previewing.set(false)"><app-icon name="edit" /></button>
            } @else if (editingLayout()) {
              <button mat-icon-button type="button" class="undo-layout" aria-label="Undo" matTooltip="Undo (Ctrl+Z)" [disabled]="busy() || past().length === 0" (click)="undo()"><app-icon name="undo" /></button>
              <button mat-icon-button type="button" class="redo-layout" aria-label="Redo" matTooltip="Redo (Ctrl+Y)" [disabled]="busy() || future().length === 0" (click)="redo()"><app-icon name="redo" /></button>
              <button mat-icon-button type="button" class="save-layout" aria-label="Save layout" matTooltip="Save layout" [disabled]="busy() || !layoutChanged()" (click)="saveLayout(d)"><app-icon name="save" /></button>
              <button mat-icon-button type="button" class="cancel-layout" aria-label="Cancel" matTooltip="Cancel" [disabled]="busy()" (click)="cancelLayout()"><app-icon name="close" /></button>
            } @else {
              @if (d.definition.widgets.length > 0) {
                <button mat-icon-button type="button" class="edit-layout" aria-label="Edit layout" matTooltip="Edit layout" (click)="editLayout()"><app-icon name="dashboard" /></button>
              }
              <button mat-icon-button type="button" class="preview" aria-label="Preview" matTooltip="Preview as a viewer" (click)="previewing.set(true)"><app-icon name="eye" /></button>
              <button mat-icon-button type="button" class="refresh" aria-label="Refresh data" matTooltip="Refresh data" [disabled]="busy()" (click)="refresh()"><app-icon name="refresh" /></button>
              <button mat-icon-button type="button" class="share" aria-label="Share" matTooltip="Share" (click)="share(d)"><app-icon name="share" /></button>
              <button mat-icon-button type="button" class="access" [class.limited]="d.restricted" [attr.aria-label]="d.restricted ? 'Access (limited)' : 'Access'"
                      [matTooltip]="d.restricted ? 'Who can see it (limited)' : 'Who can see it'" (click)="editAccess(d)"><app-icon [name]="d.restricted ? 'lock' : 'lockOpen'" /></button>
              <button mat-icon-button type="button" class="duplicate" aria-label="Duplicate" matTooltip="Duplicate" [disabled]="busy()" (click)="duplicate(d)"><app-icon name="copy" /></button>
              @if (copyTargets().length > 0) {
                <button mat-icon-button type="button" class="copy-to" aria-label="Copy to another organization" matTooltip="Copy to another organization" [disabled]="busy()" (click)="copyToOrganization(d)"><app-icon name="copyTo" /></button>
              }
              <button mat-icon-button type="button" class="export" aria-label="Export" matTooltip="Export to a file" [disabled]="busy()" (click)="exportFile(d)"><app-icon name="download" /></button>
              <button mat-icon-button type="button" class="add-menu" aria-label="Add to the dashboard" matTooltip="Add a chart, picture or text" [matMenuTriggerFor]="addMenu"><app-icon name="add" /></button>
              <mat-menu #addMenu="matMenu">
                <button mat-menu-item type="button" class="add-chart" (click)="addCharts(d)">Chart</button>
                <button mat-menu-item type="button" class="add-picture" (click)="addPicture(d)">Picture</button>
                <button mat-menu-item type="button" class="add-text" (click)="addText(d)">Text</button>
              </mat-menu>
              <button mat-icon-button type="button" class="background" aria-label="Background" matTooltip="Background picture" (click)="editBackground(d)"><app-icon name="background" /></button>
              <button mat-icon-button type="button" class="delete-dashboard" aria-label="Delete dashboard" matTooltip="Delete dashboard" (click)="removeDashboard(d)"><app-icon name="delete" /></button>
            }
          }
        </span>
      </mat-card-header>
      <mat-card-content>
      @if (d.background; as background) {
        <app-background-layer [load]="loadBackground(d, background.version)" [version]="background.version" [fit]="background.fit" [opacity]="background.opacity" />
      }

      @if (editable() || d.definition.reloadSeconds) {
        <div class="reload-bar no-print">
          @if (editable()) {
            <span class="field">
              <span>Reload page every</span>
              <app-select ariaLabel="Reload interval" [options]="reloadOptions(d.definition.reloadSeconds)" [value]="'' + (d.definition.reloadSeconds ?? '')"
                          [disabled]="busy()" (valueChange)="setReload(d, $event)" />
            </span>
          }
          @if (d.definition.reloadSeconds) {
            <small class="status">
              @if (!editable()) { Reloads every {{ reloadText(d.definition.reloadSeconds) }}. }
              @if (loadedAt(); as at) { Updated {{ at }}. }
            </small>
          }
        </div>
      }

      @if (d.refresh; as schedule) {
        @if (schedule.intervalMinutes || editable()) {
          <div class="schedule no-print">
            @if (editable()) {
              <span class="field">
                <span>Refresh every</span>
                <app-select ariaLabel="Refresh schedule" [options]="refreshOptions(schedule.intervalMinutes)" [value]="'' + (schedule.intervalMinutes ?? '')"
                            [disabled]="busy()" (valueChange)="setSchedule(d, $event)" />
              </span>
            }
            @if (schedule.intervalMinutes) {
              <small class="status">
                @if (!editable()) { Refreshed every {{ intervalText(schedule.intervalMinutes) }}. }
                @if (schedule.lastUtc; as last) { Last refresh {{ age(last) }}. } @else { Not refreshed yet. }
              </small>
              @if (schedule.lastFailures > 0) {
                <small class="fail" role="alert">
                  {{ schedule.lastFailures }} {{ schedule.lastFailures === 1 ? 'chart' : 'charts' }} could not be refreshed@if (schedule.lastMessage) {: {{ schedule.lastMessage }}}
                </small>
              }
            }
          </div>
        }
      }

      @if (d.definition.filters.length > 0) {
        <form class="filters form-row no-print" [formGroup]="filterForm" (ngSubmit)="apply()">
          @for (filter of d.definition.filters; track filter.id) {
            <mat-form-field>
              <mat-label>{{ filter.label }} ({{ operatorLabel(filter.operator) }})</mat-label>
              <input matInput [formControlName]="filter.id" [attr.aria-label]="filter.label" />
              @if (filter.operator === 'In' || filter.operator === 'Between') {
                <mat-hint>Separate values with commas</mat-hint>
              }
            </mat-form-field>
            @if (editable()) {
              <button mat-button type="button" class="remove-filter" (click)="removeFilter(d, filter)">Remove {{ filter.label }}</button>
            }
          }
          <button mat-flat-button type="submit" class="apply" [disabled]="busy()">Apply filters</button>
          <button mat-button type="button" class="apply" (click)="clearFilters()">Clear</button>
        </form>
      }

      @if (d.definition.widgets.length === 0) {
        <p><em>This dashboard has no charts yet.</em>
          @if (editable()) { Use <strong>Add</strong> and then <strong>Chart</strong> to put charts on it, or <a [routerLink]="['/organizations', orgId(), 'charts', 'new']">create a new chart</a>. }
        </p>
      }

      <app-dashboard-tab-bar [tabs]="tabs()" [activeId]="activeTabId()" [canEdit]="editable()" [editing]="editingLayout()"
                             (select)="selectTab($event)" (add)="addTab(d)" (rename)="renameTab(d, $event)" (remove)="removeTab(d, $event)"
                             (moveEarlier)="moveTab(d, $event, -1)" (moveLater)="moveTab(d, $event, 1)" />

      <app-dashboard-grid [items]="items()" [editing]="editingLayout()" [labels]="labels()" (layoutBegin)="beginChange()" (layoutChange)="draft.set($event)">
        <ng-template let-id>
          @if (widgetById(id); as widget) {
          @if (widget.kind === 'Image') {
            <mat-card appearance="outlined" class="widget picture">
              <app-image-widget [load]="loadWidgetImage(d, widget)" [version]="widget.imageKey ?? ''" [fit]="widget.imageFit ?? 'contain'"
                                [alt]="widget.alt" [linkUrl]="widget.linkUrl" />
              @if (editable() && !editingLayout()) {
                <span class="picture-actions no-print">
                  <app-move-to-tab [tabs]="tabs()" [current]="activeTabId()" (pick)="moveWidget(d, widget, $event)" />
                  <button mat-stroked-button type="button" class="edit-picture" (click)="editPicture(d, widget)">Edit picture</button>
                  <button mat-stroked-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                </span>
              }
            </mat-card>
          } @else if (widget.kind === 'Text') {
            <mat-card appearance="outlined" class="widget words">
              <app-markdown class="words-text" [text]="widget.text ?? ''" [align]="widget.textAlign ?? 'left'" [size]="widget.textSize ?? 'normal'" />
              @if (editable() && !editingLayout()) {
                <span class="picture-actions no-print">
                  <app-move-to-tab [tabs]="tabs()" [current]="activeTabId()" (pick)="moveWidget(d, widget, $event)" />
                  <button mat-stroked-button type="button" class="edit-text" (click)="editText(d, widget)">Edit text</button>
                  <button mat-stroked-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                </span>
              }
            </mat-card>
          } @else {
          <mat-card appearance="outlined" class="widget" data-export-scope>
            @if (chartFor(widget.chartId); as chart) {
              <mat-card-title class="sr-only">{{ chart.name }}</mat-card-title>
              @if (!editingLayout()) {
                <div class="widget-actions no-print">
                  <app-widget-menu [name]="chart.name" [result]="resultFor(widget.id)?.result ?? null" [type]="chart.type" [canEdit]="editable()"
                                   [orgId]="orgId()" [chartId]="chart.id" [tabs]="tabs()" [current]="activeTabId()"
                                   (pick)="moveWidget(d, widget, $event)" (remove)="removeWidget(d, widget.id)" />
                </div>
              }
              <mat-card-content>
                @if (resultFor(widget.id); as r) {
                  @if (r.error) {
                    <p class="fail" role="alert">{{ r.error }}</p>
                  } @else if (r.result; as result) {
                    <app-chart-view [fill]="true" [type]="chart.type" [result]="result" [chartStyle]="chart.definition.style"
                                    [dimensions]="chart.definition.query.dimensions?.length ?? 0" [label]="chart.name" />
                    @if (result.truncated || result.cachedUtc) {
                      <p class="note">
                        <small>
                          @if (result.truncated) { Showing the first {{ result.rows.length }} rows. }
                          @if (result.cachedUtc; as stored) { <span class="cached">Data from {{ age(stored) }}.</span> }
                        </small>
                      </p>
                    }
                  }
                } @else {
                  <p><em>Loading...</em></p>
                }
              </mat-card-content>
            } @else {
              <mat-card-header>
                <mat-card-title>Chart not found</mat-card-title>
                @if (editable() && !editingLayout()) {
                  <span class="widget-actions no-print">
                    <button mat-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                  </span>
                }
              </mat-card-header>
              <mat-card-content><p class="fail" role="alert">This chart was deleted.</p></mat-card-content>
            }
          </mat-card>
          }
          }
        </ng-template>
      </app-dashboard-grid>

      @if (editable()) {
        <h3 class="no-print">Add a shared filter</h3>
        <form class="form-row no-print" [formGroup]="newFilterForm" (ngSubmit)="addFilter(d)">
          <mat-form-field>
            <mat-label>Label</mat-label>
            <input matInput formControlName="label" aria-label="Filter label" />
          </mat-form-field>
          <app-select label="Applies to" ariaLabel="Filter applies to" formControlName="target" [options]="targetKinds()" (valueChange)="setTarget($any($event))" />
          <app-select [label]="target() === 'Parameter' ? 'Parameter' : 'Column'" ariaLabel="Filter column" formControlName="column" [options]="targetOptions()"
                      [hint]="target() === 'Parameter' ? 'Sets the value for every chart whose dataset has this parameter' : 'Applies to every chart whose dataset has this column'" />
          @if (target() === 'Column') {
            <app-select label="Condition" ariaLabel="Filter condition" formControlName="operator" [options]="operators" />
          }
          <button mat-flat-button type="submit" class="apply" [disabled]="newFilterForm.invalid || busy()">Add filter</button>
        </form>
      }
      </mat-card-content>
    } @else if (errors().length === 0) {
      <mat-card-content><p><em>Loading...</em></p></mat-card-content>
    }

    @if (errors().length > 0) {
      <mat-card-content>
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-card-content>
    }
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    /* The card fills the pane; only its content scrolls, so a long dashboard never grows the page. */
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; flex-wrap: wrap; align-items: center; gap: 0.1rem; }
    .preview-note { font: var(--mat-sys-label-medium); color: var(--mat-sys-primary); padding: 0 0.5rem; }
    .actions .limited { color: var(--mat-sys-primary); }
    .actions .save-layout:not([disabled]) { color: var(--mat-sys-primary); }
    .actions .delete-dashboard { color: var(--mat-sys-error); }
    /* The chart's name is not shown; its menu floats over the top right corner of the widget. */
    .widget-actions { position: absolute; top: 0.25rem; right: 0.25rem; z-index: 2; display: flex; background: rgb(255 255 255 / 0.8); border-radius: 50%; }
    /* The background picture is a layer under everything in the content, so the content makes its own stacking context. */
    .detail > mat-card-content { flex: 1; min-height: 0; overflow: auto; padding-top: 0.75rem; position: relative; isolation: isolate; }
    .filters { margin: 0.5rem 0 1rem; }
    .schedule, .reload-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem 0.75rem; margin: 0 0 0.75rem; }
    .schedule .field, .reload-bar .field { display: flex; align-items: center; gap: 0.5rem; }
    .apply, .remove-filter { margin-top: 0.4rem; }
    /* A widget fills its cell on the grid; its chart takes what the header leaves, and a table scrolls inside. */
    .widget { height: 100%; position: relative; }
    .widget mat-card-header { align-items: center; }
    .widget mat-card-content { padding-top: 0.75rem; }
    /* A chart's name is not drawn on the dashboard; it stays for screen readers (and is the name of the chart's picture). */
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
    /* A picture fills its widget; its buttons float over a corner, out of the way until the widget is hovered or focused. */
    .widget.picture, .widget.words { position: relative; overflow: hidden; padding: 0; }
    .words-text { display: block; height: 100%; overflow: auto; padding: 0.75rem 1rem; box-sizing: border-box; }
    .picture-actions { position: absolute; top: 0.4rem; right: 0.4rem; display: flex; gap: 0.3rem; opacity: 0; transition: opacity 0.12s; background: rgb(255 255 255 / 0.85); border-radius: 0.5rem; padding: 0.2rem; }
    .widget.picture:hover .picture-actions, .widget.picture:focus-within .picture-actions,
    .widget.words:hover .picture-actions, .widget.words:focus-within .picture-actions { opacity: 1; }
    @media (hover: none) { .picture-actions { opacity: 1; } }
    .widget mat-card-content { flex: 1; min-height: 0; overflow: hidden; display: flex; flex-direction: column; padding-bottom: 0.5rem; }
    .widget app-chart-view { flex: 1; min-height: 0; }
    .note { margin: 0.25rem 0 0; }
    /* The grid is one column on a narrow screen, so there is nothing to arrange. */
    @media (max-width: 640px) { .edit-layout { display: none; } }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(DashboardsService);
  private readonly confirms = inject(ConfirmService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly chartsApi = inject(ChartsService);
  private readonly dialog = inject(MatDialog);
  private readonly orgs = inject(OrgContext);
  private readonly fb = inject(FormBuilder);

  readonly orgId = input.required<string>();
  readonly dashboardId = input.required<string>();
  /** True while the layout is being arranged, so the page can give the dashboard the room (it folds its list away). */
  readonly editingChange = output<boolean>();
  /** Whether the person may change the dashboard (their role). */
  readonly canEdit = input(false);
  /** Showing the dashboard as someone who can only view it sees it: nothing to change, nothing saved. */
  protected readonly previewing = signal(false);
  /** What the page offers to change: the role allows it and it is not a preview. */
  protected readonly editable = computed(() => this.canEdit() && !this.previewing());

  /** The dashboard after it was saved (a chart or filter was added or removed), so the page can update its list. */
  readonly changed = output<Dashboard>();
  readonly deleted = output<string>();
  /** A copy was made, so the page can add it to its list and show it. */
  readonly duplicated = output<Dashboard>();

  protected readonly operators = SHARED_FILTER_OPERATORS;

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly charts = signal<Chart[]>([]);
  protected readonly run = signal<DashboardRun | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly tabs = computed(() => this.dashboard()?.definition.tabs ?? []);
  private readonly chosenTab = signal<string | null>(null);
  /** The tab on show: the one picked if it still exists, else the first. Null when the dashboard has no tabs. */
  protected readonly activeTabId = computed(() => {
    const tabs = this.tabs();
    return tabs.length === 0 ? null : tabs.some((t) => t.id === this.chosenTab()) ? this.chosenTab() : tabs[0].id;
  });

  /** True while the editor is arranging widgets; the changes live in `draft` until they are saved. */
  protected readonly editingLayout = signal(false);
  /** When the charts were last loaded, for "Updated 14:03:15". */
  protected readonly loadedAt = signal('');
  protected readonly reloadChoices = reloadChoices;
  protected readonly reloadText = reloadLabel;
  private readonly reloading = autoReload(() => this.dashboard()?.definition.reloadSeconds, () => this.execute(false, true), () => this.busy() || this.editingLayout());
  protected readonly draft = signal<LayoutItem[] | null>(null);
  /** The layouts Undo goes back to (the latest last), and the ones Redo goes forward to. Both only live while arranging. */
  protected readonly past = signal<LayoutItem[][]>([]);
  protected readonly future = signal<LayoutItem[][]>([]);
  /** Where each widget sits as saved, with places made up for widgets that have none yet. */
  private readonly savedLayout = computed(() => arrangeTab(this.dashboard()?.definition.widgets ?? [], this.tabs(), this.activeTabId()));
  protected readonly items = computed(() => this.draft() ?? this.savedLayout());
  protected readonly layoutChanged = computed(() => !sameLayout(this.items(), this.savedLayout()));
  protected readonly labels = computed(() => {
    const names: Record<string, string> = {};
    for (const w of this.dashboard()?.definition.widgets ?? []) {
      names[w.id] = w.kind === 'Image' ? w.alt || 'picture' : w.kind === 'Text' ? 'text' : (this.chartFor(w.chartId)?.name ?? 'missing chart');
    }
    return names;
  });

  protected filterForm: FormGroup<Record<string, FormControl<string>>> = this.fb.nonNullable.group({});
  protected readonly newFilterForm = this.fb.nonNullable.group({
    label: ['', [Validators.required, Validators.maxLength(200)]],
    column: ['', Validators.required],
    operator: ['In' as SharedFilterOperator],
    target: ['Column' as FilterTarget],
  });
  /** What the filter being made acts on. A parameter sets one value, so its condition is always "is". */
  protected readonly target = signal<FilterTarget>('Column');

  protected readonly columnNames = computed(() =>
    [...new Set(this.datasets().flatMap((d) => d.columns.map((c) => c.name)))].sort((a, b) => a.localeCompare(b)),
  );
  protected readonly parameterNames = computed(() =>
    [...new Set(this.datasets().flatMap((d) => (d.parameters ?? []).map((p) => p.name)))].sort((a, b) => a.localeCompare(b)),
  );
  protected readonly targetKinds = computed<SelectOption[]>(() => [
    { value: 'Column', label: 'A column' },
    { value: 'Parameter', label: 'A dataset parameter', disabled: this.parameterNames().length === 0 },
  ]);
  protected readonly targetOptions = computed<SelectOption[]>(() => this.targetNames().map((name) => ({ value: name, label: name })));
  protected readonly targetNames = computed(() => (this.target() === 'Parameter' ? this.parameterNames() : this.columnNames()));

  protected setTarget(target: FilterTarget): void {
    this.target.set(target);
    const controls = this.newFilterForm.controls;
    controls.column.setValue(this.targetNames()[0] ?? '');
    controls.operator.setValue(target === 'Parameter' ? 'Eq' : 'In');
  }

  constructor() {
    effect(() => this.editingChange.emit(this.editingLayout()));
  }

  ngOnInit(): void {
    this.chartsApi.list(this.orgId()).subscribe({ next: (items) => this.charts.set(items) });
    this.datasetsApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.datasets.set(items);
        if (!this.newFilterForm.controls.column.value && this.columnNames().length > 0) {
          this.newFilterForm.controls.column.setValue(this.columnNames()[0]);
        }
      },
    });
    this.api.get(this.orgId(), this.dashboardId()).subscribe({
      next: (d) => {
        this.show(d);
        this.execute();
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the dashboard.')),
    });
  }

  protected operatorLabel(operator: SharedFilterOperator): string {
    return this.operators.find((o) => o.value === operator)?.label ?? operator;
  }

  protected chartFor(id: string): Chart | undefined {
    return this.charts().find((c) => c.id === id);
  }

  protected widgetById(id: string): DashboardWidget | undefined {
    return this.dashboard()?.definition.widgets.find((w) => w.id === id);
  }

  /** Gets the picture behind the dashboard; the layer calls it again whenever the version changes. */
  protected loadBackground(d: Dashboard, version: string): () => Observable<Blob> {
    return () => this.api.backgroundImage(this.orgId(), d.id, version);
  }

  protected editBackground(d: Dashboard): void {
    this.dialog
      .open<BackgroundDialogComponent, BackgroundDialogData, Dashboard>(BackgroundDialogComponent, {
        data: { orgId: this.orgId(), dashboardId: d.id, dashboardName: d.name, background: d.background ?? null },
        width: '30rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        // Only the look changed: the rest of the dashboard on screen is still right.
        if (saved) this.dashboard.update((current) => (current ? { ...current, background: saved.background ?? null } : current));
      });
  }

  /** Public links: make one (and copy it) or revoke the ones that exist. */
  /** Who may see this dashboard. Afterwards it is read again, so the lock and the page agree with what was saved. */
  protected editAccess(d: Dashboard): void {
    this.dialog
      .open<AccessDialogComponent, AccessDialogData, boolean>(AccessDialogComponent, {
        data: { orgId: this.orgId(), kind: 'dashboard', id: d.id, name: d.name },
        width: '34rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.api.get(this.orgId(), d.id).subscribe({
          next: (fresh) => {
            this.show(fresh);
            this.changed.emit(fresh);
          },
          // Limited to people who no longer include this one: the dashboard is gone from their view.
          error: () => this.deleted.emit(d.id),
        });
      });
  }

  /** The organizations a copy can go to: where the person is an Editor or Owner, other than this one. */
  protected readonly copyTargets = computed(() =>
    this.orgs.organizations().filter((o) => o.id !== this.orgId() && o.role !== 'Viewer').map((o) => ({ id: o.id, name: o.name })));

  protected copyToOrganization(d: Dashboard): void {
    this.dialog.open<CopyDashboardDialogComponent, CopyDashboardDialogData>(CopyDashboardDialogComponent, {
      data: { orgId: this.orgId(), dashboardId: d.id, dashboardName: d.name, targets: this.copyTargets() },
      width: '32rem',
      maxWidth: '95vw',
    });
  }

  protected share(d: Dashboard): void {
    this.dialog.open<ShareDialogComponent, ShareDialogData>(ShareDialogComponent, {
      data: { orgId: this.orgId(), dashboardId: d.id, dashboardName: d.name },
      width: '36rem',
      maxWidth: '95vw',
    });
  }

  /** Opens the browser's print dialog, whose "Save as PDF" makes the file. The print styles leave only the dashboard on the page. */
  protected print(): void {
    window.print();
  }

  protected editLayout(): void {
    this.draft.set(this.savedLayout());
    this.past.set([]);
    this.future.set([]);
    this.editingLayout.set(true);
  }

  protected cancelLayout(): void {
    this.editingLayout.set(false);
    this.draft.set(null);
    this.past.set([]);
    this.future.set([]);
  }

  // ---- undo and redo of the layout being arranged ----

  /** A move or a resize is about to start (see the grid's layoutBegin): what is there now is what Undo goes back to. */
  protected beginChange(): void {
    this.past.update((past) => [...past, this.items()].slice(-MAX_UNDO));
    this.future.set([]);
  }

  protected undo(): void {
    this.step(this.past, this.future);
  }

  protected redo(): void {
    this.step(this.future, this.past);
  }

  /** Takes the layout off one stack, puts the current one on the other and shows it. A layout equal to the current one is skipped. */
  private step(from: WritableSignal<LayoutItem[][]>, to: WritableSignal<LayoutItem[][]>): void {
    const current = this.items();
    let stack = from();
    while (stack.length > 0 && sameLayout(stack[stack.length - 1], current)) stack = stack.slice(0, -1);
    const target = stack[stack.length - 1];
    from.set(target ? stack.slice(0, -1) : stack);
    if (!target) return;
    to.update((others) => [...others, current]);
    this.draft.set(target);
  }

  protected onKey(event: KeyboardEvent): void {
    if (!this.editingLayout() || this.busy() || !(event.ctrlKey || event.metaKey) || event.altKey) return;
    const target = event.target as HTMLElement | null;
    if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) return;

    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) this.undo();
    else if (key === 'y' || (key === 'z' && event.shiftKey)) this.redo();
    else return;
    event.preventDefault();
  }

  protected saveLayout(d: Dashboard): void {
    const layout = this.items();
    const widgets = d.definition.widgets.map((w) => {
      const place = layout.find((i) => i.id === w.id);
      return place ? { ...w, layout: { x: place.x, y: place.y, w: place.w, h: place.h } } : w;
    });
    // The charts are not run again: moving a widget does not change what it shows.
    this.save(d, { ...d.definition, widgets }, () => this.cancelLayout(), false);
  }

  /** Writes the places the page worked out into the widgets, tab by tab, so they stay where they are when others come and go. */
  private placed(widgets: DashboardWidget[], tabs: DashboardTab[] = this.tabs()): DashboardWidget[] {
    return placeAll(widgets, tabs);
  }

  // ---- tabs ----

  protected selectTab(id: string): void {
    if (!this.editingLayout()) this.chosenTab.set(id); // the layout being arranged belongs to the tab it was started on
  }

  protected addTab(d: Dashboard): void {
    const tabs = this.tabs();
    this.askTabName({ taken: tabs.map((t) => t.name) }, (name) => {
      // The first tab turns the one page into two: what is there now becomes the first tab.
      const first = name.toLowerCase() === 'main' ? 'Overview' : 'Main';
      const next = [...(tabs.length === 0 ? [{ id: NEW_ID, name: first }] : tabs), { id: NEW_ID, name }];
      this.save(d, { ...d.definition, tabs: next }, () => this.chosenTab.set(this.tabs().at(-1)?.id ?? null), false);
    });
  }

  protected renameTab(d: Dashboard, tab: DashboardTab): void {
    this.askTabName({ name: tab.name, taken: this.tabs().filter((t) => t.id !== tab.id).map((t) => t.name) }, (name) => {
      this.save(d, { ...d.definition, tabs: this.tabs().map((t) => (t.id === tab.id ? { ...t, name } : t)) }, undefined, false);
    });
  }

  /** Takes a tab away; what was on it goes to the first tab, and the last two tabs together become one page again. */
  protected removeTab(d: Dashboard, tab: DashboardTab): void {
    const tabs = this.tabs();
    const onIt = d.definition.widgets.filter((w) => tabOf(w, tabs) === tab.id).length;
    const moved = onIt === 0 ? '' : ` The ${onIt} ${onIt === 1 ? 'widget on it moves' : 'widgets on it move'} to the first tab.`;
    this.confirms
      .ask({ title: 'Remove tab', message: `Remove the tab "${tab.name}"?${moved}`, confirmLabel: 'Remove', danger: true })
      .subscribe((yes) => {
        if (yes) this.removeTabConfirmed(d, tab);
      });
  }

  private removeTabConfirmed(d: Dashboard, tab: DashboardTab): void {
    const tabs = this.tabs();
    const remaining = tabs.filter((t) => t.id !== tab.id);
    const home = remaining[0]?.id ?? null;
    // What was on the removed tab goes to the first one, to be placed there; with one tab left it is one page again.
    const widgets = d.definition.widgets.map((w) => {
      const was = tabOf(w, tabs);
      return was === tab.id ? { ...w, tabId: home, layout: null } : w;
    });
    const single = remaining.length < 2;
    const kept = single ? widgets.map((w) => ({ ...w, tabId: null })) : widgets;
    this.save(d, { ...d.definition, tabs: single ? [] : remaining, widgets: this.placed(kept, single ? [] : remaining) }, undefined, false);
  }

  protected moveTab(d: Dashboard, tab: DashboardTab, by: -1 | 1): void {
    const tabs = [...this.tabs()];
    const at = tabs.findIndex((t) => t.id === tab.id);
    const to = at + by;
    if (at < 0 || to < 0 || to >= tabs.length) return;
    [tabs[at], tabs[to]] = [tabs[to], tabs[at]];
    this.save(d, { ...d.definition, tabs }, undefined, false);
  }

  /** Puts a widget on another tab, at the first free place there. */
  protected moveWidget(d: Dashboard, widget: DashboardWidget, tabId: string): void {
    const widgets = d.definition.widgets.map((w) => (w.id === widget.id ? { ...w, tabId, layout: null } : w));
    this.save(d, { ...d.definition, widgets: this.placed(widgets) }, undefined, false);
  }

  private askTabName(data: TabNameDialogData, then: (name: string) => void): void {
    this.dialog
      .open<TabNameDialogComponent, TabNameDialogData, string>(TabNameDialogComponent, { data, width: '24rem', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((name) => {
        if (name) then(name);
      });
  }

  /** Lets the editor pick saved charts to put on this dashboard (or go and make a new one). */
  protected addCharts(d: Dashboard): void {
    const placed = new Set(d.definition.widgets.filter((w) => !w.kind || w.kind === 'Chart').map((w) => w.chartId));
    const names: Record<string, string> = {};
    for (const dataset of this.datasets()) names[dataset.id] = dataset.name;

    // The list may have grown since the dashboard opened (a chart was made in the editor and we came back).
    this.chartsApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.charts.set(items);
        this.dialog
          .open<AddChartsDialogComponent, AddChartsDialogData, string[]>(AddChartsDialogComponent, {
            data: { orgId: this.orgId(), charts: items.filter((c) => !placed.has(c.id)), datasetNames: names },
            width: '30rem',
            maxWidth: '95vw',
          })
          .afterClosed()
          .subscribe((ids) => {
            if (!ids || ids.length === 0) return;
            // New widgets have no id yet, so they are placed under their position in the list and the server gives ids.
            const widgets = this.placed([...d.definition.widgets, ...ids.map((chartId, n) => ({ id: `${NEW_ID}-${n}`, chartId, ...(this.activeTabId() ? { tabId: this.activeTabId() } : {}) }))])
              .map((w) => (w.id.startsWith(NEW_ID) ? { ...w, id: NEW_ID } : w));
            this.save(d, { ...d.definition, widgets });
          });
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the charts.')),
    });
  }

  /** Gets the picture of a picture widget; the widget calls it again whenever the picture's version changes. */
  protected loadWidgetImage(d: Dashboard, widget: DashboardWidget): () => Observable<Blob> {
    return () => this.api.widgetImage(this.orgId(), d.id, widget.id, widget.imageKey ?? '');
  }

  /** Lets the editor choose a picture to put on the dashboard. */
  protected addPicture(d: Dashboard): void {
    this.openPictureDialog({ orgId: this.orgId(), dashboard: d, tabId: this.activeTabId() });
  }

  protected editPicture(d: Dashboard, widget: DashboardWidget): void {
    this.openPictureDialog({ orgId: this.orgId(), dashboard: d, widget });
  }

  /** Lets the editor write some text (titles, notes) to put on the dashboard. */
  protected addText(d: Dashboard): void {
    this.openTextDialog({ orgId: this.orgId(), dashboard: d, tabId: this.activeTabId() });
  }

  protected editText(d: Dashboard, widget: DashboardWidget): void {
    this.openTextDialog({ orgId: this.orgId(), dashboard: d, widget });
  }

  private openTextDialog(data: TextWidgetDialogData): void {
    this.dialog
      .open<TextWidgetDialogComponent, TextWidgetDialogData, Dashboard>(TextWidgetDialogComponent, { data, width: '36rem', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        // Words are not charts, so nothing needs running again.
        this.show(saved);
        this.changed.emit(saved);
      });
  }

  private openPictureDialog(data: ImageWidgetDialogData): void {
    this.dialog
      .open<ImageWidgetDialogComponent, ImageWidgetDialogData, Dashboard>(ImageWidgetDialogComponent, { data, width: '30rem', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        // Pictures are not charts, so nothing needs running again.
        this.show(saved);
        this.changed.emit(saved);
      });
  }

  protected resultFor(widgetId: string): WidgetResult | undefined {
    return this.run()?.widgets.find((w) => w.widgetId === widgetId);
  }

  protected apply(): void {
    this.execute();
  }

  /** Sets how often the open page reloads its charts. It is saved with the dashboard and applies to everyone. */
  protected setReload(d: Dashboard, value: string): void {
    const seconds = value === '' ? null : Number(value);
    const before = d.definition.reloadSeconds ?? null;
    if (seconds === before) return; // if it cannot be saved, the select still shows the old interval
    this.save(d, { ...d.definition, reloadSeconds: seconds }, undefined, false);
  }

  protected reloadOptions(current: number | null | undefined): SelectOption[] {
    return reloadChoices(current).map((c) => ({ value: String(c.seconds ?? ''), label: c.label }));
  }

  protected refreshOptions(current: number | null): SelectOption[] {
    return this.refreshChoices(current).map((c) => ({ value: String(c.minutes ?? ''), label: c.label }));
  }

  /** The usual intervals, plus the current one if it was set to something else through the API. */
  protected refreshChoices(current: number | null) {
    return current === null || REFRESH_CHOICES.some((c) => c.minutes === current)
      ? REFRESH_CHOICES
      : [...REFRESH_CHOICES, { minutes: current, label: intervalLabel(current) }];
  }

  protected intervalText(minutes: number): string {
    return intervalLabel(minutes);
  }

  /** Puts the dashboard on a schedule, or off (an empty choice). It takes effect at once and needs no Save. */
  protected setSchedule(d: Dashboard, value: string): void {
    const minutes = value === '' ? null : Number(value);
    const before = d.refresh?.intervalMinutes ?? null;
    if (minutes === before) return;
    this.busy.set(true);
    this.errors.set([]);
    this.api.setRefresh(this.orgId(), d.id, minutes).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.show(saved);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not change the refresh schedule.')); // the select still shows the old schedule
      },
    });
  }

  /** How old a reused result is, for the note on a chart. */
  protected age(utc: string): string {
    return ago(utc);
  }

  /** Asks the databases again instead of reusing stored results; what comes back is stored for the next viewer. */
  protected refresh(): void {
    this.execute(true);
  }

  protected clearFilters(): void {
    this.filterForm.reset();
    this.execute();
  }

  protected addFilter(d: Dashboard): void {
    if (this.newFilterForm.invalid) return;
    const v = this.newFilterForm.getRawValue();
    this.save(d, {
      ...d.definition,
      filters: [...d.definition.filters, { id: NEW_ID, label: v.label, column: v.column, operator: v.operator, target: v.target }],
    }, () => this.newFilterForm.controls.label.reset());
  }

  protected removeFilter(d: Dashboard, filter: DashboardFilter): void {
    this.save(d, { ...d.definition, filters: d.definition.filters.filter((f) => f.id !== filter.id) });
  }

  protected removeWidget(d: Dashboard, widgetId: string): void {
    this.save(d, { ...d.definition, widgets: this.placed(d.definition.widgets.filter((w) => w.id !== widgetId)) });
  }

  /** Makes a copy of the dashboard in this organization; the page shows it. */
  protected duplicate(d: Dashboard): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.duplicate(this.orgId(), d.id).subscribe({
      next: (copy) => {
        this.busy.set(false);
        this.duplicated.emit(copy);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not copy the dashboard.'));
      },
    });
  }

  /** Saves the dashboard as a file, to keep or to bring into another organization. */
  protected exportFile(d: Dashboard): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.exportPackage(this.orgId(), d.id).subscribe({
      next: (file) => {
        this.busy.set(false);
        downloadJson(d.name, file, 'databoard.json');
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not export the dashboard.'));
      },
    });
  }

  protected removeDashboard(d: Dashboard): void {
    this.confirms
      .ask({ title: 'Delete dashboard', message: `Delete the dashboard "${d.name}"?`, confirmLabel: 'Delete', danger: true })
      .subscribe((yes) => {
        if (!yes) return;
        this.api.remove(this.orgId(), d.id).subscribe({
          next: () => this.deleted.emit(d.id),
          error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dashboard.')),
        });
      });
  }

  private save(d: Dashboard, definition: DashboardDefinition, done?: () => void, rerun = true, failed?: () => void): void {
    this.busy.set(true);
    this.errors.set([]);
    this.api.update(this.orgId(), d, definition).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.show(saved);
        this.changed.emit(saved);
        done?.();
        if (rerun) this.execute();
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the dashboard.'));
        failed?.();
      },
    });
  }

  /** Shows a dashboard and keeps the values already typed into filters that still exist. */
  private show(d: Dashboard): void {
    const previous = this.filterForm?.getRawValue() ?? {};
    const controls: Record<string, FormControl<string>> = {};
    for (const f of d.definition.filters) {
      controls[f.id] = this.fb.nonNullable.control(previous[f.id] ?? '');
    }
    this.filterForm = this.fb.nonNullable.group(controls);
    this.dashboard.set(d);
  }

  /**
   * Runs the charts again. A quiet run (the timer's) leaves the page as it is until the answer comes, so nothing flickers or
   * is disabled; the old charts stay if it fails.
   */
  private execute(refresh = false, quiet = false): void {
    const d = this.dashboard();
    if (!d) return;

    const values: FilterValues = {};
    for (const f of d.definition.filters) {
      const parsed = parseFilterInput(f.operator, this.filterForm.controls[f.id]?.value ?? '');
      if (parsed.length > 0) values[f.id] = parsed;
    }

    if (!quiet) {
      this.busy.set(true);
      this.errors.set([]);
    }
    this.api.run(this.orgId(), d.id, values, refresh, quiet).subscribe({
      next: (r) => {
        this.run.set(r);
        this.loadedAt.set(clock());
        if (quiet) this.errors.set([]);
        else this.busy.set(false);
      },
      error: (e) => {
        if (!quiet) this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not run the dashboard.'));
      },
    });
  }
}
