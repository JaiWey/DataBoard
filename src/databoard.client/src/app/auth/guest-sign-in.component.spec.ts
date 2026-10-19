import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { GuestSignInComponent } from './guest-sign-in.component';

describe('GuestSignInComponent', () => {
  let http: HttpTestingController;
  const navigateByUrl = vi.fn();

  const open = (answer: () => void) => {
    const fixture = TestBed.createComponent(GuestSignInComponent);
    fixture.detectChanges();
    answer();
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    navigateByUrl.mockReset();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Router, useValue: { navigateByUrl } }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('offers nothing when guests are off', () => {
    const el = open(() => http.expectOne('/api/auth/providers').flush({ gitHub: false, guest: false })).nativeElement as HTMLElement;

    expect(el.querySelector('button')).toBeNull();
  });

  it('signs in a guest and goes to the app', () => {
    const fixture = open(() => http.expectOne('/api/auth/providers').flush({ gitHub: false, guest: true }));
    const button = (fixture.nativeElement as HTMLElement).querySelector('button.guest') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Continue as guest');

    button.click();
    http.expectOne('/api/auth/guest').flush({
      accessToken: 't',
      accessTokenExpiresUtc: '2030-01-01T00:00:00Z',
      user: { id: '1', email: 'guest-1@guest.invalid', displayName: 'Guest' },
    });

    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('says so when it fails', () => {
    const fixture = open(() => http.expectOne('/api/auth/providers').flush({ guest: true }));
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.guest')!.click();
    http.expectOne('/api/auth/guest').flush({}, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('Too many guests');
  });
});
