import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { DataSource, DataSourcesService, problemMessages } from './data-sources.service';
import { Dataset, DatasetsService } from './datasets.service';

export interface AddDatasetDialogData {
  orgId: string;
}

/** Closes with the created dataset, or with nothing if the user cancelled. */
@Component({
  selector: 'app-add-dataset-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Add a dataset</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        @if (loaded() && sources().length === 0) {
          <p class="fail" role="alert">Add a data source first; a dataset is a saved query on one.</p>
        }
        <div class="fields">
          <mat-form-field>
            <mat-label>Data source</mat-label>
            <select matNativeControl formControlName="dataSourceId" aria-label="Data source">
              @for (s of sources(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
            </select>
          </mat-form-field>
          <mat-form-field><mat-label>Name</mat-label><input matInput formControlName="name" aria-label="Dataset name" /></mat-form-field>
          <mat-form-field class="wide">
            <mat-label>Description (optional)</mat-label>
            <input matInput formControlName="description" aria-label="Description" />
          </mat-form-field>
          <mat-form-field class="wide">
            <mat-label>SQL query</mat-label>
            <textarea matInput formControlName="sql" rows="6" spellcheck="false" aria-label="SQL query"
                      placeholder="SELECT region, amount FROM sales"></textarea>
            <mat-hint>One read-only SELECT. Give every column a unique name.</mat-hint>
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
    .fields { display: grid; grid-template-columns: 1fr 1fr; column-gap: 1rem; padding-top: 0.5rem; }
    .wide { grid-column: 1 / -1; }
    @media (max-width: 36rem) { .fields { grid-template-columns: 1fr; } }
    textarea { font-family: monospace; }
  `,
})
export class AddDatasetDialogComponent implements OnInit {
  private readonly data = inject<AddDatasetDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<AddDatasetDialogComponent, Dataset>>(MatDialogRef);
  private readonly sourcesApi = inject(DataSourcesService);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly fb = inject(FormBuilder);

  protected readonly sources = signal<DataSource[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    dataSourceId: ['', Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(1000)],
    sql: ['', Validators.required],
  });

  ngOnInit(): void {
    this.sourcesApi.list(this.data.orgId).subscribe({
      next: (items) => {
        this.sources.set(items);
        this.loaded.set(true);
        if (items.length > 0) this.form.controls.dataSourceId.setValue(items[0].id);
      },
      error: (e) => {
        this.loaded.set(true);
        this.errors.set(problemMessages(e, 'Could not load the data sources.'));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);

    const v = this.form.getRawValue();
    this.datasetsApi.create(this.data.orgId, v.dataSourceId, v.name, v.sql, v.description || undefined).subscribe({
      next: (created) => this.ref.close(created),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the dataset.'));
      },
    });
  }
}
