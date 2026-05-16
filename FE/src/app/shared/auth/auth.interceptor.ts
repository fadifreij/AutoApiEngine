import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';

const AUTH_API_PREFIX = `${environment.apiUrl}/auth`;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.url.startsWith(AUTH_API_PREFIX)) {
    return next(req);
  }

  const authService = inject(AuthService);
  const token = authService.getAccessToken();

  if (token) {
    const cloned = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
    return next(cloned);
  }

  return next(req);
};
