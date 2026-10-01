import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GitHubSignInComponent } from './github-sign-in.component';

describe('GitHubSignInComponent', () => {
  let http: HttpTestingController;

  const open = (answer: () => void) => {
    const fixture = TestBed.createComponent(GitHubSignInComponent);
    fixture.detectChanges();
    answer();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('offers a plain link to GitHub when the server has it set up', () => {
    const el = open(() => http.expectOne('/api/auth/providers').flush({ gitHub: true }));

    const link = el.querySelector('a.github') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/api/auth/github');
    expect(link.textContent?.trim()).toBe('Continue with GitHub');
    expect(el.querySelector('.or')).not.toBeNull();
  });

  it('offers nothing when it is not set up', () => {
    const el = open(() => http.expectOne('/api/auth/providers').flush({ gitHub: false }));

    expect(el.querySelector('a')).toBeNull();
    expect(el.textContent?.trim()).toBe('');
  });

  it('offers nothing when the server cannot be asked, and leaves the password form alone', () => {
    const el = open(() => http.expectOne('/api/auth/providers').flush({}, { status: 500, statusText: 'Server Error' }));

    expect(el.querySelector('a')).toBeNull();
  });

  it('offers nothing for an answer it does not understand', () => {
    const el = open(() => http.expectOne('/api/auth/providers').flush({ gitHub: 'yes' }));

    expect(el.querySelector('a')).toBeNull();
  });
});
