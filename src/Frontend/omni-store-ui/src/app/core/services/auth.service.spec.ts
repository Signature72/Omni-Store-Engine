import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(AuthService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('should store tokens and user state upon successful login', () => {
    const mockResponse = {
      userId: 'u1',
      email: 'test@example.com',
      fullName: 'Test User',
      role: 'Customer',
      accessToken: 'mock_jwt_token',
      refreshToken: 'mock_refresh_token',
      expiresAtUtc: new Date().toISOString()
    };

    service.login({ email: 'test@example.com', password: 'password123' }).subscribe(res => {
      expect(res.accessToken).toBe('mock_jwt_token');
    });

    const req = httpTesting.expectOne('http://localhost:5000/api/auth/login');
    expect(req.request.method).toBe('POST');
    req.flush(mockResponse);

    expect(service.getAccessToken()).toBe('mock_jwt_token');
    expect(service.getRefreshToken()).toBe('mock_refresh_token');
    expect(service.isAuthenticated()).toBe(true);
    expect(service.currentUserSignal()?.fullName).toBe('Test User');
  });

  it('should clear tokens and state on logout', () => {
    localStorage.setItem('omni_access_token', 'token');
    localStorage.setItem('omni_user', JSON.stringify({ userId: 'u1', email: 'a@b.c', fullName: 'A B', role: 'Customer' }));

    service.logout();

    expect(service.getAccessToken()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentUserSignal()).toBeNull();
  });
});
