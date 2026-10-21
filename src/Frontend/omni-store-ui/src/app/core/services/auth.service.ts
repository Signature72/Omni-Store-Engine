import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap, catchError, throwError } from 'rxjs';
import { AuthResponse, User } from '../models/models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  // YARP API Gateway URL
  private readonly gatewayUrl = 'http://localhost:5000/api/auth';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getStoredUser());
  public currentUser$ = this.currentUserSubject.asObservable();
  public currentUserSignal = signal<User | null>(this.getStoredUser());

  constructor(private http: HttpClient) {}

  private getStoredUser(): User | null {
    const raw = localStorage.getItem('omni_user');
    return raw ? JSON.parse(raw) : null;
  }

  public getAccessToken(): string | null {
    return localStorage.getItem('omni_access_token');
  }

  public getRefreshToken(): string | null {
    return localStorage.getItem('omni_refresh_token');
  }

  public register(payload: { email: string; password: string; fullName: string; lineUserId?: string }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.gatewayUrl}/register`, payload).pipe(
      tap(res => this.handleAuthSuccess(res))
    );
  }

  public login(payload: { email: string; password: string }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.gatewayUrl}/login`, payload).pipe(
      tap(res => this.handleAuthSuccess(res))
    );
  }

  public refreshToken(): Observable<AuthResponse> {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }

    return this.http.post<AuthResponse>(`${this.gatewayUrl}/refresh`, { refreshToken }).pipe(
      tap(res => this.handleAuthSuccess(res)),
      catchError(err => {
        this.logout();
        return throwError(() => err);
      })
    );
  }

  private handleAuthSuccess(res: AuthResponse): void {
    localStorage.setItem('omni_access_token', res.accessToken);
    localStorage.setItem('omni_refresh_token', res.refreshToken);

    const user: User = {
      userId: res.userId,
      email: res.email,
      fullName: res.fullName,
      role: res.role
    };

    localStorage.setItem('omni_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
    this.currentUserSignal.set(user);
  }

  public logout(): void {
    localStorage.removeItem('omni_access_token');
    localStorage.removeItem('omni_refresh_token');
    localStorage.removeItem('omni_user');
    this.currentUserSubject.next(null);
    this.currentUserSignal.set(null);
  }

  public isAuthenticated(): boolean {
    return !!this.getAccessToken();
  }
}
