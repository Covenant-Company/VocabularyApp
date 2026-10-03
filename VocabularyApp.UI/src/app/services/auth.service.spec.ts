import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';
import { currentAuthSuccess } from '../testing/api-contract.fixtures';
import { ApiContractError } from './api-error';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    // Do not inherit a browser session or modify persistent storage.
    spyOn(localStorage, 'getItem').and.returnValue(null);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AuthService);
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
    expect(service.getCurrentUser()).toBeNull();
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('passes through the current nested registration response without starting a session', () => {
    const setItem = spyOn(localStorage, 'setItem');
    const body = { username: 'contract-user', email: 'contract@example.test', password: 'Test password!' };
    const fixture = currentAuthSuccess();
    let received: unknown;
    service.register(body).subscribe(value => received = value);
    const request = TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/register');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush(fixture);
    expect(received).toEqual(fixture);
    expect(setItem).not.toHaveBeenCalled();
    expect(service.getCurrentUser()).toBeNull();
  });

  it('accepts the current login fixture without a server expiresAt field', () => {
    const setItem = spyOn(localStorage, 'setItem');
    const body = { username: 'contract-user', password: 'Test password!' };
    const fixture = currentAuthSuccess('2026-01-02T03:05:05Z');
    let received: unknown;
    service.login(body).subscribe(value => received = value);
    const request = TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush(fixture);
    expect(received).toEqual(fixture);
    expect(Object.keys(fixture.data).sort()).toEqual(['errorMessage', 'success', 'token', 'user']);
    expect(setItem).toHaveBeenCalledWith('vocab_app_token', fixture.data.token);
    expect(setItem).toHaveBeenCalledWith('vocab_app_user', JSON.stringify(fixture.data.user));
    expect(service.getCurrentUser()).toEqual(fixture.data.user);
  });

  const invalidResponses: unknown[] = [null, {}, { success: false }, { success: true, data: null },
    { ...currentAuthSuccess(), data: { ...currentAuthSuccess().data, token: null } },
    { ...currentAuthSuccess(), data: { ...currentAuthSuccess().data, token: ' ' } },
    { ...currentAuthSuccess(), data: { ...currentAuthSuccess().data, user: null } },
    { ...currentAuthSuccess(), data: { ...currentAuthSuccess().data, user: {} } },
    { ...currentAuthSuccess(), data: { ...currentAuthSuccess().data, success: false } }
  ];
  for (const [index, body] of invalidResponses.entries()) {
    it(`rejects malformed login success ${index} without altering an existing session`, () => {
      const storage = new Map<string, string>();
      (localStorage.getItem as jasmine.Spy).and.callFake((key: string) => storage.get(key) ?? null);
      const setItem = spyOn(localStorage, 'setItem').and.callFake((key: string, value: string) => { storage.set(key, value); });
      const removeItem = spyOn(localStorage, 'removeItem');
      service.login({ username: 'contract-user', password: 'Test password!' }).subscribe();
      const validSession = currentAuthSuccess();
      validSession.data.token = jwt({ exp: Math.floor(Date.now() / 1000) + 3600 });
      TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/login').flush(validSession);
      expect(service.isAuthenticated()).toBeTrue();
      const previousUser = service.getCurrentUser();
      const previousStorage = [...storage];
      setItem.calls.reset();
      const error = jasmine.createSpy('error');
      service.login({ username: 'other-user', password: 'Test password!' }).subscribe({ next: () => fail('Malformed login succeeded'), error });
      TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/login').flush(body as object | null);
      expect(error.calls.mostRecent().args[0]).toEqual(jasmine.any(ApiContractError));
      expect(setItem).not.toHaveBeenCalled();
      expect(removeItem).not.toHaveBeenCalled();
      expect([...storage]).toEqual(previousStorage);
      expect(service.getCurrentUser()).toBe(previousUser);
      expect(service.isAuthenticated()).toBeTrue();
    });
  }
  for (const status of [401, 500]) {
    it(`does not write or clear storage on login HTTP ${status}`, () => {
      const setItem = spyOn(localStorage, 'setItem');
      const removeItem = spyOn(localStorage, 'removeItem');
      const error = jasmine.createSpy('error');
      service.login({ username: 'contract-user', password: 'Test password!' }).subscribe({ error });
      TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/login').flush({ error: 'failure' }, { status, statusText: 'Failure' });
      expect(error).toHaveBeenCalledTimes(1);
      expect(setItem).not.toHaveBeenCalled();
      expect(removeItem).not.toHaveBeenCalled();
    });
  }
  it('excludes confirmPassword even if a caller passes an extended registration object', () => {
    const body = { username: 'contract-user', email: 'contract@example.test', password: 'Test password!', confirmPassword: 'UI only' };
    service.register(body).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/register');
    expect(request.request.body).toEqual({ username: body.username, email: body.email, password: body.password });
    request.flush(currentAuthSuccess());
  });
  it('rejects malformed registration success without storage writes', () => {
    const setItem = spyOn(localStorage, 'setItem');
    const error = jasmine.createSpy('error');
    service.register({ username: 'contract-user', email: 'contract@example.test', password: 'Test password!' }).subscribe({ error });
    TestBed.inject(HttpTestingController).expectOne(environment.apiUrl + '/users/register').flush({ success: true, data: currentAuthSuccess().data.user });
    expect(error.calls.mostRecent().args[0]).toEqual(jasmine.any(ApiContractError));
    expect(setItem).not.toHaveBeenCalled();
  });
  function jwt(payload: unknown): string {
    return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}.testSignature`;
  }
  it('accepts unpadded base64url payloads containing URL-specific characters', () => {
    spyOn(Date, 'now').and.returnValue(1000);
    const token = jwt({ exp: 100, label: '~~~???' });
    expect(token.split('.')[1]).toMatch(/[-_]/);
    (localStorage.getItem as jasmine.Spy).and.returnValue(token);
    expect(service.isAuthenticated()).toBeTrue();
  });
  for (const token of [jwt({ exp: 1 }), jwt({ exp: '9999999999' }), jwt({}), 'broken', 'a.@@@@.b', 'a.e30.b.extra', 'a.bnVsbA.b']) {
    it(`rejects expired or malformed JWT ${token}`, () => {
      spyOn(Date, 'now').and.returnValue(2000);
      (localStorage.getItem as jasmine.Spy).and.returnValue(token);
      expect(service.isAuthenticated()).toBeFalse();
    });
  }
});
