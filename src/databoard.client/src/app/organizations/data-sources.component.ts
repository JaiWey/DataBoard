import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AddDataSourceDialogComponent } from './add-data-source-dialog.component';
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

/** Data sources of an organization: the list on the left, the selected one's details on the right. */
@Component({
  selector: 'app-data-sources',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatListModule,
  ],
  template: `
    <h2>{{ organization()?.name }} <small>data sources</small></h2>

    <div class="layout">
      <mat-card appearance="raised" class="list">
        <mat-card-header>
          <mat-card-title>Data sources</mat-card-title>
          @if (canEdit()) {
            <button mat-flat-button type="button" class="add" (click)="openAddDialog()">Add data source</button>
          }
        </mat-card-header>
        <mat-card-content>
          @if (loaded() && sources().length === 0) {
            <p><em>No data sources yet.</em></p>
          }
          <mat-action-list>
            @for (source of sources(); track source.id) {
              <button mat-list-item type="button" class="item" [activated]="selectedId() === source.id" (click)="select(source)">
                <span matListItemTitle>{{ source.name }}</span>
                <span matListItemLine>{{ source.type }}</span>
              </button>
            }
          </mat-action-list>
        </mat-card-content>
      </mat-card>

      <mat-card appearance="raised" class="detail">
        @if (selected(); as source) {
          <mat-card-header>
            <mat-card-title>{{ source.name }}</mat-card-title>
            <mat-card-subtitle>{{ source.type }} &middot; added {{ source.createdUtc | date: 'medium' }}</mat-card-subtitle>
            <span class="actions">
              <button mat-stroked-button type="button" (click)="open(source, 'schema')">Schema</button>
              @if (canEdit()) {
                <button mat-stroked-button type="button" (click)="open(source, 'query')">Query</button>
                <button mat-button type="button" (click)="testSaved(source)">Test</button>
                @if (source.type === 'Csv') {
                  <button mat-button type="button" (click)="fileInput.click()" [disabled]="busy()">Replace file</button>
                  <input #fileInput type="file" hidden aria-label="New CSV file" accept=".csv,.tsv,.txt,text/csv,text/plain"
                         (change)="replaceFile(source, $any($event.target))" />
                }
              }
              @if (isOwner()) {
                <button mat-button type="button" class="danger" (click)="remove(source)" [disabled]="busy()">Delete</button>
              }
            </span>
          </mat-card-header>

          <mat-card-content>
            @if (replaced(); as r) {
              @if (r.id === source.id) {
                <p class="ok" role="status">
                  Replaced: {{ r.rowCount }} {{ r.rowCount === 1 ? 'row' : 'rows' }} now in <code>{{ r.tableName }}</code>.
                  Datasets and charts built on it use the new data.
                </p>
              }
            }
            @if (savedTest(); as t) {
              @if (t.id === source.id) {
                <p [class]="t.result.success ? 'ok' : 'fail'" role="status">{{ t.result.message }}</p>
              }
            }

            @if (!panel()) {
              <p class="hint"><em>Choose Schema to browse its tables@if (canEdit()) { , or Query to run a read-only SELECT }.</em></p>
            }

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
          </mat-card-content>
        } @else {
          <mat-card-content class="placeholder">
            <p><em>Select a data source to see its details.</em></p>
          </mat-card-content>
        }
      </mat-card>
    </div>
  `,
  styles: `
    :host { --mat-card-elevated-container-color: #fff; }
    /* Both panels fill the space under the page title; the list scrolls inside its own card. */
    .layout { display: grid; grid-template-columns: 20rem 1fr; gap: 1.25rem; align-items: start; }
    .list { height: calc(100vh - 12.5rem); position: sticky; top: 1.25rem; }
    .list mat-card-content { flex: 1; overflow-y: auto; }
    .detail { min-height: calc(100vh - 12.5rem); }
    @media (max-width: 52rem) {
      .layout { grid-template-columns: 1fr; }
      .list { height: auto; max-height: 20rem; position: static; }
      .detail { min-height: 0; }
    }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    .add { margin-left: auto; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    mat-card-content { padding-top: 0.75rem; }
    .placeholder { flex: 1; display: grid; place-items: center; text-align: center; color: var(--mat-sys-on-surface-variant); }
    .columns { margin: 0.25rem 0 0.5rem 1rem; padding: 0; list-style: none; }
    .query { display: grid; gap: 0.25rem; margin-top: 0.5rem; }
    .sql-field { width: 100%; }
    textarea { font-family: monospace; }
    .save { margin-top: 0.75rem; }
    .danger { color: var(--mat-sys-error); }
  `,
})
export class DataSourcesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly api = inject(DataSourcesService);
  private readonly fb = inject(FormBuilder);
  private readonly datasetsApi = inject(DatasetsService);
  private readonly dialog = inject(MatDialog);

  protected orgId = '';

  protected readonly organization = signal<Organization | null>(null);
  protected readonly canEdit = computed(() => {
    const role = this.organization()?.role;
    return role === 'Editor' || role === 'Owner';
  });
  protected readonly isOwner = computed(() => this.organization()?.role === 'Owner');
  protected readonly replaced = signal<{ id: string; tableName: string; rowCount: number } | null>(null);
  protected readonly sources = signal<DataSource[]>([]);
  protected readonly loaded = signal(false);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => this.sources().find((s) => s.id === this.selectedId()) ?? null);
  protected readonly panel = signal<Panel | null>(null);
  protected readonly schema = signal<DatabaseSchema | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly panelError = signal<string[]>([]);
  protected readonly savedTest = signal<{ id: string; result: ConnectionTestResult } | null>(null);
  protected readonly busy = signal(false);

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

  /** Shows a data source's details. What was open for the previous one is cleared. */
  protected select(source: DataSource): void {
    if (this.selectedId() === source.id) return;
    this.selectedId.set(source.id);
    this.panel.set(null);
    this.schema.set(null);
    this.preview.set(null);
    this.panelError.set([]);
    this.savedDataset.set(null);
    this.savedTest.set(null);
    this.replaced.set(null);
    this.queryForm.reset();
    this.datasetForm.reset();
  }

  protected openAddDialog(): void {
    this.dialog
      .open<AddDataSourceDialogComponent, { orgId: string }, DataSource>(AddDataSourceDialogComponent, {
        data: { orgId: this.orgId },
        width: '34rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) this.reload(created.id);
      });
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

  /** Puts the chosen file into a CSV data source. The table keeps its name, so what was built on it carries on. */
  protected replaceFile(source: DataSource, input: HTMLInputElement): void {
    const file = input.files?.item(0);
    input.value = ''; // choosing the same file again must still count as a change
    if (!file) return;

    this.busy.set(true);
    this.panelError.set([]);
    this.replaced.set(null);
    this.api.replaceCsv(this.orgId, source.id, file).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.replaced.set({ id: source.id, tableName: r.tableName, rowCount: r.rowCount });
        this.schema.set(null);
        if (this.panel() === 'schema') this.open(source, 'schema'); // the columns may have changed
      },
      error: (e) => {
        this.busy.set(false);
        this.panelError.set(problemMessages(e, 'Could not read the file.'));
      },
    });
  }

  /** Removes a data source with everything built on it, after asking. */
  protected remove(source: DataSource): void {
    if (!confirm(`Delete the data source "${source.name}"? Its datasets and the charts built on them are deleted too.`)) return;

    this.busy.set(true);
    this.panelError.set([]);
    this.api.delete(this.orgId, source.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.sources.update((items) => items.filter((s) => s.id !== source.id));
        this.selectedId.set(null);
        this.panel.set(null);
      },
      error: (e) => {
        this.busy.set(false);
        this.panelError.set(problemMessages(e, 'Could not delete the data source.'));
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

  /** Reloads the list; a newly created data source is selected so its details appear straight away. */
  private reload(selectId?: string): void {
    this.api.list(this.orgId).subscribe({
      next: (items) => {
        this.sources.set(items);
        this.loaded.set(true);
        const created = selectId ? items.find((s) => s.id === selectId) : undefined;
        if (created) this.select(created);
      },
    });
  }
}
