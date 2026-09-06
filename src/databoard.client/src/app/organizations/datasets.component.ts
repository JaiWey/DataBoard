import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { ActivatedRoute } from '@angular/router';
import { TabStripComponent } from '../shared/tab-strip.component';
import { DatasetTabComponent } from './dataset-tab.component';
import { NewDatasetTabComponent } from './new-dataset-tab.component';
import { NewTableViewTabComponent } from './new-table-view-tab.component';
import { Dataset, DatasetsService } from './datasets.service';

const NEW_PREFIX = 'new:';
const NEW_VIEW_PREFIX = 'new:view:';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

/** Datasets of an organization: a list on the left, and the datasets opened from it as tabs on the right. */
@Component({
  selector: 'app-datasets',
  imports: [MatCardModule, MatListModule, MatButtonModule, MatMenuModule, TabStripComponent, DatasetTabComponent, NewDatasetTabComponent, NewTableViewTabComponent],
  template: `
    <h2>{{ organization()?.name }} <small>datasets</small></h2>

    <div class="layout">
      <mat-card appearance="raised" class="list">
        <mat-card-header>
          <mat-card-title>Datasets</mat-card-title>
          @if (canEdit()) {
            <button mat-flat-button type="button" class="add" [matMenuTriggerFor]="addMenu">Add dataset</button>
            <mat-menu #addMenu="matMenu">
              <button mat-menu-item type="button" (click)="addDataset('sql')">SQL dataset</button>
              <button mat-menu-item type="button" (click)="addDataset('view')">Table view dataset</button>
            </mat-menu>
          }
        </mat-card-header>
        <mat-card-content>
          @if (loaded() && datasets().length === 0) {
            <p><em>No datasets yet.</em></p>
          }
          <mat-action-list>
            @for (item of datasets(); track item.id) {
              <button mat-list-item type="button" class="item" [activated]="activeId() === item.id" (click)="open(item)">
                <span matListItemTitle>{{ item.name }}</span>
                <span matListItemLine>{{ item.columns.length }} columns · {{ item.kind === 'TableView' ? 'table view' : 'SQL' }}</span>
              </button>
            }
          </mat-action-list>
        </mat-card-content>
      </mat-card>

      <section class="workspace">
        @if (tabs().length > 0) {
          <app-tab-strip label="Open datasets" [tabs]="tabItems()" [activeId]="activeId()"
                         (select)="activeId.set($event)" (close)="close($event)" />
          @for (tab of tabs(); track tab.id) {
            @if (tab.dataset; as dataset) {
              <app-dataset-tab [dataset]="dataset" [orgId]="orgId" [canEdit]="canEdit()" [active]="tab.id === activeId()"
                               (updated)="replace($event)" (deleted)="onDeleted($event)" (dirtyChange)="setDirty(tab.id, $event)" />
            } @else if (tab.id.startsWith(NEW_VIEW_PREFIX)) {
              <app-new-table-view-tab [orgId]="orgId" [active]="tab.id === activeId()"
                                      (created)="onCreated(tab.id, $event)" (dirtyChange)="setDirty(tab.id, $event)" />
            } @else {
              <app-new-dataset-tab [orgId]="orgId" [active]="tab.id === activeId()"
                                   (created)="onCreated(tab.id, $event)" (dirtyChange)="setDirty(tab.id, $event)" />
            }
          }
        } @else {
          <mat-card appearance="raised" class="detail empty">
            <mat-card-content class="placeholder">
              <p><em>Select a dataset to see its details.</em></p>
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
    .workspace > app-dataset-tab, .workspace > app-new-dataset-tab { flex: 1; min-height: 0; }
    .workspace > .empty { flex: 1; }
    @media (max-width: 52rem) {
      .layout { grid-template-columns: 1fr; height: auto; }
      .list { height: auto; max-height: 20rem; }
      .workspace > app-dataset-tab, .workspace > app-new-dataset-tab { min-height: 30rem; }
    }
    mat-card-title { font: var(--mat-sys-title-medium); }
    .add { margin-left: auto; }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    mat-card-content { padding-top: 0.75rem; }
    .placeholder { flex: 1; display: grid; place-items: center; text-align: center; color: var(--mat-sys-on-surface-variant); }

  `,
})
export class DatasetsComponent implements OnInit {
  protected readonly NEW_VIEW_PREFIX = NEW_VIEW_PREFIX;
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DatasetsService);

  protected orgId = '';
  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly datasets = signal<Dataset[]>([]);
  protected readonly loaded = signal(false);

  /** Ids of the open tabs, left to right, and which one is showing. */
  protected readonly openIds = signal<string[]>([]);
  protected readonly activeId = signal<string | null>(null);
  protected readonly dirtyIds = signal<ReadonlySet<string>>(new Set());
  /** An open tab is a saved dataset, or an unsaved new one (its id starts with "new:" and it has no dataset). */
  protected readonly tabs = computed(() =>
    this.openIds().flatMap((id): { id: string; name: string; dataset: Dataset | null }[] => {
      if (id.startsWith(NEW_PREFIX)) return [{ id, name: id.startsWith(NEW_VIEW_PREFIX) ? 'New table view' : 'New dataset', dataset: null }];
      const dataset = this.datasets().find((d) => d.id === id);
      return dataset ? [{ id, name: dataset.name, dataset }] : [];
    }),
  );
  protected readonly tabItems = computed(() => this.tabs().map((t) => ({ id: t.id, name: t.name, dirty: this.dirtyIds().has(t.id) })));
  private newCount = 0;

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

  /** Opens a dataset in a tab, or shows its tab if it is already open. */
  protected open(dataset: Dataset): void {
    if (!this.openIds().includes(dataset.id)) this.openIds.update((ids) => [...ids, dataset.id]);
    this.activeId.set(dataset.id);
    this.remember();
  }

  protected close(id: string): void {
    if (this.dirtyIds().has(id) && !confirm('This dataset has unsaved changes. Close it anyway?')) return;
    this.drop(id);
  }

  protected setDirty(id: string, dirty: boolean): void {
    this.dirtyIds.update((set) => {
      if (set.has(id) === dirty) return set;
      const next = new Set(set);
      if (dirty) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  /** A saved dataset replaces its old copy in the list, which keeps its tab and name current. */
  protected replace(saved: Dataset): void {
    this.datasets.update((items) => items.map((d) => (d.id === saved.id ? saved : d)));
  }

  protected onDeleted(id: string): void {
    this.drop(id);
    this.reload();
  }

  /** Opens an empty editor (or the table view builder) in a new tab; the dataset is only created when that tab is saved. */
  protected addDataset(kind: 'sql' | 'view'): void {
    const id = kind === 'view' ? `${NEW_VIEW_PREFIX}${++this.newCount}` : `${NEW_PREFIX}sql:${++this.newCount}`;
    this.openIds.update((ids) => [...ids, id]);
    this.activeId.set(id);
  }

  /** The new-dataset tab was saved: it becomes the tab of the created dataset, in the same place. */
  protected onCreated(tabId: string, created: Dataset): void {
    this.datasets.update((items) => [...items, created]);
    this.openIds.update((ids) => ids.map((id) => (id === tabId ? created.id : id)));
    this.setDirty(tabId, false);
    this.activeId.set(created.id);
    this.remember();
    this.reload();
  }

  /** Removes a tab; if it was showing, its right neighbour (or else its left) takes over. */
  private drop(id: string): void {
    const ids = this.openIds();
    const index = ids.indexOf(id);
    if (index < 0) return;
    const remaining = ids.filter((i) => i !== id);
    this.openIds.set(remaining);
    this.setDirty(id, false);
    if (this.activeId() === id) this.activeId.set(remaining[index] ?? remaining[index - 1] ?? null);
    this.remember();
  }

  private get storageKey(): string {
    return `databoard.datasetTabs.${this.orgId}`;
  }

  /** Remembers which tabs are open, per organization, so a reload brings them back. */
  private remember(): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify({ open: this.openIds().filter((id) => !id.startsWith(NEW_PREFIX)), active: this.activeId() }));
    } catch {
      // Storage can be unavailable (private windows, blocked site data); the tabs just are not remembered.
    }
  }

  private restore(items: Dataset[]): void {
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
        this.datasets.set(items);
        this.loaded.set(true);
        if (first) this.restore(items);
        // Tabs of datasets that no longer exist (deleted elsewhere) go away.
        const gone = this.openIds().filter((id) => !id.startsWith(NEW_PREFIX) && !items.some((d) => d.id === id));
        gone.forEach((id) => this.drop(id));
      },
    });
  }
}
