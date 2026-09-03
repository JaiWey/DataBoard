import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChartQuery, ChartResult } from '../organizations/datasets.service';

export type WidgetChartType = 'Table' | 'Bar' | 'Line' | 'Pie';
export type SharedFilterOperator = 'Eq' | 'NotEq' | 'In' | 'Contains' | 'StartsWith' | 'Gte' | 'Lte' | 'Between';

export const SHARED_FILTER_OPERATORS: { value: SharedFilterOperator; label: string }[] = [
  { value: 'Eq', label: 'is' },
  { value: 'NotEq', label: 'is not' },
  { value: 'In', label: 'is one of' },
  { value: 'Contains', label: 'contains' },
  { value: 'StartsWith', label: 'starts with' },
  { value: 'Gte', label: 'at least' },
  { value: 'Lte', label: 'at most' },
  { value: 'Between', label: 'between' },
];

export interface DashboardWidget {
  id: string;
  title: string;
  datasetId: string;
  chartType: WidgetChartType;
  query: ChartQuery;
}

export interface DashboardFilter {
  id: string;
  label: string;
  column: string;
  operator: SharedFilterOperator;
}

export interface DashboardDefinition {
  widgets: DashboardWidget[];
  filters: DashboardFilter[];
}

export interface DashboardSummary {
  id: string;
  name: string;
  description: string | null;
  widgetCount: number;
  updatedUtc: string;
}

export interface Dashboard {
  id: string;
  name: string;
  description: string | null;
  definition: DashboardDefinition;
  createdUtc: string;
  updatedUtc: string;
}

export interface WidgetResult {
  widgetId: string;
  result: ChartResult | null;
  error: string | null;
  appliedFilters: string[];
}

export interface DashboardRun {
  widgets: WidgetResult[];
}

/** Filter id -> the values the viewer picked. A filter with no values is not applied. */
export type FilterValues = Record<string, string[]>;

/** Ids are assigned by the server; a new widget or filter is sent with the empty id. */
export const NEW_ID = '00000000-0000-0000-0000-000000000000';

@Injectable({ providedIn: 'root' })
export class DashboardsService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/dashboards${suffix}`;
  }

  list(orgId: string): Observable<DashboardSummary[]> {
    return this.http.get<DashboardSummary[]>(this.url(orgId));
  }

  get(orgId: string, id: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(this.url(orgId, `/${id}`));
  }

  create(orgId: string, name: string, definition: DashboardDefinition = { widgets: [], filters: [] }): Observable<Dashboard> {
    return this.http.post<Dashboard>(this.url(orgId), { name, definition });
  }

  update(orgId: string, dashboard: Dashboard, definition: DashboardDefinition): Observable<Dashboard> {
    return this.http.put<Dashboard>(this.url(orgId, `/${dashboard.id}`), {
      name: dashboard.name,
      description: dashboard.description,
      definition,
    });
  }

  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  run(orgId: string, id: string, filters: FilterValues): Observable<DashboardRun> {
    return this.http.post<DashboardRun>(this.url(orgId, `/${id}/run`), { filters });
  }
}
