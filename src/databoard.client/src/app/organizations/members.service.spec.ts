import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MEMBER_ROLES, MembersService } from './members.service';

describe('MembersService', () => {
  let api: MembersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(MembersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists, adds, changes and removes through the members address of the organization', () => {
    api.list('o1').subscribe();
    http.expectOne({ method: 'GET', url: '/api/organizations/o1/members' }).flush([]);

    api.add('o1', 'a@example.com', 'Editor').subscribe();
    expect(http.expectOne({ method: 'POST', url: '/api/organizations/o1/members' }).request.body).toEqual({ email: 'a@example.com', role: 'Editor' });

    api.changeRole('o1', 'u1', 'Owner').subscribe();
    expect(http.expectOne({ method: 'PUT', url: '/api/organizations/o1/members/u1' }).request.body).toEqual({ role: 'Owner' });

    api.remove('o1', 'u1').subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/organizations/o1/members/u1' });
  });

  it('has the three roles lowest first, each with a hint', () => {
    expect(MEMBER_ROLES.map((r) => r.value)).toEqual(['Viewer', 'Editor', 'Owner']);
    expect(MEMBER_ROLES.every((r) => r.hint.length > 0)).toBe(true);
  });
});
