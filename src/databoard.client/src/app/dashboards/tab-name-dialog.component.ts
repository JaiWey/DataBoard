import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MAX_TAB_NAME_LENGTH } from './dashboard-tabs';

export interface TabNameDialogData {
  /** The name to change; missing means a new tab. */
  name?: string;
  /** The names the other tabs have, which this one cannot take. */
  taken: string[];
}

/** Asks for the name of a tab. Closes with the name, or with nothing if cancelled. */
@Component({
  selector: 'app-tab-name-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.name === undefined ? 'New tab' : 'Rename tab' }}</h2>
    <form (submit)="$event.preventDefault(); save()">
      <mat-dialog-content>
        <label class="field">
          <span>Name</span>
          <input type="text" aria-label="Tab name" [maxLength]="max" [value]="name()" (input)="name.set($any($event.target).value)" cdkFocusInitial />
        </label>
        @if (problem()) {
          <p class="fail" role="alert">{{ problem() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="!valid()">{{ data.name === undefined ? 'Add tab' : 'Rename' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .field { display: grid; gap: 0.25rem; padding-top: 0.5rem; }
    input { padding: 0.45rem 0.5rem; font: inherit; }
  `,
})
export class TabNameDialogComponent {
  protected readonly data = inject<TabNameDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TabNameDialogComponent, string>>(MatDialogRef);

  protected readonly max = MAX_TAB_NAME_LENGTH;
  protected readonly name = signal(this.data.name ?? '');

  protected readonly problem = computed(() => {
    const value = this.name().trim();
    if (value && this.data.taken.some((t) => t.trim().toLowerCase() === value.toLowerCase())) return 'Another tab already has that name.';
    return '';
  });
  protected readonly valid = computed(() => this.name().trim().length > 0 && !this.problem());

  protected save(): void {
    if (this.valid()) this.ref.close(this.name().trim());
  }
}
