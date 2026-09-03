import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <mat-card class="auth">
      <mat-card-header><mat-card-title>Create account</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>Name</mat-label>
            <input matInput type="text" formControlName="displayName" autocomplete="name" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Email</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="username" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="new-password" />
            <mat-hint>At least 10 characters</mat-hint>
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">Register</button>
        </form>
        @for (message of errors(); track message) {
          <p class="fail" role="alert">{{ message }}</p>
        }
        <p>Already registered? <a routerLink="/login">Sign in</a></p>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .auth { max-width: 24rem; margin: 2rem auto; }
    mat-card-content { padding-top: 1rem; }
    form { display: grid; gap: 0.25rem; }
  `,
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });
  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.errors.set([]);

    const { email, password, displayName } = this.form.getRawValue();
    this.auth.register(email, password, displayName).subscribe({
      next: () => this.router.navigate(['/organizations']),
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.errors.set(this.messagesFrom(e));
      },
    });
  }

  /** Reads the API's validation problem details: { errors: { Field: [messages] } }. */
  private messagesFrom(e: HttpErrorResponse): string[] {
    const fields = e.error?.errors as Record<string, string[]> | undefined;
    const messages = fields ? Object.values(fields).flat() : [];
    return messages.length ? messages : ['Registration failed. Try again.'];
  }
}
