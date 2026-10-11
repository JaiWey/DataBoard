import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { problemMessages } from '../organizations/data-sources.service';
import { AppSelectComponent } from '../shared/app-select.component';
import { CopyResult, DashboardsService } from './dashboards.service';

export interface CopyDashboardDialogData {
  orgId: string;
  dashboardId: string;
  dashboardName: string;
  /** The organizations the person may copy into: Editor or Owner there, and not this one. */
  targets: { id: string; name: string }[];
}

/**
 * Copies a dashboard, with its charts and datasets, into another organization of the person. Data sources are not copied
 * (they hold connection details): they are found by name in the other organization, and what needs one that is missing is
 * left out and listed, so the person knows what to set up.
 */
@Component({
  selector: 'app-copy-dashboard-dialog',
  imports: [MatDialogModule, MatButtonModule, RouterLink, AppSelectComponent],
  template: `
    <h2 mat-dialog-title>Copy to another organization</h2>
    <mat-dialog-content>
      @if (result(); as r) {
        <p class="done" role="status">
          Copied "{{ r.dashboard.name }}" to {{ targetName() }}: {{ r.newCharts }} new
          {{ r.newCharts === 1 ? 'chart' : 'charts' }} and {{ r.newDatasets }} new {{ r.newDatasets === 1 ? 'dataset' : 'datasets' }}.
        </p>
        @if (r.missingDataSources.length > 0) {
          <p class="warn" role="alert">
            {{ targetName() }} has no data source named {{ names(r.missingDataSources) }}, so
            {{ names(r.skippedCharts) }} {{ r.skippedCharts.length === 1 ? 'was' : 'were' }} left out. Add a data source with that name
            there, then copy again.
          </p>
        }
      } @else {
        <p>Copies "{{ data.dashboardName }}" with its charts and datasets. Data sources are not copied: each dataset uses the data source of the same name in the other organization.</p>
        <app-select label="Organization" ariaLabel="Copy to organization" [options]="options()" [value]="target()" (valueChange)="target.set($event)" />
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (result(); as r) {
        <a mat-button [routerLink]="['/organizations', target(), 'dashboards', r.dashboard.id]" mat-dialog-close>Open the copy</a>
        <button mat-flat-button type="button" mat-dialog-close>Done</button>
      } @else {
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="button" class="copy" [disabled]="!target() || busy()" (click)="copy()">Copy</button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    .warn { color: var(--mat-sys-error, #b3261e); }
    app-select { display: block; padding-top: 0.5rem; }
  `,
})
export class CopyDashboardDialogComponent {
  protected readonly data = inject<CopyDashboardDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(DashboardsService);

  protected readonly target = signal<string>(this.data.targets[0]?.id ?? '');
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);
  protected readonly result = signal<CopyResult | null>(null);

  protected readonly options = computed(() => this.data.targets.map((o) => ({ value: o.id, label: o.name })));
  protected readonly targetName = computed(() => this.data.targets.find((o) => o.id === this.target())?.name ?? '');

  protected names(list: string[]): string {
    return list.map((n) => `"${n}"`).join(', ');
  }

  protected copy(): void {
    if (!this.target()) return;
    this.busy.set(true);
    this.errors.set([]);
    this.api.copyToOrganization(this.data.orgId, this.data.dashboardId, this.target()).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.result.set(r);
      },
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not copy the dashboard.'));
      },
    });
  }
}
