import { Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AppSelectComponent, SelectOption } from '../shared/app-select.component';
import { DataSource, DataSourcesService, problemMessages } from './data-sources.service';
import { Dataset, TableViewDefinition, emptyTableView } from './datasets.service';
import { SaveDatasetDialogComponent, SaveDatasetDialogData } from './save-dataset-dialog.component';
import { TableViewBuilderComponent } from './table-view-builder.component';

/** An unsaved table view dataset: pick the data source, build the view step by step, and Save asks for a name. */
@Component({
  selector: 'app-new-table-view-tab',
  imports: [MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, AppSelectComponent, TableViewBuilderComponent],
  template: `
    <mat-card appearance="raised" class="detail">
      <mat-card-header>
        <mat-card-title>New table view dataset</mat-card-title>
        <span class="actions">
          <button mat-flat-button type="button" class="save" [disabled]="!canSave()" (click)="openSave()">Save</button>
        </span>
      </mat-card-header>
      <mat-card-content>
        @if (loaded() && sources().length === 0) {
          <p class="fail" role="alert">Add a data source first; a dataset is built on one.</p>
        }
        <app-select class="source" label="Data source" ariaLabel="Data source" [options]="sourceOptions()" [value]="sourceId()" (valueChange)="changeSource($event)" />
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        @if (sourceId()) {
          <app-table-view-builder [orgId]="orgId()" [dataSourceId]="sourceId()" [(definition)]="definition" />
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    :host(.inactive) { display: none; }
    .detail { --mat-card-elevated-container-color: #fff; height: 100%; }
    mat-card-content { flex: 1; min-height: 0; overflow: auto; padding-top: 0.75rem; }
    mat-card-title { font: var(--mat-sys-title-medium); }
    mat-card-header { align-items: center; flex-wrap: wrap; gap: 0.5rem; }
    .actions { margin-left: auto; }
    .source { width: 18rem; max-width: 100%; }
  `,
  host: { '[class.inactive]': '!active()' },
})
export class NewTableViewTabComponent implements OnInit {
  private readonly sourcesApi = inject(DataSourcesService);
  private readonly dialog = inject(MatDialog);

  readonly orgId = input.required<string>();
  readonly active = input(true);
  /** The folder the dataset is saved into (null is the top level). */
  readonly folderId = input<string | null>(null);
  /** The dataset that was saved; the page turns this tab into that dataset's tab. */
  readonly created = output<Dataset>();
  /** True once something was chosen that closing the tab would lose. */
  readonly dirtyChange = output<boolean>();

  protected readonly sources = signal<DataSource[]>([]);
  protected readonly sourceOptions = computed<SelectOption[]>(() => this.sources().map((s) => ({ value: s.id, label: s.name })));
  protected readonly loaded = signal(false);
  protected readonly sourceId = signal('');
  protected readonly definition = signal<TableViewDefinition>(emptyTableView());
  protected readonly errors = signal<string[]>([]);
  protected readonly canSave = computed(() => {
    const d = this.definition();
    const chosen = [d.main, ...d.joins.map((j) => j.table)];
    return !!this.sourceId() && chosen.every((t) => !!t.name) && chosen.some((t) => t.fields.length > 0)
      && d.joins.every((j) => j.on.length > 0 && j.on.every((c) => !!c.leftField && !!c.rightField));
  });

  constructor() {
    effect(() => this.dirtyChange.emit(!!this.definition().main.name));
  }

  ngOnInit(): void {
    this.sourcesApi.list(this.orgId()).subscribe({
      next: (items) => {
        this.sources.set(items);
        this.loaded.set(true);
        if (items.length > 0) this.sourceId.set(items[0].id);
      },
      error: (e) => {
        this.loaded.set(true);
        this.errors.set(problemMessages(e, 'Could not load the data sources.'));
      },
    });
  }

  /** Tables belong to one data source, so choosing another one starts the view over. */
  protected changeSource(id: string): void {
    if (id === this.sourceId()) return;
    this.sourceId.set(id);
    this.definition.set(emptyTableView());
  }

  protected openSave(): void {
    if (!this.canSave()) return;
    this.dialog
      .open<SaveDatasetDialogComponent, SaveDatasetDialogData, Dataset>(SaveDatasetDialogComponent, {
        data: { orgId: this.orgId(), dataSourceId: this.sourceId(), definition: this.definition(), folderId: this.folderId() },
        width: '28rem',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) this.created.emit(created);
      });
  }
}
