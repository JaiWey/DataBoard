import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { buildPostgresConnectionString } from './connection-string';
import { DatasetsService } from './datasets.service';
import {
  ConnectionTestResult,
  DataSource,
  DataSourcesService,
  DatabaseSchema,
  QueryPreview,
  problemMessages,
} from './data-sources.service';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
}

type Panel = 'schema' | 'query';

@Component({
  selector: 'app-data-sources',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <p>
      <a mat-button routerLink="/organizations">&larr; Organizations</a>
      <a mat-button [routerLink]="['/organizations', organization()?.id ?? '', 'datasets']">Datasets</a>
      <a mat-button [routerLink]="['/organizations', organization()?.id ?? '', 'dashboards']">Dashboards</a>
    </p>
    <h2>{{ organization()?.name }} <small>data sources</small></h2>

    @if (sources().length === 0) {
      <p><em>No data sources yet.</em></p>
    }

    @for (source of sources(); track source.id) {
      <mat-card class="source" appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ source.name }}</mat-card-title>
          <mat-card-subtitle>{{ source.type }}</mat-card-subtitle>
          <span class="actions">
            <button mat-stroked-button type="button" (click)="open(source, 'schema')">Schema</button>
            @if (canEdit()) {
              <button mat-stroked-button type="button" (click)="open(source, 'query')">Query</button>
              <button mat-button type="button" (click)="testSaved(source)">Test</button>
            }
          </span>
        </mat-card-header>
        <mat-card-content>
        @if (savedTest(); as t) {
          @if (t.id === source.id) {
            <p [class]="t.result.success ? 'ok' : 'fail'" role="status">{{ t.result.message }}</p>
          }
        }

        @if (selectedId() === source.id) {
          @if (panel() === 'schema') {
            @if (schema(); as s) {
              @for (node of s.schemas; track node.name) {
                <details open>
                  <summary>{{ node.name }}</summary>
                  @for (table of node.tables; track table.name) {
                    <details>
                      <summary>{{ table.name }} <small>{{ table.kind }}, {{ table.columns.length }} columns</small></summary>
                      <ul class="columns">
                        @for (column of table.columns; track column.name) {
                          <li>
                            <code>{{ column.name }}</code> {{ column.dataType }}
                            @if (column.nullable) { <small>nullable</small> }
                          </li>
                        }
                      </ul>
                    </details>
                  }
                </details>
              } @empty {
                <p><em>No tables visible to this database account.</em></p>
              }
              @if (s.truncated) {
                <p class="fail">The schema is large; only the first part is shown.</p>
              }
            } @else if (!panelError()) {
              <p><em>Loading schema...</em></p>
            }
          }

          @if (panel() === 'query') {
            <form [formGroup]="queryForm" (ngSubmit)="runQuery(source)" class="query">
              <mat-form-field class="sql-field">
                <mat-label>SQL query</mat-label>
                <textarea matInput formControlName="sql" rows="5" spellcheck="false" aria-label="SQL query"
                          placeholder="SELECT * FROM my_table"></textarea>
                <mat-hint>One read-only SELECT. Writes are blocked.</mat-hint>
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="queryForm.invalid || busy()">Run (max 50 rows)</button>
            </form>
            @if (preview(); as p) {
              <div class="results">
                <table>
                  <thead><tr>@for (c of p.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                  <tbody>
                    @for (row of p.rows; track $index) {
                      <tr>@for (cell of row; track $index) { <td>{{ show(cell) }}</td> }</tr>
                    }
                  </tbody>
                </table>
              </div>
              <p><small>{{ p.rows.length }} rows@if (p.truncated) { (more rows exist) }</small></p>

              <form [formGroup]="datasetForm" (ngSubmit)="saveDataset(source)" class="save form-row">
                <mat-form-field>
                  <mat-label>Dataset name</mat-label>
                  <input matInput formControlName="name" aria-label="Dataset name" />
                </mat-form-field>
                <button mat-flat-button type="submit" [disabled]="datasetForm.invalid || busy() || queryForm.invalid">Save as dataset</button>
              </form>
            }
            @if (savedDataset(); as name) {
              <p class="ok" role="status">
                Saved dataset "{{ name }}". <a [routerLink]="['/organizations', orgId, 'datasets']">View datasets</a>
              </p>
            }
          }

          @for (message of panelError(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }
        </mat-card-content>
      </mat-card>
    }

    @if (canEdit()) {
      <h3>Add a PostgreSQL data source</h3>
      <form [formGroup]="form" (ngSubmit)="save()" class="add">
        <mat-form-field><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>
        <mat-form-field><mat-label>Host</mat-label><input matInput formControlName="host" placeholder="db.example.com" /></mat-form-field>
        <mat-form-field><mat-label>Port</mat-label><input matInput type="number" formControlName="port" /></mat-form-field>
        <mat-form-field><mat-label>Database</mat-label><input matInput formControlName="database" /></mat-form-field>
        <mat-form-field><mat-label>Username</mat-label><input matInput formControlName="username" autocomplete="off" /></mat-form-field>
        <mat-form-field><mat-label>Password</mat-label><input matInput type="password" formControlName="password" autocomplete="new-password" /></mat-form-field>
        <mat-form-field>
          <mat-label>SSL</mat-label>
          <select matNativeControl formControlName="sslMode">
            <option value="Prefer">Prefer</option>
            <option value="Require">Require</option>
            <option value="Disable">Disable</option>
          </select>
        </mat-form-field>
        <div class="buttons">
          <button mat-stroked-button type="button" (click)="testNew()" [disabled]="form.invalid || busy()">Test connection</button>
          <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Save</button>
        </div>
      </form>
      <p class="hint"><small>Use a database account that can only SELECT the data you want to expose.</small></p>

      @if (draftTest(); as t) {
        <p [class]="t.success ? 'ok' : 'fail'" role="status">
          {{ t.message }}@if (t.success && t.serverVersion) { (PostgreSQL {{ t.serverVersion }}, {{ t.latencyMs }} ms) }
        </p>
      }
      @for (message of formError(); track message) {
        <p class="fail" role="alert">{{ message }}</p>
      }
    }
  `,
  styles: `
    .source { margin: 1rem 0; }
    mat-card-header { align-items: center; }
    .columns { margin: 0.25rem 0 0.5rem 1rem; padding: 0; list-style: none; }
    .add, .query { display: grid; gap: 0.25rem; max-width: 28rem; }
    .query { max-width: none; margin-top: 1rem; }
    .sql-field { width: 100%; }
    textarea { font-family: monospace; }
    .buttons { display: flex; gap: 0.5rem; }
    .save { margin-top: 0.75rem; }
  `,
})
export class DataSourcesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DataSourcesService);
  private readonly fb = inject(FormBuilder);
  private readonly datasetsApi = inject(DatasetsService);

  protected orgId = '';

  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly sources = signal<DataSource[]>([]);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly panel = signal<Panel>('schema');
  protected readonly schema = signal<DatabaseSchema | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly panelError = signal<string[]>([]);
  protected readonly savedTest = signal<{ id: string; result: ConnectionTestResult } | null>(null);

  protected readonly draftTest = signal<ConnectionTestResult | null>(null);
  protected readonly formError = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    host: ['', Validators.required],
    port: [5432, [Validators.required, Validators.min(1), Validators.max(65535)]],
    database: ['', Validators.required],
    username: ['', Validators.required],
    password: [''],
    sslMode: ['Prefer' as 'Prefer' | 'Require' | 'Disable'],
  });

  protected readonly queryForm = this.fb.nonNullable.group({
    sql: ['', Validators.required],
  });

  protected readonly datasetForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });
  protected readonly savedDataset = signal<string | null>(null);

  ngOnInit(): void {
    this.orgId = this.route.snapshot.paramMap.get('id') ?? '';
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (orgs) => this.organization.set(orgs.find((o) => o.id === this.orgId) ?? null),
    });
    this.reload();
  }

  protected show(value: unknown): string {
    return value === null || value === undefined ? '∅' : String(value);
  }

  protected open(source: DataSource, panel: Panel): void {
    this.selectedId.set(source.id);
    this.panel.set(panel);
    this.panelError.set([]);
    this.preview.set(null);
    this.savedDataset.set(null);

    if (panel === 'schema') {
      this.schema.set(null);
      this.api.schema(this.orgId, source.id).subscribe({
        next: (s) => this.schema.set(s),
        error: (e) => this.panelError.set(problemMessages(e, 'Could not load the schema.')),
      });
    }
  }

  protected runQuery(source: DataSource): void {
    if (this.queryForm.invalid) return;
    this.busy.set(true);
    this.panelError.set([]);

    this.api.preview(this.orgId, source.id, this.queryForm.getRawValue().sql).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.busy.set(false);
      },
      error: (e) => {
        this.preview.set(null);
        this.busy.set(false);
        this.panelError.set(problemMessages(e, 'The query failed.'));
      },
    });
  }

  protected saveDataset(source: DataSource): void {
    if (this.datasetForm.invalid || this.queryForm.invalid) return;
    this.busy.set(true);
    this.panelError.set([]);
    this.savedDataset.set(null);

    const name = this.datasetForm.getRawValue().name;
    this.datasetsApi.create(this.orgId, source.id, name, this.queryForm.getRawValue().sql).subscribe({
      next: (dataset) => {
        this.busy.set(false);
        this.savedDataset.set(dataset.name);
        this.datasetForm.reset();
      },
      error: (e) => {
        this.busy.set(false);
        this.panelError.set(problemMessages(e, 'Could not save the dataset.'));
      },
    });
  }

  protected testSaved(source: DataSource): void {
    this.savedTest.set(null);
    this.api.testSaved(this.orgId, source.id).subscribe({
      next: (result) => this.savedTest.set({ id: source.id, result }),
      error: (e) =>
        this.savedTest.set({
          id: source.id,
          result: { success: false, message: problemMessages(e, 'Test failed.')[0], serverVersion: null, latencyMs: 0 },
        }),
    });
  }

  protected testNew(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.draftTest.set(null);
    this.formError.set([]);

    this.api.testNew(this.orgId, this.connectionString()).subscribe({
      next: (result) => {
        this.draftTest.set(result);
        this.busy.set(false);
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(problemMessages(e, 'Test failed.'));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.draftTest.set(null);
    this.formError.set([]);

    this.api.create(this.orgId, this.form.getRawValue().name, this.connectionString()).subscribe({
      next: () => {
        this.busy.set(false);
        this.form.reset({ port: 5432, sslMode: 'Prefer' });
        this.reload();
      },
      error: (e) => {
        this.busy.set(false);
        this.formError.set(problemMessages(e, 'Could not save the data source.'));
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

  private reload(): void {
    this.api.list(this.orgId).subscribe({ next: (items) => this.sources.set(items) });
  }
}
