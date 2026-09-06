import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemMessages } from './data-sources.service';
import { Dataset, DatasetsService, TableViewDefinition } from './datasets.service';

export interface SaveDatasetDialogData {
  orgId: string;
  dataSourceId: string;
  /** What to save: SQL for a SQL dataset, or the definition of a table view (the server generates its SQL). */
  sql?: string;
  definition?: TableViewDefinition;
}

/** Asks for the name of a new dataset and saves it. Closes with the created dataset, or with nothing if cancelled. */
@Component({
  selector: 'app-save-dataset-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Save dataset</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <div class="fields">
          <mat-form-field><mat-label>Name</mat-label><input matInput formControlName="name" aria-label="Dataset name" cdkFocusInitial /></mat-form-field>
          <mat-form-field>
            <mat-label>Description (optional)</mat-label>
            <input matInput formControlName="description" aria-label="Description" />
          </mat-form-field>
        </div>

        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Save dataset</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields { display: grid; padding-top: 0.5rem; }
  `,
})
export class SaveDatasetDialogComponent {
  private readonly data = inject<SaveDatasetDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SaveDatasetDialogComponent, Dataset>>(MatDialogRef);
  private readonly api = inject(DatasetsService);
  private readonly fb = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(1000)],
  });

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);

    const v = this.form.getRawValue();
    const { orgId, dataSourceId, sql, definition } = this.data;
    const request = definition
      ? this.api.createTableView(orgId, dataSourceId, v.name, definition, v.description || undefined)
      : this.api.create(orgId, dataSourceId, v.name, sql ?? '', v.description || undefined);
    request.subscribe({
      next: (created) => this.ref.close(created),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the dataset.'));
      },
    });
  }
}
