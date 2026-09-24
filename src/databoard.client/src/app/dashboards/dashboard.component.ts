import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ChartViewComponent } from '../charts/chart-view.component';
import { ExportMenuComponent } from '../charts/export-menu.component';
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
import { ShareDialogComponent, ShareDialogData } from './share-dialog.component';
import { REFRESH_CHOICES, ago, intervalLabel } from '../shared/relative-time';
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

/** In and Between take several values, typed as a comma-separated list; the rest take one. */
export function parseFilterInput(operator: SharedFilterOperator, text: string): string[] {
  const trimmed = text.trim();
  if (!trimmed) return [];
  return operator === 'In' || operator === 'Between'
    ? trimmed.split(',').map((v) => v.trim()).filter((v) => v.length > 0)
    : [trimmed];
}

@Component({
  selector: 'app-dashboard',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, ChartViewComponent, DashboardGridComponent, ExportMenuComponent, BackgroundLayerComponent, ImageWidgetComponent, MarkdownComponent, DashboardTabBarComponent, MoveToTabComponent],
  template: `
    <mat-card appearance="raised" class="detail">
    @if (dashboard(); as d) {
      <mat-card-header>
        <mat-card-title>{{ d.name }}</mat-card-title>
        <span class="actions no-print">
          @if (d.definition.widgets.length > 0 && !editingLayout()) {
            <button mat-button type="button" class="print" (click)="print()">Print / PDF</button>
          }
          @if (canEdit()) {
            @if (editingLayout()) {
              <button mat-flat-button type="button" class="save-layout" [disabled]="busy() || !layoutChanged()" (click)="saveLayout(d)">Save layout</button>
              <button mat-button type="button" class="cancel-layout" [disabled]="busy()" (click)="cancelLayout()">Cancel</button>
            } @else {
              @if (d.definition.widgets.length > 0) {
                <button mat-stroked-button type="button" class="edit-layout" (click)="editLayout()">Edit layout</button>
              }
              <button mat-button type="button" class="refresh" [disabled]="busy()" (click)="refresh()">Refresh data</button>
              <button mat-stroked-button type="button" class="share" (click)="share(d)">Share</button>
              <button mat-stroked-button type="button" class="duplicate" [disabled]="busy()" (click)="duplicate(d)">Duplicate</button>
              <button mat-stroked-button type="button" class="export" [disabled]="busy()" (click)="exportFile(d)">Export</button>
              <button mat-stroked-button type="button" (click)="addCharts(d)">Add chart</button>
              <button mat-stroked-button type="button" class="add-picture" (click)="addPicture(d)">Add picture</button>
              <button mat-stroked-button type="button" class="add-text" (click)="addText(d)">Add text</button>
              <button mat-stroked-button type="button" class="background" (click)="editBackground(d)">Background</button>
              <button mat-button type="button" (click)="removeDashboard(d)">Delete dashboard</button>
            }
          }
        </span>
      </mat-card-header>
      <mat-card-content>
      @if (d.background; as background) {
        <app-background-layer [load]="loadBackground(d, background.version)" [version]="background.version" [fit]="background.fit" [opacity]="background.opacity" />
      }

      @if (d.refresh; as schedule) {
        @if (schedule.intervalMinutes || canEdit()) {
          <div class="schedule no-print">
            @if (canEdit()) {
              <label>
                <span>Refresh every</span>
                <select aria-label="Refresh schedule" [disabled]="busy()" (change)="setSchedule(d, $any($event.target))">
                  @for (choice of refreshChoices(schedule.intervalMinutes); track choice.minutes) {
                    <option [value]="choice.minutes ?? ''" [selected]="choice.minutes === schedule.intervalMinutes">{{ choice.label }}</option>
                  }
                </select>
              </label>
            }
            @if (schedule.intervalMinutes) {
              <small class="status">
                @if (!canEdit()) { Refreshed every {{ intervalText(schedule.intervalMinutes) }}. }
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
            @if (canEdit()) {
              <button mat-button type="button" class="remove-filter" (click)="removeFilter(d, filter)">Remove {{ filter.label }}</button>
            }
          }
          <button mat-flat-button type="submit" class="apply" [disabled]="busy()">Apply filters</button>
          <button mat-button type="button" class="apply" (click)="clearFilters()">Clear</button>
        </form>
      }

      @if (d.definition.widgets.length === 0) {
        <p><em>This dashboard has no charts yet.</em>
          @if (canEdit()) { Use <strong>Add chart</strong> to put charts on it, or <a [routerLink]="['/organizations', orgId(), 'charts', 'new']">create a new chart</a>. }
        </p>
      }

      <app-dashboard-tab-bar [tabs]="tabs()" [activeId]="activeTabId()" [canEdit]="canEdit()" [editing]="editingLayout()"
                             (select)="selectTab($event)" (add)="addTab(d)" (rename)="renameTab(d, $event)" (remove)="removeTab(d, $event)"
                             (moveEarlier)="moveTab(d, $event, -1)" (moveLater)="moveTab(d, $event, 1)" />

      <app-dashboard-grid [items]="items()" [editing]="editingLayout()" [labels]="labels()" (layoutChange)="draft.set($event)">
        <ng-template let-id>
          @if (widgetById(id); as widget) {
          @if (widget.kind === 'Image') {
            <mat-card appearance="outlined" class="widget picture">
              <app-image-widget [load]="loadWidgetImage(d, widget)" [version]="widget.imageKey ?? ''" [fit]="widget.imageFit ?? 'contain'"
                                [alt]="widget.alt" [linkUrl]="widget.linkUrl" />
              @if (canEdit() && !editingLayout()) {
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
              @if (canEdit() && !editingLayout()) {
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
              <mat-card-header>
                <mat-card-title>{{ chart.name }}</mat-card-title>
                <mat-card-subtitle>{{ datasetName(chart.datasetId) }}</mat-card-subtitle>
                @if (!editingLayout()) {
                  <span class="widget-actions no-print">
                    @if (resultFor(widget.id)?.result; as exported) {
                      <app-export-menu [name]="chart.name" [result]="exported" [type]="chart.type" />
                    }
                    @if (canEdit()) {
                      <app-move-to-tab [tabs]="tabs()" [current]="activeTabId()" (pick)="moveWidget(d, widget, $event)" />
                      <a mat-button [routerLink]="['/organizations', orgId(), 'charts', chart.id]">Edit chart</a>
                      <button mat-button type="button" class="remove-widget" (click)="removeWidget(d, widget.id)">Remove</button>
                    }
                  </span>
                }
              </mat-card-header>
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
                @if (canEdit() && !editingLayout()) {
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

      @if (canEdit()) {
        <h3 class="no-print">Add a shared filter</h3>
        <form class="form-row no-print" [formGroup]="newFilterForm" (ngSubmit)="addFilter(d)">
          <mat-form-field>
            <mat-label>Label</mat-label>
            <input matInput formControlName="label" aria-label="Filter label" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Applies to</mat-label>
            <select matNativeControl formControlName="target" aria-label="Filter applies to" (change)="setTarget($any($event.target).value)">
              <option value="Column">A column</option>
              <option value="Parameter" [disabled]="parameterNames().length === 0">A dataset parameter</option>
            </select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ target() === 'Parameter' ? 'Parameter' : 'Column' }}</mat-label>
            <select matNativeControl formControlName="column" aria-label="Filter column">
              @for (name of targetNames(); track name) { <option [value]="name">{{ name }}</option> }
            </select>
            <mat-hint>
              @if (target() === 'Parameter') { Sets the value for every chart whose dataset has this parameter }
              @else { Applies to every chart whose dataset has this column }
            </mat-hint>
          </mat-form-field>
          @if (target() === 'Column') {
            <mat-form-field>
              <mat-label>Condition</mat-label>
              <select matNativeControl formControlName="operator" aria-label="Filter condition">
                @for (op of operators; track op.value) { <option [value]="op.value">{{ op.label }}</option> }
              </select>
            </mat-form-field>
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
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    .widget-actions { margin-left: auto; display: flex; }
    /* The background picture is a layer under everything in the content, so the content makes its own stacking context. */
    .detail > mat-card-content { flex: 1; min-height: 0; overflow: auto; padding-top: 0.75rem; position: relative; isolation: isolate; }
    .filters { margin: 0.5rem 0 1rem; }
    .schedule { display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem 0.75rem; margin: 0 0 0.75rem; }
    .schedule label { display: flex; align-items: center; gap: 0.5rem; }
    .schedule select { padding: 0.25rem; }
    .apply, .remove-filter { margin-top: 0.4rem; }
    /* A widget fills its cell on the grid; its chart takes what the header leaves, and a table scrolls inside. */
    .widget { height: 100%; }
    .widget mat-card-header { align-items: center; }
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
  private readonly datasetsApi = inject(DatasetsService);
  private readonly chartsApi = inject(ChartsService);
  private readonly dialog = inject(MatDialog);
  private readonly fb = inject(FormBuilder);

  readonly orgId = input.required<string>();
  readonly dashboardId = input.required<string>();
  readonly canEdit = input(false);

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
  protected readonly draft = signal<LayoutItem[] | null>(null);
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
  protected readonly targetNames = computed(() => (this.target() === 'Parameter' ? this.parameterNames() : this.columnNames()));

  protected setTarget(target: FilterTarget): void {
    this.target.set(target);
    const controls = this.newFilterForm.controls;
    controls.column.setValue(this.targetNames()[0] ?? '');
    controls.operator.setValue(target === 'Parameter' ? 'Eq' : 'In');
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
    this.editingLayout.set(true);
  }

  protected cancelLayout(): void {
    this.editingLayout.set(false);
    this.draft.set(null);
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
    if (!confirm(`Remove the tab "${tab.name}"?${moved}`)) return;
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

  protected datasetName(id: string): string {
    return this.datasets().find((d) => d.id === id)?.name ?? '';
  }

  protected resultFor(widgetId: string): WidgetResult | undefined {
    return this.run()?.widgets.find((w) => w.widgetId === widgetId);
  }

  protected apply(): void {
    this.execute();
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
  protected setSchedule(d: Dashboard, select: HTMLSelectElement): void {
    const minutes = select.value === '' ? null : Number(select.value);
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
        this.errors.set(problemMessages(e, 'Could not change the refresh schedule.'));
        select.value = before === null ? '' : String(before); // nothing in the page changed, so put the control back by hand
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
    if (!confirm(`Delete the dashboard "${d.name}"?`)) return;
    this.api.remove(this.orgId(), d.id).subscribe({
      next: () => this.deleted.emit(d.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dashboard.')),
    });
  }

  private save(d: Dashboard, definition: DashboardDefinition, done?: () => void, rerun = true): void {
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

  private execute(refresh = false): void {
    const d = this.dashboard();
    if (!d) return;

    const values: FilterValues = {};
    for (const f of d.definition.filters) {
      const parsed = parseFilterInput(f.operator, this.filterForm.controls[f.id]?.value ?? '');
      if (parsed.length > 0) values[f.id] = parsed;
    }

    this.busy.set(true);
    this.errors.set([]);
    this.api.run(this.orgId(), d.id, values, refresh).subscribe({
      next: (r) => {
        this.run.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not run the dashboard.'));
      },
    });
  }
}
