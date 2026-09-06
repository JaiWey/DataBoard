import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { ActivatedRoute } from '@angular/router';
import { TabStripComponent } from '../shared/tab-strip.component';
import { DashboardComponent } from './dashboard.component';
import { NewDashboardDialogComponent, NewDashboardDialogData } from './new-dashboard-dialog.component';
import { Dashboard, DashboardSummary, DashboardsService } from './dashboards.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

/** Dashboards of an organization: a list on the left, and the dashboards opened from it as tabs on the right. */
@Component({
  selector: 'app-dashboards',
  imports: [MatCardModule, MatListModule, MatButtonModule, TabStripComponent, DashboardComponent],
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
              <button mat-list-item type="button" class="item" [activated]="activeId() === item.id" (click)="open(item.id)">
                <span matListItemTitle>{{ item.name }}</span>
                <span matListItemLine>{{ item.widgetCount }} {{ item.widgetCount === 1 ? 'chart' : 'charts' }}</span>
              </button>
            }
          </mat-action-list>
        </mat-card-content>
      </mat-card>

      <section class="workspace">
        @if (tabs().length > 0) {
          <app-tab-strip label="Open dashboards" [tabs]="tabs()" [activeId]="activeId()"
                         (select)="activeId.set($event)" (close)="close($event)" />
          @for (tab of tabs(); track tab.id) {
            <app-dashboard [orgId]="orgId" [dashboardId]="tab.id" [canEdit]="canEdit()" [active]="tab.id === activeId()"
                           (changed)="onChanged($event)" (deleted)="onDeleted($event)" />
          }
        } @else {
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

  /** Ids of the open tabs, left to right, and which one is showing. */
  protected readonly openIds = signal<string[]>([]);
  protected readonly activeId = signal<string | null>(null);
  protected readonly tabs = computed(() =>
    this.openIds().flatMap((id) => {
      const dashboard = this.dashboards().find((d) => d.id === id);
      return dashboard ? [{ id, name: dashboard.name }] : [];
    }),
  );

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

  /** Opens a dashboard in a tab, or shows its tab if it is already open. */
  protected open(id: string): void {
    if (!this.openIds().includes(id)) this.openIds.update((ids) => [...ids, id]);
    this.activeId.set(id);
    this.remember();
  }

  protected close(id: string): void {
    this.drop(id);
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
        this.open(created.id);
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
    this.drop(id);
    this.reload();
  }

  /** Removes a tab; if it was showing, its right neighbour (or else its left) takes over. */
  private drop(id: string): void {
    const ids = this.openIds();
    const index = ids.indexOf(id);
    if (index < 0) return;
    const remaining = ids.filter((i) => i !== id);
    this.openIds.set(remaining);
    if (this.activeId() === id) this.activeId.set(remaining[index] ?? remaining[index - 1] ?? null);
    this.remember();
  }

  private get storageKey(): string {
    return `databoard.dashboardTabs.${this.orgId}`;
  }

  /** Remembers which tabs are open, per organization, so a reload brings them back. */
  private remember(): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify({ open: this.openIds(), active: this.activeId() }));
    } catch {
      // Storage can be unavailable (private windows, blocked site data); the tabs just are not remembered.
    }
  }

  private restore(items: DashboardSummary[]): void {
    try {
      const saved = JSON.parse(localStorage.getItem(this.storageKey) ?? 'null') as { open?: string[]; active?: string | null } | null;
      const open = (saved?.open ?? []).filter((id) => items.some((d) => d.id === id));
      this.openIds.set(open);
      this.activeId.set(open.includes(saved?.active ?? '') ? (saved?.active ?? null) : (open[0] ?? null));
    } catch {
      // Nothing usable stored.
    }
  }

  private reload(): void {
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        const first = !this.loaded();
        this.dashboards.set(items);
        this.loaded.set(true);
        if (first) {
          this.restore(items);
          // A link to one dashboard (the old per-dashboard address) opens that dashboard.
          const linked = this.route.snapshot.paramMap.get('dashboardId');
          if (linked && items.some((d) => d.id === linked)) this.open(linked);
        }
        // Tabs of dashboards that no longer exist (deleted elsewhere) go away.
        this.openIds().filter((id) => !items.some((d) => d.id === id)).forEach((id) => this.drop(id));
      },
    });
  }
}
