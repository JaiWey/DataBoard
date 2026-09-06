import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface FieldsDialogData {
  table: string;
  columns: { name: string; dataType: string }[];
  selected: string[];
}

/** Pick which fields of a table appear in the dataset. Closes with the chosen names (in the table's own order). */
@Component({
  selector: 'app-fields-dialog',
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Fields of {{ data.table }}</h2>
    <mat-dialog-content>
      <mat-form-field class="search">
        <mat-label>Search fields</mat-label>
        <input matInput aria-label="Search fields" [value]="query()" (input)="query.set($any($event.target).value)" />
      </mat-form-field>
      <p class="bulk">
        <button mat-button type="button" (click)="selectShown(true)">Select all</button>
        <button mat-button type="button" (click)="selectShown(false)">Clear</button>
        <small>{{ chosen().size }} of {{ data.columns.length }} selected</small>
      </p>
      <ul class="list">
        @for (column of shown(); track column.name) {
          <li>
            <mat-checkbox [checked]="chosen().has(column.name)" (change)="toggle(column.name, $event.checked)">
              {{ column.name }} <small>{{ column.dataType }}</small>
            </mat-checkbox>
          </li>
        } @empty {
          <li><em>No field matches.</em></li>
        }
      </ul>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" (click)="done()">Done</button>
    </mat-dialog-actions>
  `,
  styles: `
    .search { width: 100%; }
    .bulk { display: flex; align-items: center; gap: 0.5rem; margin: 0; }
    .bulk small { margin-left: auto; color: var(--mat-sys-on-surface-variant); }
    .list { list-style: none; margin: 0; padding: 0; max-height: 18rem; overflow-y: auto; }
    small { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class FieldsDialogComponent {
  protected readonly data = inject<FieldsDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<FieldsDialogComponent, string[]>>(MatDialogRef);

  protected readonly query = signal('');
  protected readonly chosen = signal<ReadonlySet<string>>(new Set(this.data.selected));
  protected readonly shown = computed(() => {
    const q = this.query().trim().toLowerCase();
    return q ? this.data.columns.filter((c) => c.name.toLowerCase().includes(q)) : this.data.columns;
  });

  protected toggle(name: string, checked: boolean): void {
    this.chosen.update((set) => {
      const next = new Set(set);
      if (checked) next.add(name);
      else next.delete(name);
      return next;
    });
  }

  /** Acts on what the search currently shows, so "Select all" after a search picks only the matches. */
  protected selectShown(checked: boolean): void {
    this.chosen.update((set) => {
      const next = new Set(set);
      for (const column of this.shown()) {
        if (checked) next.add(column.name);
        else next.delete(column.name);
      }
      return next;
    });
  }

  protected done(): void {
    this.ref.close(this.data.columns.map((c) => c.name).filter((name) => this.chosen().has(name)));
  }
}
