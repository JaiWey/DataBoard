import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';

interface HealthResponse {
  status: string;
  timeUtc: string;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatButtonModule],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected readonly title = signal('DataBoard');
  protected readonly health = signal<HealthResponse | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit() {
    this.checkHealth();
  }

  checkHealth() {
    this.http.get<HealthResponse>('/api/health').subscribe({
      next: (result) => {
        this.health.set(result);
        this.error.set(null);
      },
      error: () => {
        this.health.set(null);
        this.error.set('API is not reachable. Start the ASP.NET backend and refresh.');
      },
    });
  }

  protected logout() {
    this.auth.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
