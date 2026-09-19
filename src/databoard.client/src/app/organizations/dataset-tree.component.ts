import { Component, computed, effect, input, output, signal } from '@angular/core';
import { MatMenuModule } from '@angular/material/menu';
import { TreeItem, ancestorIds, buildRows, canMoveTo } from './dataset-tree';
import { Dataset, DatasetFolder } from './datasets.service';

export interface MoveRequest {
  item: TreeItem;
  /** The folder it goes into; null is the top level. */
  targetId: string | null;
}

/** What "Add dataset" in a folder's menu asks for. */
export interface AddDatasetRequest {
  kind: 'sql' | 'view';
  folderId: string | null;
}

/**
 * The datasets of an organization as a tree of folders. It only shows and reports what the user did (open, move, add,
 * rename, delete); the page does the work. Editors can drag a row onto a folder (or the empty space, for the top
 * level), and every row also has a menu with the same actions for the keyboard.
 */
@Component({
  selector: 'app-dataset-tree',
  imports: [MatMenuModule],
  template: `
    <div class="toolbar">
      <input type="search" class="filter" placeholder="Filter datasets" aria-label="Filter datasets"
             [value]="filter()" (input)="filter.set($any($event.target).value)" />
      @if (canEdit()) {
        <button type="button" class="new-folder" title="New folder" aria-label="New folder" (click)="createFolder.emit(null)">
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h4.6l2 2h8.4A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M12 10.5v5M9.5 13h5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
        </button>
      }
    </div>

    <div class="tree" role="tree" aria-label="Datasets" [class.drop-root]="dropTarget() === ROOT"
         (keydown)="onKey($event)" (dragover)="over(null, $event)" (dragleave)="leave()" (drop)="drop(null, $event)">
      @if (rows().length === 0) {
        <p class="empty"><em>{{ filter().trim() ? 'No datasets match.' : 'No datasets yet.' }}</em></p>
      }
      @for (row of rows(); track row.kind + row.id) {
        <div class="row" role="treeitem" [attr.aria-level]="row.depth + 1"
             [attr.aria-expanded]="row.kind === 'folder' ? row.expanded : null"
             [attr.aria-selected]="row.id === activeId()"
             [class.active]="row.id === activeId()" [class.selected]="row.kind === 'folder' && row.id === selectedFolderId()"
             [class.drop]="row.kind === 'folder' && dropTarget() === row.id"
             [style.padding-left.rem]="0.25 + row.depth * 1.1"
             [attr.draggable]="canEdit() && !filter().trim() ? 'true' : null"
             (dragstart)="dragStart(row, $event)" (dragend)="dragEnd()"
             (dragover)="over(row, $event)" (drop)="drop(row, $event)">
          @if (row.kind === 'folder') {
            <button type="button" class="main folder" [attr.aria-label]="row.name" (click)="toggle(row.folder)">
              <svg class="chevron" [class.open]="row.expanded" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
              <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h4.6l2 2h8.4A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z" [attr.fill]="row.expanded ? 'none' : 'currentColor'" fill-opacity="0.25" stroke="currentColor" stroke-width="1.6"/></svg>
              <span class="name">{{ row.name }}</span>
              <span class="count">{{ row.childCount }}</span>
            </button>
          } @else {
            <button type="button" class="main item" (click)="open.emit(row.dataset)">
              <span class="spacer"></span>
              <span class="text">
                <span class="name">{{ row.name }}</span>
                <span class="sub">{{ row.dataset.columns.length }} columns · {{ row.dataset.kind === 'TableView' ? 'table view' : 'SQL' }}</span>
              </span>
            </button>
          }
          @if (canEdit()) {
            <button type="button" class="more" [attr.aria-label]="'Actions for ' + row.name"
                    [matMenuTriggerFor]="rowMenu" [matMenuTriggerData]="{ row }">⋮</button>
          }
        </div>
      }
    </div>

    <mat-menu #rowMenu="matMenu">
      <ng-template matMenuContent let-row="row">
        @if (row.kind === 'folder') {
          <button mat-menu-item type="button" (click)="createFolder.emit(row.id)">New folder here</button>
          <button mat-menu-item type="button" (click)="addDataset.emit({ kind: 'sql', folderId: row.id })">New SQL dataset here</button>
          <button mat-menu-item type="button" (click)="addDataset.emit({ kind: 'view', folderId: row.id })">New table view here</button>
          <button mat-menu-item type="button" (click)="renameFolder.emit(row.folder)">Rename</button>
        }
        <button mat-menu-item type="button" (click)="moveTo.emit({ kind: row.kind, id: row.id })">Move to…</button>
        @if (row.kind === 'folder') {
          <button mat-menu-item type="button" (click)="deleteFolder.emit(row.folder)">Delete</button>
        }
      </ng-template>
    </mat-menu>
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 0; height: 100%; }
    .toolbar { display: flex; gap: 0.4rem; align-items: center; padding-bottom: 0.5rem; }
    .filter { flex: 1; min-width: 0; padding: 0.35rem 0.5rem; border: 1px solid var(--mat-sys-outline-variant); border-radius: 0.4rem; font: inherit; background: transparent; color: inherit; }
    .new-folder, .more { border: 0; background: transparent; color: inherit; cursor: pointer; border-radius: 0.4rem; padding: 0.25rem; display: grid; place-items: center; }
    .new-folder:hover, .more:hover { background: var(--mat-sys-surface-container-high); }
    .tree { flex: 1; min-height: 6rem; overflow-y: auto; border-radius: 0.4rem; }
    .tree.drop-root { outline: 2px dashed var(--mat-sys-primary); outline-offset: -2px; }
    .empty { padding: 0.5rem; color: var(--mat-sys-on-surface-variant); }
    .row { display: flex; align-items: center; border-radius: 0.4rem; padding-right: 0.15rem; }
    .row:hover { background: var(--mat-sys-surface-container); }
    .row.active { background: var(--mat-sys-secondary-container); }
    .row.selected .name { font-weight: 600; }
    .row.drop { background: var(--mat-sys-primary-container); outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    .more { visibility: hidden; font-size: 1.1rem; line-height: 1; width: 1.6rem; }
    .row:hover .more, .row:focus-within .more, .row.active .more { visibility: visible; }
    .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 0.35rem; border: 0; background: transparent; color: inherit; font: inherit; text-align: left; padding: 0.4rem 0.25rem; cursor: pointer; }
    .main:focus-visible, .more:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .text { display: flex; flex-direction: column; min-width: 0; }
    .sub { font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .count { margin-left: auto; font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); }
    .chevron { transition: transform 0.12s; flex: none; }
    .chevron.open { transform: rotate(90deg); }
    .spacer { width: 16px; flex: none; }
    svg { flex: none; }
  `,
})
export class DatasetTreeComponent {
  protected readonly ROOT = '__root__';

