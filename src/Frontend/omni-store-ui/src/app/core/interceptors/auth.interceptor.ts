import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getAccessToken();

  let authReq = req;
  if (token && !req.url.includes('/api/auth/login') && !req.url.includes('/api/auth/register')) {
    authReq = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      // If 401 Unauthorized and not already refreshing, attempt silent token refresh
      if (error.status === 401 && !req.url.includes('/api/auth/refresh')) {
        return authService.refreshToken().pipe(
          switchMap(newTokens => {
            const retriedReq = req.clone({
              setHeaders: {
                Authorization: `Bearer ${newTokens.accessToken}`
              }
            });
            return next(retriedReq);
          }),
          catchError(refreshErr => {
            authService.logout();
            return throwError(() => refreshErr);
          })
        );
      }

      return throwError(() => error);
    })
  );
};
