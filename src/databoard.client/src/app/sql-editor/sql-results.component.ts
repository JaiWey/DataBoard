import { Component, computed, input, linkedSignal, signal } from '@angular/core';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { QueryPreview } from '../organizations/data-sources.service';

/** The rows a SQL query returned, as a table with paging, or the reasons it failed. */
@Component({
  selector: 'app-sql-results',
  imports: [MatPaginatorModule],
  template: `
    @for (message of errors(); track message) {
      <p class="fail" role="alert">{{ message }}</p>
    }
    @if (result(); as r) {
      <div class="results">
        <table>
          <thead><tr>@for (c of r.columns; track $index) { <th [title]="c.dataType">{{ c.name }}</th> }</tr></thead>
          <tbody>
            @for (row of page(); track $index) {
              <tr>@for (cell of row; track $index) { <td>{{ cell === null ? '∅' : cell }}</td> }</tr>
            }
          </tbody>
        </table>
      </div>
      <mat-paginator [length]="r.rows.length" [pageIndex]="pageIndex()" [pageSize]="pageSize()" [pageSizeOptions]="pageSizes"
                     [hidePageSize]="false" [showFirstLastButtons]="true" aria-label="Result rows page" (page)="change($event)" />
      <p><small>{{ r.rows.length }} {{ r.rows.length === 1 ? 'row' : 'rows' }}@if (r.truncated) { (more rows exist; only the first are shown) }</small></p>
    }
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class SqlResultsComponent {
  readonly result = input<QueryPreview | null>(null);
  readonly errors = input<string[]>([]);

  protected readonly pageSizes = [10, 25, 50, 100];
  protected readonly pageSize = signal(10);
  /** A new result always starts at its first page. */
  protected readonly pageIndex = linkedSignal(() => (this.result(), 0));
  protected readonly page = computed(() => {
    const rows = this.result()?.rows ?? [];
    const start = this.pageIndex() * this.pageSize();
    return rows.slice(start, start + this.pageSize());
  });

  protected change(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }
}
