import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { NotificationService } from '../services/notification.service';
import { AuthStore } from '../../auth/store/auth.store';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const notify = inject(NotificationService);
  const authStore = inject(AuthStore);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      switch (err.status) {
        case 401:
          // Token expired or invalid — clear auth and redirect
          authStore.clear();
          router.navigate(['/auth/signin'], {
            queryParams: { reason: 'session_expired' },
          });
          break;

        case 403:
          notify.error('Access denied', 'You do not have permission to perform this action.');
          break;

        case 404:
          // Let components handle 404 themselves
          break;

        case 422:
          // Validation errors — let forms handle these
          break;

        case 0:
          notify.error('Network error', 'Could not reach the server. Check your connection.');
          break;

        default:
          if (err.status >= 500) {
            notify.error(
              'Server error',
              err.error?.message ?? 'Something went wrong. Please try again.'
            );
          }
      }

      return throwError(() => err);
    })
  );
};
