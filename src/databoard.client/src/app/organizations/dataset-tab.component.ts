import { Component, computed, effect, inject, input, linkedSignal, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { SqlEditorComponent } from '../sql-editor/sql-editor.component';
import { SqlResultsComponent } from '../sql-editor/sql-results.component';
import { DataSourcesService, MAX_PREVIEW_ROWS, QueryPreview, problemMessages } from './data-sources.service';
import { TableViewBuilderComponent } from './table-view-builder.component';
import { Dataset, DatasetsService, TableViewDefinition, emptyTableView } from './datasets.service';

/** One open dataset: its SQL editor and the preview/explore panels. Each tab keeps its own state while hidden. */
@Component({
  selector: 'app-dataset-tab',
  imports: [MatCardModule, MatButtonModule, SqlEditorComponent, SqlResultsComponent, TableViewBuilderComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>{{ dataset().name }}</mat-card-title>
        <span class="actions">
          <button mat-stroked-button type="button" (click)="show()">Preview</button>
          @if (canEdit()) {
            <button mat-button type="button" (click)="remove()">Delete</button>
          }
        </span>
      </mat-card-header>
      <mat-card-content>
        @if (dataset().description) { <p class="desc">{{ dataset().description }}</p> }

        <p class="chips">
          @for (column of dataset().columns; track column.name) {
            <span class="chip" [class.measure]="column.role === 'Measure'" [title]="column.dataType">
              {{ column.name }} <small>{{ column.role === 'Measure' ? 'measure' : 'dimension' }}</small>
            </span>
          }
        </p>

        @if (isView()) {
          <app-table-view-builder [orgId]="orgId()" [dataSourceId]="dataset().dataSourceId" [(definition)]="viewDefinition" />
          <p class="save-row">
            <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy()" (click)="saveSql()">Save</button>
            @if (dirty()) { <small>Unsaved changes</small> }
          </p>
          <details class="generated"><summary>Generated SQL</summary><app-sql-editor [value]="dataset().sql ?? ''" [readOnly]="true" label="Generated SQL" /></details>
        } @else if (dataset().sql !== null) {
          <app-sql-editor [value]="sqlText()" [readOnly]="!canEdit()" (valueChange)="sqlText.set($event)" (save)="saveSql()" (run)="testSql()" />
          @if (canEdit()) {
            <p class="save-row">
              <button mat-flat-button type="button" class="save" [disabled]="!dirty() || busy()" (click)="saveSql()">Save</button>
              <button mat-stroked-button type="button" class="test" [disabled]="testing() || !sqlText().trim()" (click)="testSql()">Run query</button>
              @if (dirty()) { <small>Unsaved changes</small> }
            </p>
          }
        }
        @for (message of sqlErrors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        <app-sql-results [result]="testResult()" [errors]="testErrors()" />

        @if (panel() === 'preview') {
          @if (preview(); as p) {
            <div class="results">
              <table>
                <thead><tr>@for (c of p.columns; track $index) { <th>{{ c.name }}</th> }</tr></thead>
                <tbody>
                  @for (row of p.rows; track $index) {
                    <tr>@for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }</tr>
                  }
                </tbody>
              </table>
            </div>
            <p><small>{{ p.rows.length }} rows@if (p.truncated) { (more rows exist) }</small></p>
          }
          @for (message of errors(); track message) {
            <p class="fail" role="alert">{{ message }}</p>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    :host(.inactive) { display: none; }
    /* The card fills the tab's pane; only its content scrolls, so a long result never grows the page. */
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-content { flex: 1; min-height: 0; overflow: auto; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; display: flex; gap: 0.4rem; }
    mat-card-content { padding-top: 0.75rem; }
    .desc { margin: 0.25rem 0; color: var(--mat-sys-on-surface-variant); }
    .chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .generated { margin: 0.5rem 0; }
    .save-row { display: flex; align-items: center; gap: 0.75rem; margin: 0.5rem 0; }
  `,
  host: { '[class.inactive]': '!active()' },
})
export class DatasetTabComponent {
  private readonly api = inject(DatasetsService);
  private readonly sourcesApi = inject(DataSourcesService);

  readonly dataset = input.required<Dataset>();
  readonly orgId = input.required<string>();
  readonly canEdit = input(false);
  /** Only the visible tab is shown; the others stay alive so their editor text and results survive. */
  readonly active = input(true);

  /** The saved dataset, after the SQL was saved. */
  readonly updated = output<Dataset>();
  readonly deleted = output<string>();
  readonly dirtyChange = output<boolean>();

  /** The text in the editor; starts from, and resets to, the saved SQL whenever the dataset changes. */
  protected readonly sqlText = linkedSignal(() => this.dataset().sql ?? '');
  /** A table view that this user may edit: built step by step instead of written as SQL. */
  protected readonly isView = computed(() => this.canEdit() && this.dataset().kind === 'TableView' && this.dataset().definition !== null);
  /** The table view being edited; starts from, and resets to, the saved definition. */
  protected readonly viewDefinition = linkedSignal<TableViewDefinition>(() => this.dataset().definition ?? emptyTableView());
  protected readonly dirty = computed(() =>
    this.isView()
      ? JSON.stringify(this.viewDefinition()) !== JSON.stringify(this.dataset().definition)
      : this.dataset().sql !== null && this.sqlText() !== this.dataset().sql);
  protected readonly sqlErrors = signal<string[]>([]);
  /** Rows from running the editor's current SQL (saved or not) against the dataset's data source. */
  protected readonly testResult = signal<QueryPreview | null>(null);
  protected readonly testErrors = signal<string[]>([]);
  protected readonly testing = signal(false);

  protected readonly panel = signal<'preview' | null>(null);
  protected readonly preview = signal<QueryPreview | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);
  constructor() {
    effect(() => this.dirtyChange.emit(this.dirty()));
  }

  /** Runs the SQL as it is in the editor, so a change can be tried before it is saved. */
  protected testSql(): void {
    const sql = this.sqlText().trim();
    if (!this.canEdit() || !sql || this.testing()) return;
    this.testing.set(true);
    this.testResult.set(null);
    this.testErrors.set([]);
    this.sourcesApi.preview(this.orgId(), this.dataset().dataSourceId, sql, MAX_PREVIEW_ROWS).subscribe({
      next: (r) => {
        this.testing.set(false);
        this.testResult.set(r);
      },
      error: (e) => {
        this.testing.set(false);
        this.testErrors.set(problemMessages(e, 'The query failed.'));
      },
    });
  }

  protected saveSql(): void {
    const dataset = this.dataset();
    if (!this.canEdit() || !this.dirty() || this.busy()) return;
    this.busy.set(true);
    this.sqlErrors.set([]);
    const request = this.isView()
      ? this.api.updateTableView(this.orgId(), dataset.id, dataset.name, this.viewDefinition(), dataset.description)
      : this.api.update(this.orgId(), dataset.id, dataset.name, this.sqlText(), dataset.description);
    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        // The columns may have changed with the query, so any open results no longer describe the dataset.
        this.preview.set(null);
        this.panel.set(null);
        this.updated.emit(saved);
      },
      error: (e) => {
        this.busy.set(false);
        this.sqlErrors.set(problemMessages(e, 'Could not save the query.'));
      },
    });
  }

  protected show(): void {
    this.panel.set('preview');
    this.preview.set(null);
    this.errors.set([]);
    this.api.preview(this.orgId(), this.dataset().id).subscribe({
      next: (p) => this.preview.set(p),
      error: (e) => this.errors.set(problemMessages(e, 'Could not load the data.')),
    });
  }

  protected remove(): void {
    const dataset = this.dataset();
    if (!confirm(`Delete the dataset "${dataset.name}"?`)) return;
    this.api.remove(this.orgId(), dataset.id).subscribe({
      next: () => this.deleted.emit(dataset.id),
      error: (e) => this.errors.set(problemMessages(e, 'Could not delete the dataset.')),
    });
  }
}
