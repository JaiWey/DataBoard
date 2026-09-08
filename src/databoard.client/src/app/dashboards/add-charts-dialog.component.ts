import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { CHART_TYPES, Chart } from '../charts/charts.service';

export interface AddChartsDialogData {
  orgId: string;
  /** Charts that are not on the dashboard yet. */
  charts: Chart[];
  datasetNames: Record<string, string>;
}

/** Pick saved charts to put on a dashboard, or go and make a new one. Closes with the chosen chart ids. */
@Component({
  selector: 'app-add-charts-dialog',
  imports: [MatDialogModule, MatButtonModule, MatCheckboxModule, RouterLink],
  template: `
    <h2 mat-dialog-title>Add charts</h2>
    <mat-dialog-content>
      @if (data.charts.length === 0) {
        <p><em>Every chart is already on this dashboard, or there are no charts yet.</em></p>
      }
      <ul class="list">
        @for (chart of data.charts; track chart.id) {
          <li>
            <mat-checkbox [checked]="chosen().has(chart.id)" (change)="toggle(chart.id, $event.checked)">
              {{ chart.name }}
              <small>{{ typeLabel(chart.type) }} · {{ data.datasetNames[chart.datasetId] ?? 'unknown dataset' }}</small>
            </mat-checkbox>
          </li>
        }
      </ul>
      <p>
        <a mat-button mat-dialog-close [routerLink]="['/organizations', data.orgId, 'charts', 'new']">Create a new chart</a>
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Cancel</button>
      <button mat-flat-button type="button" [disabled]="chosen().size === 0" (click)="done()">Add {{ chosen().size || '' }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .list { list-style: none; margin: 0; padding: 0; max-height: 20rem; overflow-y: auto; }
    small { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class AddChartsDialogComponent {
  protected readonly data = inject<AddChartsDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<AddChartsDialogComponent, string[]>>(MatDialogRef);

  protected readonly chosen = signal<ReadonlySet<string>>(new Set());

  protected typeLabel(type: string): string {
    return CHART_TYPES.find((t) => t.value === type)?.label ?? type;
  }

  protected toggle(id: string, checked: boolean): void {
    this.chosen.update((set) => {
      const next = new Set(set);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected done(): void {
    // Keep the list's own order, so charts land on the dashboard in the order they were shown.
    this.ref.close(this.data.charts.map((c) => c.id).filter((id) => this.chosen().has(id)));
  }
}
