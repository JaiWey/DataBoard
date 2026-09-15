import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { DatasetParameter } from './dataset-parameters';

export interface DataSource {
  id: string;
  organizationId: string;
  name: string;
  type: string;
  createdUtc: string;
}

export interface ConnectionTestResult {
  success: boolean;
  message: string;
  serverVersion: string | null;
  latencyMs: number;
}

export interface ColumnSchema { name: string; dataType: string; nullable: boolean }
export interface TableSchema { name: string; kind: string; columns: ColumnSchema[] }
export interface SchemaNode { name: string; tables: TableSchema[] }
export interface DatabaseSchema { schemas: SchemaNode[]; truncated: boolean }

/** The most rows the API returns for one query. */
export const MAX_PREVIEW_ROWS = 100;

export interface QueryPreview {
  columns: { name: string; dataType: string }[];
  rows: unknown[][];
  truncated: boolean;
}

/** How to read a CSV file. Anything left out is worked out from the file. */
export interface CsvOptions {
  tableName?: string;
  /** `,` `;` `|` or the word `tab`. */
  delimiter?: string;
  /** Whether the first row holds the column names (it does, if left out). */
  hasHeader?: boolean;
}

export interface CsvUploadResult {
  dataSource: DataSource;
  tableName: string;
  rowCount: number;
  columns: { name: string; dataType: string }[];
}

/** The delimiters the file can use, as the choices offered. */
export const CSV_DELIMITERS: { value: string; label: string }[] = [
  { value: '', label: 'Detect' },
  { value: ',', label: 'Comma' },
  { value: ';', label: 'Semicolon' },
  { value: 'tab', label: 'Tab' }, // spelled out: a form field that is only whitespace counts as left out
  { value: '|', label: 'Vertical bar' },
];

function csvForm(file: File, options: CsvOptions): FormData {
  const form = new FormData();
  form.append('File', file, file.name);
  if (options.tableName?.trim()) form.append('TableName', options.tableName.trim());
  if (options.delimiter) form.append('Delimiter', options.delimiter);
  if (options.hasHeader === false) form.append('HasHeader', 'false');
  return form;
}

/** Reads the API's problem details into plain messages: validation errors, or a single detail line. */
export function problemMessages(e: unknown, fallback: string): string[] {
  if (!(e instanceof HttpErrorResponse)) return [fallback];
  if (e.status === 429) return ['Too many requests. Wait a moment and try again.'];
  if (e.status === 403) return ['You do not have permission to do this.'];

  const fields = e.error?.errors as Record<string, string[]> | undefined;
  const messages = fields ? Object.values(fields).flat() : [];
  if (messages.length) return messages;
  return typeof e.error?.detail === 'string' ? [e.error.detail] : [fallback];
}

@Injectable({ providedIn: 'root' })
export class DataSourcesService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/data-sources${suffix}`;
  }

  list(orgId: string): Observable<DataSource[]> {
    return this.http.get<DataSource[]>(this.url(orgId));
  }

  create(orgId: string, name: string, connectionString: string): Observable<DataSource> {
    return this.http.post<DataSource>(this.url(orgId), { name, type: 'PostgreSql', connectionString });
  }

  /** Makes a data source from a CSV file. It is read into a database of its own, so nothing connects to anywhere. */
  uploadCsv(orgId: string, name: string, file: File, options: CsvOptions = {}): Observable<CsvUploadResult> {
    const form = csvForm(file, options);
    form.append('Name', name);
    return this.http.post<CsvUploadResult>(this.url(orgId, '/csv'), form);
  }

  /** Puts a new file into a CSV data source. It keeps its table name, so what was built on it carries on. */
  replaceCsv(orgId: string, id: string, file: File, options: CsvOptions = {}): Observable<CsvUploadResult> {
    return this.http.put<CsvUploadResult>(this.url(orgId, `/${id}/csv`), csvForm(file, options));
  }

  /** Removes a data source with the datasets and charts built on it. Owners only. */
  delete(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  testNew(orgId: string, connectionString: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url(orgId, '/test'), { type: 'PostgreSql', connectionString });
  }

  testSaved(orgId: string, id: string): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url(orgId, `/${id}/test`), {});
  }

  schema(orgId: string, id: string): Observable<DatabaseSchema> {
    return this.http.get<DatabaseSchema>(this.url(orgId, `/${id}/schema`));
  }

  /** Runs SQL once, to try it. Its {{parameters}} (with their defaults) are declared in `parameters`. */
  preview(orgId: string, id: string, sql: string, maxRows?: number, parameters: DatasetParameter[] = []): Observable<QueryPreview> {
    return this.http.post<QueryPreview>(this.url(orgId, `/${id}/preview`), {
      sql, ...(maxRows ? { maxRows } : {}), ...(parameters.length > 0 ? { parameters } : {}),
    });
  }
}
