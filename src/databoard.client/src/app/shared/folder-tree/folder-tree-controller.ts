import { inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { problemMessages } from '../../organizations/data-sources.service';
import { FolderNameDialogComponent, FolderNameDialogData, MoveDialogComponent, MoveDialogData } from './folder-dialogs.component';
import { TreeEntry, TreeItem, folderPath } from './folder-tree';
import { Folder, FolderKind, FoldersService } from './folders.service';
import { ConfirmService } from '../confirm.service';

export interface FolderTreeConfig {
  orgId: () => string;
  kind: FolderKind;
  /** The things in the tree (datasets, charts or dashboards), for the Move to… dialog. */
  entries: () => TreeEntry[];
  /** Files one of them in a folder (null for the top level). The page keeps its own list current from the result. */
  moveEntry: (id: string, folderId: string | null) => Observable<unknown>;
}

/**
 * What a page with a folder tree does besides showing it: it loads the folders, and makes, renames, moves and deletes
 * them. Create it in a field initializer (it injects what it needs), and bind the tree to its signals and methods.
 */
export class FolderTreeController {
  private readonly api = inject(FoldersService);
  private readonly confirms = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  readonly folders = signal<Folder[]>([]);
  /** The folder a new item goes in: the last folder clicked, or the folder of the item last opened (null is the top level). */
  readonly selectedFolderId = signal<string | null>(null);
  /** Problems with moving or deleting something in the tree. */
  readonly errors = signal<string[]>([]);

  constructor(private readonly config: FolderTreeConfig) {}

  load(): void {
    this.api.list(this.config.orgId(), this.config.kind).subscribe({ next: (items) => this.folders.set(items) });
  }

  newFolder(parentId: string | null): void {
    this.dialog
      .open<FolderNameDialogComponent, FolderNameDialogData, Folder>(FolderNameDialogComponent, {
        data: { orgId: this.config.orgId(), kind: this.config.kind, parentId }, width: '24rem', maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) this.folders.update((items) => [...items, created]);
      });
  }

  renameFolder(folder: Folder): void {
    this.dialog
      .open<FolderNameDialogComponent, FolderNameDialogData, Folder>(FolderNameDialogComponent, {
        data: { orgId: this.config.orgId(), kind: this.config.kind, parentId: folder.parentId, folder }, width: '24rem', maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) this.folders.update((items) => items.map((f) => (f.id === saved.id ? saved : f)));
      });
  }

  deleteFolder(folder: Folder): void {
    this.confirms
      .ask({ title: 'Delete folder', message: `Delete the folder "${folder.name}"?`, confirmLabel: 'Delete', danger: true })
      .subscribe((yes) => {
        if (yes) this.deleteConfirmed(folder);
      });
  }

  private deleteConfirmed(folder: Folder): void {
    this.errors.set([]);
    this.api.remove(this.config.orgId(), folder.id).subscribe({
      next: () => {
        this.folders.update((items) => items.filter((f) => f.id !== folder.id));
        if (this.selectedFolderId() === folder.id) this.selectedFolderId.set(folder.parentId);
      },
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the folder.')),
    });
  }

  /** The "Move to…" menu entry: asks where to, then moves. */
  chooseDestination(item: TreeItem): void {
    this.dialog
      .open<MoveDialogComponent, MoveDialogData, { targetId: string | null }>(MoveDialogComponent, {
        data: { folders: this.folders(), entries: this.config.entries(), item }, width: '26rem', maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((chosen) => {
        if (chosen) this.moveItem(item, chosen.targetId);
      });
  }

  /** Files an item or a folder in another folder (null is the top level). Used by dropping and by the Move to… dialog. */
  moveItem(item: TreeItem, targetId: string | null): void {
    this.errors.set([]);
    const fail = (e: unknown) =>
      this.errors.set(problemMessages(e, `Could not move it to ${targetId ? folderPath(this.folders(), targetId) : 'the top level'}.`));
    if (item.kind === 'item') {
      this.config.moveEntry(item.id, targetId).subscribe({ error: fail });
      return;
    }
    const folder = this.folders().find((f) => f.id === item.id);
    if (!folder) return;
    this.api.update(this.config.orgId(), folder.id, folder.name, targetId).subscribe({
      next: (saved) => this.folders.update((items) => items.map((f) => (f.id === saved.id ? saved : f))),
      error: fail,
    });
  }
}
