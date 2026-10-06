import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { OrgContext } from './organizations/org-context.service';
import { LoadingBarComponent } from './shared/loading-bar.component';
import { quietly } from './shared/loading.service';

interface HealthResponse {
  status: string;
  timeUtc: string;
}

const ORG_IN_URL = /^\/organizations\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i;

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, LoadingBarComponent],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly orgs = inject(OrgContext);
  protected readonly auth = inject(AuthService);

  protected readonly title = signal('DataBoard');
  protected readonly health = signal<HealthResponse | null>(null);
  protected readonly error = signal<string | null>(null);

  /** A page opened from a public link has no navigation or status of its own to show: it is the dashboard and nothing else. */
  protected readonly publicPage = signal(false);

  /** The organization the navigation links point at. */
  protected readonly orgId = computed(() => this.orgs.current()?.id ?? null);

  protected readonly initials = computed(() => {
    const name = this.auth.user()?.displayName?.trim() ?? '';
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0].toUpperCase()).join('') || '?';
  });

  constructor() {
    // Organizations are loaded once someone is signed in, and forgotten when they sign out.
    effect(() => {
      if (this.auth.user()) this.orgs.load();
      else this.orgs.clear();
    });

    // Opening a page of an organization makes it the current one, so the navigation follows the user.
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe((e) => {
      this.publicPage.set(e.urlAfterRedirects.startsWith('/shared/'));
      const id = ORG_IN_URL.exec(e.urlAfterRedirects)?.[1];
      if (id) this.orgs.select(id); // an id the user does not belong to is ignored by OrgContext.current
    });
  }

  ngOnInit() {
    this.checkHealth();
  }

  checkHealth() {
    this.http.get<HealthResponse>('/api/health', { context: quietly() }).subscribe({
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
