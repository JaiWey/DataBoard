import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  const run = () =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

  it('redirects to /login when signed out', () => {
    const result = run();

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
  });

  it('remembers the page that was asked for, so signing in comes back to it', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/organizations/1/dashboards' } as RouterStateSnapshot));

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Forganizations%2F1%2Fdashboards');
  });

  it('does not remember an address that could lead away', () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '//evil.example' } as RouterStateSnapshot));

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
  });

  it('allows navigation when signed in', () => {
    const auth = TestBed.inject(AuthService);
    auth.login('a@b.c', 'password-123456').subscribe();
    TestBed.inject(HttpTestingController).expectOne('/api/auth/login').flush({
      accessToken: 't',
      accessTokenExpiresUtc: '2099-01-01T00:00:00Z',
      user: { id: '1', email: 'a@b.c', displayName: 'A' },
    });

    expect(run()).toBe(true);
  });
});
