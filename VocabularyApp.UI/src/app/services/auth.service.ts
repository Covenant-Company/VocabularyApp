import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';
import { LoginRequest, LoginResponse, RegisterRequest, RegisterResponse, User } from '../models/user.model';
import {environment} from "../../environments/environment";
import { ApiContractError } from './api-error';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isUser(value: unknown): value is User {
  return isRecord(value) && typeof value['id'] === 'number' && Number.isInteger(value['id']) && value['id'] > 0
    && typeof value['username'] === 'string' && value['username'].trim().length > 0
    && typeof value['email'] === 'string' && value['email'].trim().length > 0
    && typeof value['createdAt'] === 'string' && Number.isFinite(Date.parse(value['createdAt']))
    && (value['lastLoginAt'] === null || (typeof value['lastLoginAt'] === 'string' && Number.isFinite(Date.parse(value['lastLoginAt']))));
}

function requireAuthResponse(value: LoginResponse): LoginResponse & { data: { success: true; errorMessage: null; user: User; token: string } } {
  if (!value || value.success !== true || value.data?.success !== true || value.data.errorMessage !== null
    || typeof value.data.token !== 'string' || !value.data.token.trim() || !isUser(value.data.user)) {
    throw new ApiContractError();
  }
  // Narrow fields after checking the untrusted runtime response.
  return { ...value, data: { success: true, errorMessage: null, user: value.data.user, token: value.data.token } };
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = environment.apiUrl + '/users';
  private readonly TOKEN_KEY = 'vocab_app_token';
  private readonly USER_KEY = 'vocab_app_user';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getUserFromStorage());
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {}

  register(request: RegisterRequest): Observable<RegisterResponse> {
    const { username, email, password } = request;
    return this.http.post<RegisterResponse>(`${this.API_URL}/register`, { username, email, password })
      .pipe(map(requireAuthResponse));
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    const { username, password } = request;
    return this.http.post<LoginResponse>(`${this.API_URL}/login`, { username, password })
      .pipe(
        map(requireAuthResponse),
        tap(response => {
          this.setSession(response.data.token, response.data.user);
        })
      );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.currentUserSubject.next(null);
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) return false;

    // Check if token is expired
    try {
      const segments = token.split('.');
      if (segments.length !== 3 || segments.some(segment => !/^[A-Za-z0-9_-]+$/.test(segment))) return false;
      const headerPart = segments[0].replace(/-/g, '+').replace(/_/g, '/');
      const header: unknown = JSON.parse(atob(headerPart.padEnd(Math.ceil(headerPart.length / 4) * 4, '=')));
      if (!isRecord(header) || typeof header['alg'] !== 'string' || !header['alg'] || header['alg'] === 'none') return false;
      const payload = segments[1].replace(/-/g, '+').replace(/_/g, '/');
      const decoded: unknown = JSON.parse(atob(payload.padEnd(Math.ceil(payload.length / 4) * 4, '=')));
      // Expiry is a UI hint. The backend remains responsible for signature validation.
      return isRecord(decoded) && typeof decoded['exp'] === 'number' && Number.isFinite(decoded['exp'])
        && decoded['exp'] > Date.now() / 1000;
    } catch {
      return false;
    }
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  private setSession(token: string, user: User): void {
    localStorage.setItem(this.TOKEN_KEY, token);
    localStorage.setItem(this.USER_KEY, JSON.stringify(user));
    this.currentUserSubject.next(user);
  }

  private getUserFromStorage(): User | null {
    const userStr = localStorage.getItem(this.USER_KEY);
    if (userStr && this.isAuthenticated()) {
      try {
        const user: unknown = JSON.parse(userStr);
        return isUser(user) ? user : null;
      } catch {
        return null;
      }
    }
    return null;
  }
}
