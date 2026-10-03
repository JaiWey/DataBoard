import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<LoginComponent>;

  const open = (query: Record<string, string> = {}, gitHub = false) => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    http.expectOne('/api/auth/providers').flush({ gitHub });
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  afterEach(() => http.verify());

  it('has no message and no GitHub link when nothing happened and GitHub is not set up', () => {
    const el = open();

    expect(el.querySelector('[role=alert]')).toBeNull();
    expect(el.querySelector('a.github')).toBeNull();
  });

  it('offers GitHub when the server has it', () => {
    expect(open({}, true).querySelector('a.github')).not.toBeNull();
  });

  it('says what went wrong when a sign-in with GitHub ends here', () => {
    expect(open({ error: 'github' }).querySelector('[role=alert]')?.textContent).toContain('Could not sign in with GitHub');
  });

  it('says why when GitHub shared no verified email', () => {
    expect(open({ error: 'github-email' }).querySelector('[role=alert]')?.textContent).toContain('verified email');
  });

  it('shows nothing for an error code it does not know', () => {
    expect(open({ error: '<script>alert(1)</script>' }).querySelector('[role=alert]')).toBeNull();
  });

  describe('coming back to where the person was going', () => {
    const signIn = (el: HTMLElement) => {
      const set = (selector: string, value: string) => {
        const input = el.querySelector(selector) as HTMLInputElement;
        input.value = value;
        input.dispatchEvent(new Event('input'));
      };
      set('input[type=email]', 'a@b.c');
      set('input[type=password]', 'password-123456');
      fixture.detectChanges(); // the button is enabled once the form is valid
      (el.querySelector('button[type=submit]') as HTMLButtonElement).click();
      http.expectOne('/api/auth/login').flush({
        accessToken: 't', accessTokenExpiresUtc: '2099-01-01T00:00:00Z', user: { id: '1', email: 'a@b.c', displayName: 'A' },
      });
    };

    it('goes there after signing in', () => {
      const el = open({ returnUrl: '/shared/T0KEN' });
      const go = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

      signIn(el);

      expect(go).toHaveBeenCalledWith('/shared/T0KEN');
    });

    it('goes home when there is nowhere to go back to, and when the address could lead away', () => {
      const queries: Record<string, string>[] = [{}, { returnUrl: '//evil.example' }, { returnUrl: 'https://evil.example' }];
      for (const query of queries) {
        TestBed.resetTestingModule();
        const el = open(query);
        const go = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

        signIn(el);

        expect(go).toHaveBeenCalledWith('/');
        http.verify();
      }
    });

    it('keeps it for the register link and for GitHub', () => {
      const el = open({ returnUrl: '/shared/T0KEN' }, true);

      expect((el.querySelector('a[href^="/register"]') as HTMLAnchorElement).getAttribute('href')).toBe('/register?returnUrl=%2Fshared%2FT0KEN');
      expect((el.querySelector('a.github') as HTMLAnchorElement).getAttribute('href')).toBe('/api/auth/github?returnUrl=%2Fshared%2FT0KEN');
    });

    it('adds nothing to those links when there is nowhere to go back to', () => {
      const el = open({}, true);

      expect((el.querySelector('a[href^="/register"]') as HTMLAnchorElement).getAttribute('href')).toBe('/register');
      expect((el.querySelector('a.github') as HTMLAnchorElement).getAttribute('href')).toBe('/api/auth/github');
    });
  });
});