  readonly folders = input.required<DatasetFolder[]>();
  readonly datasets = input.required<Dataset[]>();
  /** Which dataset's tab is showing; its folders open so it can be seen. */
  readonly activeId = input<string | null>(null);
  readonly canEdit = input(false);
  /** The folder "Add dataset" would put a new dataset in. */
  readonly selectedFolderId = input<string | null>(null);
  /** Where the open folders are remembered (per organization); empty means they are not. */
  readonly storageKey = input('');

  readonly open = output<Dataset>();
  readonly selectFolder = output<string | null>();
  /** A new folder is wanted inside this folder (null for the top level). */
  readonly createFolder = output<string | null>();
  readonly renameFolder = output<DatasetFolder>();
  readonly deleteFolder = output<DatasetFolder>();
  readonly addDataset = output<AddDatasetRequest>();
  /** A row was dropped on a folder (or the top level). */
  readonly move = output<MoveRequest>();
  /** "Move to…" was chosen in a row's menu; the page asks where to. */
  readonly moveTo = output<TreeItem>();

  protected readonly filter = signal('');
  private readonly expanded = signal<ReadonlySet<string>>(new Set());
  protected readonly dropTarget = signal<string | null>(null);
  private dragging: TreeItem | null = null;
  private restored = '';

  protected readonly rows = computed(() => buildRows(this.folders(), this.datasets(), this.expanded(), this.filter()));

