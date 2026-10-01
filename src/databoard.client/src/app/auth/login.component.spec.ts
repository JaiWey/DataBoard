import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let http: HttpTestingController;

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
    const fixture = TestBed.createComponent(LoginComponent);
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
});
