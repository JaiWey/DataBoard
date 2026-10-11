import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

interface Organization {
  id: string;
  name: string;
  role: 'Viewer' | 'Editor' | 'Owner';
  isDefault?: boolean;
}

@Component({
  selector: 'app-organizations',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2>Your organizations</h2>

    @if (organizations().length === 0) {
      <p><em>You are not in any organization yet. Create one below.</em></p>
    } @else {
      <div class="orgs">
        @for (org of organizations(); track org.id) {
          <mat-card appearance="outlined">
            <mat-card-header>
              <mat-card-title>{{ org.name }}</mat-card-title>
              <mat-card-subtitle>{{ org.role }}@if (org.isDefault) { · Default }</mat-card-subtitle>
            </mat-card-header>
            <mat-card-actions>
              <a mat-button [routerLink]="['/organizations', org.id]">Data sources</a>
              <a mat-button [routerLink]="['/organizations', org.id, 'datasets']">Datasets</a>
              <a mat-button [routerLink]="['/organizations', org.id, 'dashboards']">Dashboards</a>
              <a mat-button [routerLink]="['/organizations', org.id, 'members']">Members</a>
            </mat-card-actions>
          </mat-card>
        }
      </div>
    }

    <h3>New organization</h3>
    <form [formGroup]="form" (ngSubmit)="create()" class="form-row">
      <mat-form-field>
        <mat-label>Organization name</mat-label>
        <input matInput type="text" formControlName="name" aria-label="Organization name" />
      </mat-form-field>
      <button mat-flat-button type="submit" class="create" [disabled]="form.invalid || busy()">Create</button>
    </form>

    @if (error(); as e) {
      <p class="fail" role="alert">{{ e }}</p>
    }
  `,
  styles: `
    .orgs { display: grid; grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr)); gap: 1rem; }
    .create { margin-top: 0.4rem; }
  `,
})
export class OrganizationsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  protected readonly organizations = signal<Organization[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected create(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);

    this.http.post<Organization>('/api/organizations', this.form.getRawValue()).subscribe({
      next: () => {
        this.form.reset();
        this.busy.set(false);
        this.load();
      },
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(e.status === 400 ? 'Please enter a valid name.' : 'Could not create the organization.');
      },
    });
  }

  private load(): void {
    this.http.get<Organization[]>('/api/organizations').subscribe({
      next: (items) => this.organizations.set(items),
      error: () => this.error.set('Could not load organizations.'),
    });
  }
}
