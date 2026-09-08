import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CHART_TYPES, Chart, ChartsService } from '../charts/charts.service';
import { DatasetsService } from '../organizations/datasets.service';
import { ChartCardComponent } from './chart-card.component';
import { DashboardComponent } from './dashboard.component';
import { NewDashboardDialogComponent, NewDashboardDialogData } from './new-dashboard-dialog.component';
import { Dashboard, DashboardSummary, DashboardsService } from './dashboards.service';

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
  imports: [RouterLink, MatCardModule, MatListModule, MatButtonModule, MatButtonToggleModule, DashboardComponent, ChartCardComponent],
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
              <button mat-flat-button type="button" class="add" (click)="openNewDialog()">New dashboard</button>
            } @else {
              <a mat-flat-button class="add" [routerLink]="['/organizations', orgId, 'charts', 'new']">New chart</a>
            }
          }
        </mat-card-header>
        <mat-card-content>
          @if (mode() === 'dashboards') {
            @if (loaded() && dashboards().length === 0) {
              <p><em>No dashboards yet.</em></p>
            }
            <mat-action-list>
              @for (item of dashboards(); track item.id) {
                <button mat-list-item type="button" class="item" [activated]="selectedId() === item.id" (click)="select(item.id)">
                  <span matListItemTitle>{{ item.name }}</span>
                  <span matListItemLine>{{ item.widgetCount }} {{ item.widgetCount === 1 ? 'chart' : 'charts' }}</span>
                </button>
              }
            </mat-action-list>
          } @else {
            @if (chartsLoaded() && charts().length === 0) {
              <p><em>No charts yet.</em></p>
            }
            <mat-action-list>
              @for (item of charts(); track item.id) {
                <button mat-list-item type="button" class="item" [activated]="selectedChartId() === item.id" (click)="selectChart(item.id)">
                  <span matListItemTitle>{{ item.name }}</span>
                  <span matListItemLine>{{ typeLabel(item.type) }} · {{ datasetNames()[item.datasetId] ?? 'unknown dataset' }}</span>
                </button>
              }
            </mat-action-list>
          }
        </mat-card-content>
      </mat-card>

      <section class="workspace">
        @if (mode() === 'dashboards') {
          @for (dashboard of selected(); track dashboard.id) {
            <app-dashboard [orgId]="orgId" [dashboardId]="dashboard.id" [canEdit]="canEdit()"
                           (changed)="onChanged($event)" (deleted)="onDeleted($event)" />
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
    .list mat-card-content { flex: 1; overflow-y: auto; }
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

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });

  protected readonly mode = signal<Mode>('dashboards');

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
  }

  protected openNewDialog(): void {
    this.dialog
      .open<NewDashboardDialogComponent, NewDashboardDialogData, Dashboard>(NewDashboardDialogComponent, {
        data: { orgId: this.orgId },
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
          ? { ...d, name: saved.name, widgetCount: saved.definition.widgets.length, chartIds: saved.definition.widgets.map((w) => w.chartId) }
          : d,
      ),
    );
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
