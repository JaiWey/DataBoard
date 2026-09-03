import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { QueryPreview } from './data-sources.service';

export type ColumnRole = 'Dimension' | 'Measure';
export type Aggregation = 'Sum' | 'Avg' | 'Min' | 'Max' | 'Count' | 'CountDistinct';
export type TimeGrain = 'Day' | 'Week' | 'Month' | 'Quarter' | 'Year';

/** The typed query the API understands. It is a description, never SQL. */
export interface ChartQuery {
  dimensions?: { column: string; grain?: TimeGrain; alias?: string }[];
  measures?: { aggregation: Aggregation; column?: string; alias?: string }[];
  limit?: number;
}

export interface ChartResult extends QueryPreview {
  /** Only editors and owners receive the generated SQL. */
  sql: string | null;
}

export interface DatasetColumn {
  name: string;
  dataType: string;
  role: ColumnRole;
}

export interface Dataset {
  id: string;
  dataSourceId: string;
  name: string;
  description: string | null;
  /** Null for viewers: only editors and owners may see how a dataset is built. */
  sql: string | null;
  columns: DatasetColumn[];
  createdUtc: string;
  updatedUtc: string;
}

@Injectable({ providedIn: 'root' })
export class DatasetsService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/datasets${suffix}`;
  }

  list(orgId: string): Observable<Dataset[]> {
    return this.http.get<Dataset[]>(this.url(orgId));
  }

  create(orgId: string, dataSourceId: string, name: string, sql: string, description?: string): Observable<Dataset> {
    return this.http.post<Dataset>(this.url(orgId), { dataSourceId, name, description: description ?? null, sql });
  }

  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  preview(orgId: string, id: string): Observable<QueryPreview> {
    return this.http.get<QueryPreview>(this.url(orgId, `/${id}/preview`));
  }

  query(orgId: string, id: string, query: ChartQuery): Observable<ChartResult> {
    return this.http.post<ChartResult>(this.url(orgId, `/${id}/query`), query);
  }
}
