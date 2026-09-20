import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemMessages } from '../organizations/data-sources.service';
import { Dashboard, DashboardsService } from './dashboards.service';

export interface NewDashboardDialogData {
  orgId: string;
  /** The folder the dashboard is filed in; missing or null is the top level. */
  folderId?: string | null;
}

/** Asks for a name and creates an empty dashboard. Closes with the created dashboard, or with nothing if cancelled. */
@Component({
  selector: 'app-new-dashboard-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>New dashboard</h2>
    <form [formGroup]="form" (ngSubmit)="create()">
      <mat-dialog-content>
        <div class="fields">
          <mat-form-field><mat-label>Name</mat-label><input matInput formControlName="name" aria-label="Dashboard name" cdkFocusInitial /></mat-form-field>
          <mat-form-field>
            <mat-label>Description (optional)</mat-label>
            <input matInput formControlName="description" aria-label="Description" />
          </mat-form-field>
        </div>

        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>

      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Create dashboard</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `.fields { display: grid; padding-top: 0.5rem; }`,
})
export class NewDashboardDialogComponent {
  private readonly data = inject<NewDashboardDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NewDashboardDialogComponent, Dashboard>>(MatDialogRef);
  private readonly api = inject(DashboardsService);
  private readonly fb = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', Validators.maxLength(1000)],
  });

  protected create(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);

    const v = this.form.getRawValue();
    this.api.create(this.data.orgId, v.name, undefined, v.description || undefined, this.data.folderId).subscribe({
      next: (created) => this.ref.close(created),
      error: (e) => {
        this.busy.set(false);
        this.errors.set(problemMessages(e, 'Could not create the dashboard.'));
      },
    });
  }
}
