import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChartQuery, ChartResult } from '../organizations/datasets.service';

/** How a saved chart is drawn: ECharts kinds, plus a plain table and a single big number. */
export type ChartType = 'Table' | 'Bar' | 'Line' | 'Pie' | 'Area' | 'BarHorizontal' | 'Donut' | 'Number' | 'Heatmap' | 'Scatter';

export const CHART_TYPES: { value: ChartType; label: string }[] = [
  { value: 'Table', label: 'Table' },
  { value: 'Bar', label: 'Bar' },
  { value: 'BarHorizontal', label: 'Horizontal bar' },
  { value: 'Line', label: 'Line' },
  { value: 'Area', label: 'Area' },
  { value: 'Pie', label: 'Pie' },
  { value: 'Donut', label: 'Donut' },
  { value: 'Number', label: 'Number' },
  { value: 'Heatmap', label: 'Heatmap' },
  { value: 'Scatter', label: 'Scatter / bubble' },
];

export const PALETTE_NAMES = ['default', 'warm', 'cool', 'mono'];

export interface ChartStyle {
  legend: boolean;
  labels: boolean;
  palette: string;
  /** Series are piled on top of each other (bars, lines and areas). */
  stack: boolean;
  /** Stacked, and each category scaled to 100%. Implies `stack`. */
  percent: boolean;
}

export const defaultChartStyle = (): ChartStyle => ({ legend: true, labels: false, palette: 'default', stack: false, percent: false });

/** The stored part of a chart: its query (row limit and sorting included) and style. */
export interface ChartDefinition {
  query: ChartQuery;
  style: ChartStyle;
}

export interface Chart {
  id: string;
  name: string;
  description: string | null;
  datasetId: string;
  type: ChartType;
  definition: ChartDefinition;
  createdUtc: string;
  updatedUtc: string;
}

export interface SaveChart {
  name: string;
  description?: string | null;
  datasetId: string;
  type: ChartType;
  definition: ChartDefinition;
}

@Injectable({ providedIn: 'root' })
export class ChartsService {
  private readonly http = inject(HttpClient);

  private url(orgId: string, suffix = ''): string {
    return `/api/organizations/${orgId}/charts${suffix}`;
  }

  list(orgId: string): Observable<Chart[]> {
    return this.http.get<Chart[]>(this.url(orgId));
  }

  get(orgId: string, id: string): Observable<Chart> {
    return this.http.get<Chart>(this.url(orgId, `/${id}`));
  }

  create(orgId: string, chart: SaveChart): Observable<Chart> {
    return this.http.post<Chart>(this.url(orgId), chart);
  }

  update(orgId: string, id: string, chart: SaveChart): Observable<Chart> {
    return this.http.put<Chart>(this.url(orgId, `/${id}`), chart);
  }

  /** Also takes the chart off every dashboard that showed it. */
  remove(orgId: string, id: string): Observable<void> {
    return this.http.delete<void>(this.url(orgId, `/${id}`));
  }

  run(orgId: string, id: string): Observable<ChartResult> {
    return this.http.post<ChartResult>(this.url(orgId, `/${id}/run`), {});
  }
}
