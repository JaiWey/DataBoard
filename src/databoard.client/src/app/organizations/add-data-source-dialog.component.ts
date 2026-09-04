import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { buildPostgresConnectionString } from './connection-string';
import { ConnectionTestResult, DataSource, DataSourcesService, problemMessages } from './data-sources.service';

export interface AddDataSourceDialogData {
  orgId: string;
}

/** Closes with the created data source, or with nothing if the user cancelled. */
@Component({
  selector: 'app-add-data-source-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Add a PostgreSQL data source</h2>
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
  `,
  styles: `
    .fields { display: grid; grid-template-columns: 1fr 1fr; column-gap: 1rem; padding-top: 0.5rem; }
    .wide { grid-column: 1 / -1; }
    @media (max-width: 36rem) { .fields { grid-template-columns: 1fr; } }
    .hint { margin: 0; }
  `,
})
export class AddDataSourceDialogComponent {
  private readonly data = inject<AddDataSourceDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<AddDataSourceDialogComponent, DataSource>>(MatDialogRef);
  private readonly api = inject(DataSourcesService);
  private readonly fb = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly draftTest = signal<ConnectionTestResult | null>(null);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    host: ['', Validators.required],
    port: [5432, [Validators.required, Validators.min(1), Validators.max(65535)]],
    database: ['', Validators.required],
    username: ['', Validators.required],
    password: [''],
    sslMode: ['Prefer' as 'Prefer' | 'Require' | 'Disable'],
  });

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
