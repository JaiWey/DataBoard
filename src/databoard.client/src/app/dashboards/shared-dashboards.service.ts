import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChartStyle, ChartType } from '../charts/charts.service';
import { DashboardBackground, DashboardRun, DashboardTab, DashboardWidget, FilterValues, SharedFilterOperator } from './dashboards.service';

/** What a visitor with a link needs to draw one chart: how it looks, not where its data comes from. */
export interface SharedChart {
  id: string;
  name: string;
  type: ChartType;
  style: ChartStyle;
  dimensionCount: number;
}

export interface SharedFilter {
  id: string;
  label: string;
  operator: SharedFilterOperator;
}

export interface SharedDashboard {
  name: string;
  description: string | null;
  widgets: DashboardWidget[];
  filters: SharedFilter[];
  charts: SharedChart[];
  expiresUtc: string | null;
  background?: DashboardBackground | null;
  /** Missing or empty: one page. */
  tabs?: DashboardTab[] | null;
}

/** The public side of a dashboard link: no account, the token in the address is the credential. */
@Injectable({ providedIn: 'root' })
export class SharedDashboardsService {
  private readonly http = inject(HttpClient);

  private url(token: string, suffix = ''): string {
    return `/api/shared/dashboards/${encodeURIComponent(token)}${suffix}`;
  }

  get(token: string): Observable<SharedDashboard> {
    return this.http.get<SharedDashboard>(this.url(token));
  }

  /** The picture behind the dashboard, as a file. */
  backgroundImage(token: string, version: string): Observable<Blob> {
    return this.http.get(this.url(token, '/background'), { params: { v: version }, responseType: 'blob' });
  }

  /** The picture of a picture widget, as a file. */
  widgetImage(token: string, widgetId: string, version: string): Observable<Blob> {
    return this.http.get(this.url(token, `/images/${widgetId}`), { params: { v: version }, responseType: 'blob' });
  }

  run(token: string, filters: FilterValues): Observable<DashboardRun> {
    return this.http.post<DashboardRun>(this.url(token, '/run'), { filters });
  }
}
