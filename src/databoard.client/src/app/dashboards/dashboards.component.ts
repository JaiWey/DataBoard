import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { tap } from 'rxjs';
import { CHART_TYPES, Chart, ChartsService } from '../charts/charts.service';
import { problemMessages } from '../organizations/data-sources.service';
import { DatasetsService } from '../organizations/datasets.service';
import { FolderTreeController } from '../shared/folder-tree/folder-tree-controller';
import { TreeEntry } from '../shared/folder-tree/folder-tree';
import { FolderTreeComponent } from '../shared/folder-tree/folder-tree.component';
import { ChartCardComponent } from './chart-card.component';
import { DashboardComponent } from './dashboard.component';
import { NewDashboardDialogComponent, NewDashboardDialogData } from './new-dashboard-dialog.component';
import { DASHBOARD_PACKAGE_FORMAT, Dashboard, DashboardPackage, DashboardSummary, DashboardsService, MAX_PACKAGE_BYTES } from './dashboards.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

type Mode = 'dashboards' | 'charts';

/**
 * Dashboards and the charts they are made of. A list on the left (switch between Dashboards and Charts) and the selected
 * dashboard or chart on the right. Charts are made in the chart editor; dashboards pick charts to show.
 */
@Component({
  selector: 'app-dashboards',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatButtonToggleModule, FolderTreeComponent, DashboardComponent, ChartCardComponent],
  template: `
    <h2>{{ organization()?.name }} <small>dashboards</small></h2>

    <div class="layout">
      <mat-card appearance="raised" class="list">
        <mat-card-header>
          <mat-button-toggle-group class="modes" aria-label="Show" [value]="mode()" (change)="setMode($event.value)">
            <mat-button-toggle value="dashboards">Dashboards</mat-button-toggle>
            <mat-button-toggle value="charts">Charts</mat-button-toggle>
          </mat-button-toggle-group>
          @if (canEdit()) {
            @if (mode() === 'dashboards') {
              <button mat-flat-button type="button" class="add" (click)="openNewDialog(dashboardTree.selectedFolderId())">New dashboard</button>
              <input #importFile type="file" class="hidden" accept=".json,application/json" aria-label="Dashboard file" (change)="importFrom($any($event.target))" />
              <button mat-stroked-button type="button" class="import" [disabled]="importing()" (click)="importFile.click()">Import</button>
            } @else {
              <a mat-flat-button class="add" [routerLink]="['/organizations', orgId, 'charts', 'new']" [queryParams]="newChartParams()">New chart</a>
            }
          }
        </mat-card-header>
        <mat-card-content>
          @if (mode() === 'dashboards') {
            @for (message of importErrors(); track message) {
              <p class="fail" role="alert">{{ message }}</p>
            }
            @if (importNotice(); as notice) {
              <p class="notice" role="status">{{ notice }}</p>
            }
            @for (message of dashboardTree.errors(); track message) {
              <p class="fail" role="alert">{{ message }}</p>
            }
            <app-folder-tree [folders]="dashboardTree.folders()" [entries]="dashboardEntries()" [activeId]="selectedId()" [canEdit]="canEdit()"
                             [selectedFolderId]="dashboardTree.selectedFolderId()" [storageKey]="'databoard.dashboardFolders.' + orgId"
                             filterLabel="Filter dashboards" treeLabel="Dashboards" emptyText="No dashboards yet."
                             [addActions]="[{ id: 'new', label: 'New dashboard here' }]"
                             (open)="select($event.id)" (selectFolder)="dashboardTree.selectedFolderId.set($event)"
                             (createFolder)="dashboardTree.newFolder($event)" (renameFolder)="dashboardTree.renameFolder($event)"
                             (deleteFolder)="dashboardTree.deleteFolder($event)" (addItem)="openNewDialog($event.folderId)"
                             (move)="dashboardTree.moveItem($event.item, $event.targetId)" (moveTo)="dashboardTree.chooseDestination($event)" />
          } @else {
            @for (message of chartTree.errors(); track message) {
              <p class="fail" role="alert">{{ message }}</p>
            }
            <app-folder-tree [folders]="chartTree.folders()" [entries]="chartEntries()" [activeId]="selectedChartId()" [canEdit]="canEdit()"
                             [selectedFolderId]="chartTree.selectedFolderId()" [storageKey]="'databoard.chartFolders.' + orgId"
                             filterLabel="Filter charts" treeLabel="Charts" emptyText="No charts yet."
                             [addActions]="[{ id: 'new', label: 'New chart here' }]"
                             (open)="selectChart($event.id)" (selectFolder)="chartTree.selectedFolderId.set($event)"
                             (createFolder)="chartTree.newFolder($event)" (renameFolder)="chartTree.renameFolder($event)"
                             (deleteFolder)="chartTree.deleteFolder($event)" (addItem)="newChartIn($event.folderId)"
                             (move)="chartTree.moveItem($event.item, $event.targetId)" (moveTo)="chartTree.chooseDestination($event)" />
          }        </mat-card-content>
      </mat-card>

      <section class="workspace">
        @if (mode() === 'dashboards') {
          @for (dashboard of selected(); track dashboard.id) {
            <app-dashboard [orgId]="orgId" [dashboardId]="dashboard.id" [canEdit]="canEdit()"
                           (changed)="onChanged($event)" (deleted)="onDeleted($event)" (duplicated)="onCreatedCopy($event)" />
          } @empty {
            <mat-card appearance="raised" class="detail empty">
              <mat-card-content class="placeholder">
                <p><em>Select a dashboard to see it.</em></p>
              </mat-card-content>
            </mat-card>
          }
        } @else {
          @for (chart of selectedChart(); track chart.id) {
            <app-chart-card [chart]="chart" [orgId]="orgId" [canEdit]="canEdit()" [datasetNames]="datasetNames()"
                            [dashboards]="dashboards()" (deleted)="onChartDeleted($event)" />
          } @empty {
            <mat-card appearance="raised" class="detail empty">
              <mat-card-content class="placeholder">
                <p><em>Select a chart to see it.</em></p>
              </mat-card-content>
            </mat-card>
          }
        }
      </section>
    </div>
  `,
  styles: `
    :host { --mat-card-elevated-container-color: #fff; }
    /* Both columns are exactly as tall as the space under the title (so the page itself never scrolls) and scroll inside. */
    .layout { display: grid; grid-template-columns: 20rem 1fr; gap: 1.25rem; height: calc(100vh - 16rem); min-height: 24rem; }
    .list { height: 100%; min-height: 0; }
    .list mat-card-content { flex: 1; min-height: 0; display: flex; flex-direction: column; }
    .list app-folder-tree { flex: 1; }
    .fail { color: var(--mat-sys-error); margin: 0 0 0.5rem; }
    .notice { margin: 0 0 0.5rem; padding: 0.5rem 0.75rem; border-radius: 0.5rem; background: var(--mat-sys-secondary-container); }
    .hidden { display: none; }
    .workspace { min-width: 0; min-height: 0; display: flex; flex-direction: column; }
    .workspace > app-dashboard, .workspace > app-chart-card { flex: 1; min-height: 0; }
    .workspace > .empty { flex: 1; }
    @media (max-width: 52rem) {
      .layout { grid-template-columns: 1fr; height: auto; }
      .list { height: auto; max-height: 20rem; }
      .workspace > app-dashboard, .workspace > app-chart-card { min-height: 30rem; }
    }
    .add { margin-left: auto; }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    mat-card-content { padding-top: 0.75rem; }
    .placeholder { flex: 1; display: grid; place-items: center; text-align: center; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class DashboardsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DashboardsService);
  private readonly chartsApi = inject(ChartsService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });

  protected readonly mode = signal<Mode>('dashboards');
  protected readonly importing = signal(false);
  protected readonly importErrors = signal<string[]>([]);
  /** What the last import did, when something was left out of it. */
  protected readonly importNotice = signal('');

  /** The folder trees: one for dashboards, one for charts. */
  protected readonly dashboardTree = new FolderTreeController({
    orgId: () => this.orgId,
    kind: 'Dashboard',
    entries: () => this.dashboardEntries(),
    moveEntry: (id, folderId) =>
      this.api.move(this.orgId, id, folderId).pipe(
        tap((saved) => this.dashboards.update((items) => items.map((d) => (d.id === saved.id ? { ...d, folderId: saved.folderId ?? null } : d)))),
      ),
  });
  protected readonly chartTree = new FolderTreeController({
    orgId: () => this.orgId,
    kind: 'Chart',
    entries: () => this.chartEntries(),
    moveEntry: (id, folderId) =>
      this.chartsApi.move(this.orgId, id, folderId).pipe(
        tap((saved) => this.charts.update((items) => items.map((c) => (c.id === saved.id ? { ...c, folderId: saved.folderId ?? null } : c)))),
      ),
  });
  protected readonly dashboardEntries = computed<TreeEntry[]>(() =>
    this.dashboards().map((d) => ({
      id: d.id, name: d.name, folderId: d.folderId ?? null,
      subline: `${d.widgetCount} ${d.widgetCount === 1 ? 'chart' : 'charts'}`,
    })),
  );
  protected readonly chartEntries = computed<TreeEntry[]>(() =>
    this.charts().map((c) => ({
      id: c.id, name: c.name, folderId: c.folderId ?? null,
      subline: `${this.typeLabel(c.type)} · ${this.datasetNames()[c.datasetId] ?? 'unknown dataset'}`,
    })),
  );

  protected readonly dashboards = signal<DashboardSummary[]>([]);
  protected readonly loaded = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  /** Zero or one entries, so the detail is rebuilt (filters and results reset) whenever another dashboard is picked. */
  protected readonly selected = computed(() => {
    const dashboard = this.dashboards().find((d) => d.id === this.selectedId());
    return dashboard ? [dashboard] : [];
  });

  protected readonly charts = signal<Chart[]>([]);
  protected readonly chartsLoaded = signal(false);
  protected readonly selectedChartId = signal<string | null>(null);
  protected readonly selectedChart = computed(() => {
    const chart = this.charts().find((c) => c.id === this.selectedChartId());
    return chart ? [chart] : [];
  });
  protected readonly datasetNames = signal<Record<string, string>>({});

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    // Coming back from the chart editor (or following a link) can ask for the Charts list and one chart.
    const query = this.route.snapshot.queryParamMap;
    if (query.get('view') === 'charts') this.mode.set('charts');

    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.datasetsApi.list(this.orgId).subscribe({
      next: (items) => this.datasetNames.set(Object.fromEntries(items.map((d) => [d.id, d.name]))),
    });
    this.dashboardTree.load();
    this.chartTree.load();
    this.reload();
    this.reloadCharts(query.get('chart'));
  }

  protected typeLabel(type: string): string {
    return CHART_TYPES.find((t) => t.value === type)?.label ?? type;
  }

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
  }

  // ---- dashboards -----------------------------------------------------------------------------------------

  protected select(id: string): void {
    this.selectedId.set(id);
    this.dashboardTree.selectedFolderId.set(this.dashboards().find((d) => d.id === id)?.folderId ?? null);
  }

  protected openNewDialog(folderId: string | null = null): void {
    this.dialog
      .open<NewDashboardDialogComponent, NewDashboardDialogData, Dashboard>(NewDashboardDialogComponent, {
        data: { orgId: this.orgId, folderId },
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (!created) return;
        const entry: DashboardSummary = {
          id: created.id,
          name: created.name,
          description: created.description,
          widgetCount: 0,
          updatedUtc: created.updatedUtc,
          chartIds: [],
          folderId: created.folderId ?? null,
        };
        this.dashboards.update((items) => [...items, entry]);
        this.select(created.id);
        this.reload();
      });
  }

  /** A saved dashboard keeps its list entry (name, chart count and charts) current. */
  protected onChanged(saved: Dashboard): void {
    this.dashboards.update((items) =>
      items.map((d) =>
        d.id === saved.id
          ? {
              ...d, name: saved.name,
              // Pictures and text are on the dashboard but are not charts.
              widgetCount: saved.definition.widgets.filter((w) => !w.kind || w.kind === 'Chart').length,
              chartIds: saved.definition.widgets.filter((w) => !w.kind || w.kind === 'Chart').map((w) => w.chartId),
            }
          : d,
      ),
    );
  }

  /** A copy was made of the dashboard on show: it joins the list and is shown. */
  protected onCreatedCopy(copy: Dashboard): void {
    this.addToList(copy);
  }

  /** Makes a dashboard from a file (a dashboard exported earlier, here or in another organization). */
  protected importFrom(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    this.importErrors.set([]);
    this.importNotice.set('');
    if (!file) return;
    if (file.size > MAX_PACKAGE_BYTES) {
      this.importErrors.set(['That file is too big to be a dashboard.']);
      return;
    }

    this.importing.set(true);
    file.text().then(
      (text) => {
        let pkg: DashboardPackage;
        try {
          pkg = JSON.parse(text) as DashboardPackage;
        } catch {
          this.fail('That file is not a dashboard file: it is not valid JSON.');
          return;
        }
        if (pkg?.format !== DASHBOARD_PACKAGE_FORMAT) {
          this.fail('That file is not a DataBoard dashboard file.');
          return;
        }
        this.api.importPackage(this.orgId, pkg, { folderId: this.dashboardTree.selectedFolderId() }).subscribe({
          next: (result) => {
            this.importing.set(false);
            this.addToList(result.dashboard);
            const missing = result.missingCharts;
            if (missing.length > 0) {
              const plural = missing.length > 1;
              this.importNotice.set(
                `Imported "${result.dashboard.name}". ${plural ? 'These charts were' : 'This chart was'} not found here, ` +
                  `so ${plural ? 'their widgets were' : 'its widget was'} left out: ${missing.join(', ')}.`,
              );
            }
          },
          error: (e) => this.fail(problemMessages(e, 'Could not import the dashboard.').join(' ')),
        });
      },
      () => this.fail('That file could not be read.'),
    );
  }

  private fail(message: string): void {
    this.importing.set(false);
    this.importErrors.set([message]);
  }

  /** Puts a dashboard that was just made in the list, and shows it. */
  private addToList(made: Dashboard): void {
    const entry: DashboardSummary = {
      id: made.id,
      name: made.name,
      description: made.description,
      widgetCount: made.definition.widgets.filter((w) => !w.kind || w.kind === 'Chart').length,
      updatedUtc: made.updatedUtc,
      chartIds: made.definition.widgets.filter((w) => !w.kind || w.kind === 'Chart').map((w) => w.chartId),
      folderId: made.folderId ?? null,
    };
    this.dashboards.update((items) => [...items, entry]);
    this.mode.set('dashboards');
    this.select(made.id);
    this.reload();
  }

  protected onDeleted(id: string): void {
    if (this.selectedId() === id) this.selectedId.set(null);
    this.reload();
  }

  private reload(): void {
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        const first = !this.loaded();
        this.dashboards.set(items);
        this.loaded.set(true);
        // A link to one dashboard (the address a dashboard used to have) selects that dashboard.
        const linked = this.route.snapshot.paramMap.get('dashboardId');
        if (first && linked && items.some((d) => d.id === linked)) this.select(linked);
        // A dashboard that no longer exists (deleted elsewhere) is no longer shown.
        if (this.selectedId() && !items.some((d) => d.id === this.selectedId())) this.selectedId.set(null);
      },
    });
  }

  // ---- charts ---------------------------------------------------------------------------------------------

  protected selectChart(id: string): void {
    this.selectedChartId.set(id);
    this.chartTree.selectedFolderId.set(this.charts().find((c) => c.id === id)?.folderId ?? null);
  }

  /** The editor's address for a new chart: it files the chart in the folder last clicked. */
  protected newChartParams(): { folder: string } | null {
    const folder = this.chartTree.selectedFolderId();
    return folder ? { folder } : null;
  }

  /** "New chart here" in a folder's menu. */
  protected newChartIn(folderId: string | null): void {
    void this.router.navigate(['/organizations', this.orgId, 'charts', 'new'], { queryParams: folderId ? { folder: folderId } : {} });
  }

  protected onChartDeleted(id: string): void {
    if (this.selectedChartId() === id) this.selectedChartId.set(null);
    this.reloadCharts(null);
    this.reload(); // dashboards that showed it lost a chart
  }

  private reloadCharts(select: string | null): void {
    this.chartsApi.list(this.orgId).subscribe({
      next: (items) => {
        this.charts.set(items);
        this.chartsLoaded.set(true);
        if (select && items.some((c) => c.id === select)) this.selectedChartId.set(select);
        if (this.selectedChartId() && !items.some((c) => c.id === this.selectedChartId())) this.selectedChartId.set(null);
      },
    });
  }
}
