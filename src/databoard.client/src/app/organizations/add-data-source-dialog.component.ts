import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { buildPostgresConnectionString } from './connection-string';
import {
  CSV_DELIMITERS,
  ConnectionTestResult,
  CsvUploadResult,
  DataSource,
  DataSourcesService,
  problemMessages,
} from './data-sources.service';

export interface AddDataSourceDialogData {
  orgId: string;
}

type Kind = 'postgres' | 'csv';

/** Closes with the created data source, or with nothing if the user cancelled. */
@Component({
  selector: 'app-add-data-source-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Add a data source</h2>
    <mat-dialog-content>
      <mat-form-field class="wide">
        <mat-label>Type</mat-label>
        <select matNativeControl aria-label="Type" [value]="kind()" (change)="setKind($any($event.target).value)" [disabled]="uploaded() !== null">
          <option value="postgres">PostgreSQL database</option>
          <option value="csv">CSV file</option>
        </select>
      </mat-form-field>
    </mat-dialog-content>

    @if (kind() === 'postgres') {
      <form [formGroup]="form" (ngSubmit)="save()">
        <mat-dialog-content>
          <div class="fields">
            <mat-form-field class="wide"><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>
            <mat-form-field><mat-label>Host</mat-label><input matInput formControlName="host" placeholder="db.example.com" /></mat-form-field>
            <mat-form-field><mat-label>Port</mat-label><input matInput type="number" formControlName="port" /></mat-form-field>
            <mat-form-field><mat-label>Database</mat-label><input matInput formControlName="database" /></mat-form-field>
            <mat-form-field>
              <mat-label>SSL</mat-label>
              <select matNativeControl formControlName="sslMode">
                <option value="Prefer">Prefer</option>
                <option value="Require">Require</option>
                <option value="Disable">Disable</option>
              </select>
            </mat-form-field>
            <mat-form-field><mat-label>Username</mat-label><input matInput formControlName="username" autocomplete="off" /></mat-form-field>
            <mat-form-field><mat-label>Password</mat-label><input matInput type="password" formControlName="password" autocomplete="new-password" /></mat-form-field>
          </div>
          <p class="hint"><small>Use a database account that can only SELECT the data you want to expose.</small></p>

          @if (draftTest(); as t) {
            <p [class]="t.success ? 'ok' : 'fail'" role="status">
              {{ t.message }}@if (t.success && t.serverVersion) { (PostgreSQL {{ t.serverVersion }}, {{ t.latencyMs }} ms) }
            </p>
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        </mat-dialog-content>

        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>Cancel</button>
          <button mat-stroked-button type="button" (click)="test()" [disabled]="form.invalid || busy()">Test connection</button>
          <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Save</button>
        </mat-dialog-actions>
      </form>
    } @else if (uploaded(); as result) {
      <mat-dialog-content>
        <p class="ok" role="status">
          Read {{ result.rowCount }} {{ result.rowCount === 1 ? 'row' : 'rows' }} into the table <code>{{ result.tableName }}</code>.
        </p>
        <table class="columns" aria-label="Columns">
          <thead><tr><th>Column</th><th>Type</th></tr></thead>
          <tbody>
            @for (column of result.columns; track column.name) {
              <tr><td><code>{{ column.name }}</code></td><td>{{ column.dataType }}</td></tr>
            }
          </tbody>
        </table>
        <p class="hint"><small>Query it like any table: <code>SELECT * FROM {{ result.tableName }}</code>.</small></p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-flat-button type="button" (click)="ref.close(result.dataSource)">Done</button>
      </mat-dialog-actions>
    } @else {
      <form [formGroup]="csvForm" (ngSubmit)="upload()">
        <mat-dialog-content>
          <div class="fields">
            <mat-form-field class="wide"><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>

            <div class="wide file">
              <label for="csv-file">File</label>
              <input id="csv-file" type="file" accept=".csv,.tsv,.txt,text/csv,text/plain" (change)="choose($any($event.target).files)" />
              <small>Up to 20 MB, 200,000 rows and 200 columns. Each column is given the type that fits what is in it.</small>
            </div>

            <mat-form-field>
              <mat-label>Delimiter</mat-label>
              <select matNativeControl formControlName="delimiter">
                @for (d of delimiters; track d.value) {
                  <option [value]="d.value">{{ d.label }}</option>
                }
              </select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Table name (optional)</mat-label>
              <input matInput formControlName="tableName" placeholder="from the file's name" spellcheck="false" />
            </mat-form-field>
            <mat-checkbox class="wide" formControlName="hasHeader">The first row holds the column names</mat-checkbox>
          </div>

          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        </mat-dialog-content>

        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>Cancel</button>
          <button mat-flat-button type="submit" [disabled]="csvForm.invalid || !file() || busy()">
            {{ busy() ? 'Uploading...' : 'Upload' }}
          </button>
        </mat-dialog-actions>
      </form>
    }
  `,
  styles: `
    .fields { display: grid; grid-template-columns: 1fr 1fr; column-gap: 1rem; padding-top: 0.5rem; }
    .wide { grid-column: 1 / -1; }
    @media (max-width: 36rem) { .fields { grid-template-columns: 1fr; } }
    .hint { margin: 0; }
    .file { display: grid; gap: 0.25rem; margin-bottom: 1rem; }
    .columns { border-collapse: collapse; margin: 0.5rem 0; }
    .columns th, .columns td { text-align: left; padding: 0.15rem 1rem 0.15rem 0; }
  `,
})
export class AddDataSourceDialogComponent {
  private readonly data = inject<AddDataSourceDialogData>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<AddDataSourceDialogComponent, DataSource>>(MatDialogRef);
  private readonly api = inject(DataSourcesService);
  private readonly fb = inject(FormBuilder);

  protected readonly kind = signal<Kind>('postgres');
  protected readonly busy = signal(false);
  protected readonly draftTest = signal<ConnectionTestResult | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly delimiters = CSV_DELIMITERS;
  protected readonly file = signal<File | null>(null);
  /** What was read from the file, shown before the dialog closes. */
  protected readonly uploaded = signal<CsvUploadResult | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    host: ['', Validators.required],
    port: [5432, [Validators.required, Validators.min(1), Validators.max(65535)]],
    database: ['', Validators.required],
    username: ['', Validators.required],
    password: [''],
    sslMode: ['Prefer' as 'Prefer' | 'Require' | 'Disable'],
  });

  protected readonly csvForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    delimiter: [''],
    tableName: [''],
    hasHeader: [true],
  });

  protected setKind(kind: Kind): void {
    this.kind.set(kind);
    this.errors.set([]);
    this.draftTest.set(null);
  }

  /** The file the person picked. The name of the data source starts as the file's name, if they have not typed one. */
  protected choose(files: FileList | null): void {
    const file = files?.item(0) ?? null;
    this.file.set(file);
    this.errors.set([]);
    const name = this.csvForm.controls.name;
    if (file && !name.value.trim()) name.setValue(file.name.replace(/\.[^.]*$/, ''));
  }

  protected upload(): void {
    const file = this.file();
    if (this.csvForm.invalid || !file) return;
    this.busy.set(true);
    this.errors.set([]);

    const v = this.csvForm.getRawValue();
    this.api.uploadCsv(this.data.orgId, v.name, file, { tableName: v.tableName, delimiter: v.delimiter, hasHeader: v.hasHeader }).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.uploaded.set(result);
        this.ref.disableClose = true; // the data source exists now; closing goes through Done, which hands it back to the list
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not read the file.'));
      },
    });
  }

  protected test(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.draftTest.set(null);
    this.errors.set([]);

    this.api.testNew(this.data.orgId, this.connectionString()).subscribe({
      next: (result) => {
        this.draftTest.set(result);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Test failed.'));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.draftTest.set(null);
    this.errors.set([]);

    this.api.create(this.data.orgId, this.form.getRawValue().name, this.connectionString()).subscribe({
      next: (created) => this.ref.close(created),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not save the data source.'));
      },
    });
  }

  private connectionString(): string {
    const v = this.form.getRawValue();
    return buildPostgresConnectionString({
      host: v.host,
      port: v.port,
      database: v.database,
      username: v.username,
      password: v.password,
      sslMode: v.sslMode,
    });
  }
}