  constructor() {
    // Open folders come back after a reload.
    effect(() => {
      const key = this.storageKey();
      if (!key || this.restored === key) return;
      this.restored = key;
      try {
        const saved = JSON.parse(localStorage.getItem(key) ?? 'null') as string[] | null;
        if (Array.isArray(saved)) this.expanded.set(new Set(saved));
      } catch {
        // Nothing usable stored.
      }
    });

    // The active dataset is never hidden inside a closed folder.
    effect(() => {
      const id = this.activeId();
      const dataset = this.datasets().find((d) => d.id === id);
      if (!dataset?.folderId) return;
      const path = ancestorIds(this.folders(), dataset.folderId);
      const open = this.expanded();
      if (path.every((p) => open.has(p))) return;
      this.expanded.set(new Set([...open, ...path]));
    });
  }

  protected toggle(folder: DatasetFolder): void {
    const open = new Set(this.expanded());
    if (!open.delete(folder.id)) open.add(folder.id);
    this.expanded.set(open);
    this.selectFolder.emit(folder.id);
    this.remember();
  }

  private setOpen(id: string, open: boolean): void {
    if (this.expanded().has(id) === open) return;
    const next = new Set(this.expanded());
    if (open) next.add(id);
    else next.delete(id);
    this.expanded.set(next);
    this.remember();
  }

  private remember(): void {
    const key = this.storageKey();
    if (!key) return;
    try {
      localStorage.setItem(key, JSON.stringify([...this.expanded()]));
    } catch {
      // Storage can be unavailable; the open folders just are not remembered.
    }
  }

  /** Arrow keys move between rows, and right and left open and close a folder. */
  protected onKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (!target.classList?.contains('main')) return;
    const mains = Array.from((event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('.main'));
    const index = mains.indexOf(target);
    const row = this.rows()[index];
    switch (event.key) {
      case 'ArrowDown': mains[index + 1]?.focus(); break;
      case 'ArrowUp': mains[index - 1]?.focus(); break;
      case 'ArrowRight':
        if (row?.kind === 'folder') {
          if (row.expanded) mains[index + 1]?.focus();
          else this.setOpen(row.id, true);
        }
        break;
      case 'ArrowLeft':
        if (row?.kind === 'folder' && row.expanded && !this.filter().trim()) this.setOpen(row.id, false);
        break;
      default: return;
    }
    event.preventDefault();
  }

  // ---- drag and drop ---------------------------------------------------------------------------------------

  protected dragStart(row: { kind: 'folder' | 'dataset'; id: string }, event: DragEvent): void {
    if (!this.canEdit() || this.filter().trim()) {
      event.preventDefault();
      return;
    }
    this.dragging = { kind: row.kind, id: row.id };
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', row.id);
    }
  }

  protected dragEnd(): void {
    this.dragging = null;
    this.dropTarget.set(null);
  }

  /** A folder is dropped on as itself; a dataset row stands for the folder it is in. */
  private targetOf(row: { kind: 'folder' | 'dataset'; id: string; dataset?: Dataset } | null): string | null {
    if (!row) return null;
    return row.kind === 'folder' ? row.id : (row.dataset?.folderId ?? null);
  }

  protected over(row: { kind: 'folder' | 'dataset'; id: string; dataset?: Dataset } | null, event: DragEvent): void {
    event.stopPropagation();
    if (!this.dragging) return;
    const target = this.targetOf(row);
    if (!canMoveTo(this.folders(), this.datasets(), this.dragging, target)) {
      this.dropTarget.set(null);
      return;
    }
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    this.dropTarget.set(target ?? this.ROOT);
  }

  protected leave(): void {
    this.dropTarget.set(null);
  }

  protected drop(row: { kind: 'folder' | 'dataset'; id: string; dataset?: Dataset } | null, event: DragEvent): void {
    event.stopPropagation();
    const item = this.dragging;
    const target = this.targetOf(row);
    this.dragEnd();
    if (!item || !canMoveTo(this.folders(), this.datasets(), item, target)) return;
    event.preventDefault();
    this.move.emit({ item, targetId: target });
    if (target) this.setOpen(target, true);
  }
}
