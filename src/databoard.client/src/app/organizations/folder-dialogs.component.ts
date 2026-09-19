import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemMessages } from './data-sources.service';
import { TreeItem, canMoveTo, childFolders } from './dataset-tree';
import { Dataset, DatasetFolder, DatasetFoldersService } from './datasets.service';

export interface FolderNameDialogData {
  orgId: string;
  /** Where a new folder goes (null is the top level). Ignored when renaming. */
  parentId: string | null;
  /** The folder to rename; missing means a new folder is being made. */
  folder?: DatasetFolder;
}

/** Asks for a folder's name and creates the folder, or renames it. Closes with the saved folder, or with nothing if cancelled. */
@Component({
  selector: 'app-folder-name-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>{{ data.folder ? 'Rename folder' : 'New folder' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <mat-form-field class="name">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" aria-label="Folder name" cdkFocusInitial />
        </mat-form-field>
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ data.folder ? 'Rename' : 'Create folder' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .name { width: 100%; padding-top: 0.5rem; }
  `,
})
export class FolderNameDialogComponent {
  protected readonly data = inject<FolderNameDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<FolderNameDialogComponent, DatasetFolder>>(MatDialogRef);
  private readonly api = inject(DatasetFoldersService);
  private readonly fb = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);
  protected readonly form = this.fb.nonNullable.group({
    name: [this.data.folder?.name ?? '', [Validators.required, Validators.maxLength(100), Validators.pattern(/^(?!\s*$)[^/\\]+$/)]],
  });

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);

    const name = this.form.getRawValue().name.trim();
    const { orgId, parentId, folder } = this.data;
    const request = folder ? this.api.update(orgId, folder.id, name, folder.parentId) : this.api.create(orgId, name, parentId);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the folder.'));
      },
    });
  }
}

export interface MoveDialogData {
  folders: DatasetFolder[];
  datasets: Dataset[];
  item: TreeItem;
}

/** Closes with `{ targetId }` (null is the top level), or with nothing if cancelled. */
@Component({
  selector: 'app-move-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Move “{{ itemName() }}” to…</h2>
    <mat-dialog-content>
      <div role="radiogroup" aria-label="Destination folder" class="options">
        @for (option of options(); track option.id) {
          <label class="option" [class.disabled]="!option.allowed" [style.padding-left.rem]="0.5 + option.depth * 1.1">
            <input type="radio" name="target" [value]="option.id" [disabled]="!option.allowed"
                   [checked]="chosen() === option.id" (change)="chosen.set(option.id)" />
            <span>{{ option.name }}</span>
          </label>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="chosen() === undefined" (click)="move()">Move</button>
    </mat-dialog-actions>
  `,
  styles: `
    .options { display: flex; flex-direction: column; min-width: 18rem; max-height: 50vh; }
    .option { display: flex; align-items: center; gap: 0.5rem; padding: 0.35rem 0.5rem; border-radius: 0.4rem; cursor: pointer; }
    .option:hover:not(.disabled) { background: var(--mat-sys-surface-container); }
    .option.disabled { color: var(--mat-sys-on-surface-variant); opacity: 0.6; cursor: default; }
  `,
})
export class MoveDialogComponent {
  private readonly data = inject<MoveDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<MoveDialogComponent, { targetId: string | null }>>(MatDialogRef);

  /** The folder picked; undefined until one is. The top level is null. */
  protected readonly chosen = signal<string | null | undefined>(undefined);

  protected readonly itemName = computed(() => {
    const { item, folders, datasets } = this.data;
    return (item.kind === 'folder' ? folders : datasets).find((x) => x.id === item.id)?.name ?? '';
  });

  /** The top level, then every folder in tree order. A folder that cannot be the destination is shown but cannot be picked. */
  protected readonly options = computed(() => {
    const { folders, datasets, item } = this.data;
    const out: { id: string | null; name: string; depth: number; allowed: boolean }[] = [
      { id: null, name: 'Top level', depth: 0, allowed: canMoveTo(folders, datasets, item, null) },
    ];
    const visit = (parentId: string | null, depth: number): void => {
      for (const f of childFolders(folders, parentId)) {
        out.push({ id: f.id, name: f.name, depth, allowed: canMoveTo(folders, datasets, item, f.id) });
        visit(f.id, depth + 1);
      }
    };
    visit(null, 1);
    return out;
  });

  protected move(): void {
    const targetId = this.chosen();
    if (targetId !== undefined) this.ref.close({ targetId });
  }
}
