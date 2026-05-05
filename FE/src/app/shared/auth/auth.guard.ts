import { isPlatformBrowser } from '@angular/common';
import { inject, PLATFORM_ID } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

/** Protects dashboard routes — unauthenticated users go to login */
export const authGuard: CanActivateFn = () => {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return true;
    const auth = inject(AuthService);
    const router = inject(Router);

    if (auth.isAuthenticated()) return true;

    // Session may exist via refresh token cookie — try to restore it
    return auth.initAuth().pipe(
        map(authenticated => {
            if (authenticated) return true;
            router.navigate(['/login']);
            return false;
        })
    );
};

/** Protects public routes — already-logged-in users go straight to dashboard */
export const publicGuard: CanActivateFn = () => {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return true;
    const auth = inject(AuthService);
    const router = inject(Router);

    if (auth.isAuthenticated()) {
        router.navigate(['/app/dashboard']);
        return false;
    }

    // Session may exist via refresh token cookie — try to restore it
    return auth.initAuth().pipe(
        map(authenticated => {
            if (authenticated) {
                router.navigate(['/app/dashboard']);
                return false;
            }
            return true;
        })
    );
};

