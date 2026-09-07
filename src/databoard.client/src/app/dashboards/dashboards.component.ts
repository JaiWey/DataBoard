import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { ActivatedRoute } from '@angular/router';
import { DashboardComponent } from './dashboard.component';
import { NewDashboardDialogComponent, NewDashboardDialogData } from './new-dashboard-dialog.component';
import { Dashboard, DashboardSummary, DashboardsService } from './dashboards.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

/** Dashboards of an organization: a list on the left, and the selected dashboard on the right. */
@Component({
  selector: 'app-dashboards',
  imports: [MatCardModule, MatListModule, MatButtonModule, DashboardComponent],
  template: `
    <h2>{{ organization()?.name }} <small>dashboards</small></h2>

    <div class="layout">
      <mat-card appearance="raised" class="list">
        <mat-card-header>
          <mat-card-title>Dashboards</mat-card-title>
          @if (canEdit()) {
            <button mat-flat-button type="button" class="add" (click)="openNewDialog()">New dashboard</button>
          }
        </mat-card-header>
        <mat-card-content>
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
        </mat-card-content>
      </mat-card>

      <section class="workspace">
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
    .workspace > app-dashboard { flex: 1; min-height: 0; }
    .workspace > .empty { flex: 1; }
    @media (max-width: 52rem) {
      .layout { grid-template-columns: 1fr; height: auto; }
      .list { height: auto; max-height: 20rem; }
      .workspace > app-dashboard { min-height: 30rem; }
    }
    mat-card-title { font: var(--mat-sys-title-medium); }
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
  private readonly dialog = inject(MatDialog);

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly dashboards = signal<DashboardSummary[]>([]);
  protected readonly loaded = signal(false);

  protected readonly selectedId = signal<string | null>(null);
  /** Zero or one entries, so the detail is rebuilt (filters and results reset) whenever another dashboard is picked. */
  protected readonly selected = computed(() => {
    const dashboard = this.dashboards().find((d) => d.id === this.selectedId());
    return dashboard ? [dashboard] : [];
  });

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

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
        };
        this.dashboards.update((items) => [...items, entry]);
        this.select(created.id);
        this.reload();
      });
  }

  /** A saved dashboard keeps its list entry (name and chart count) current. */
  protected onChanged(saved: Dashboard): void {
    this.dashboards.update((items) =>
      items.map((d) => (d.id === saved.id ? { ...d, name: saved.name, widgetCount: saved.definition.widgets.length } : d)),
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
        // A link to one dashboard (the address "Open dashboard" leads to) selects that dashboard.
        const linked = this.route.snapshot.paramMap.get('dashboardId');
        if (first && linked && items.some((d) => d.id === linked)) this.select(linked);
        // A dashboard that no longer exists (deleted elsewhere) is no longer shown.
        if (this.selectedId() && !items.some((d) => d.id === this.selectedId())) this.selectedId.set(null);
      },
    });
  }
}
