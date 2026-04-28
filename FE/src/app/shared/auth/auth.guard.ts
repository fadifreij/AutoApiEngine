import { isPlatformBrowser } from '@angular/common';
import { inject, PLATFORM_ID } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Protects dashboard routes — unauthenticated users go to login */
export const authGuard: CanActivateFn = () => {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return true;
    const auth = inject(AuthService);
    if (auth.isAuthenticated()) return true;
    inject(Router).navigate(['/login']);
    return false;
};

/** Protects public routes — already-logged-in users go straight to dashboard */
export const publicGuard: CanActivateFn = () => {
    if (!isPlatformBrowser(inject(PLATFORM_ID))) return true;
    const auth = inject(AuthService);
    if (auth.isAuthenticated()) {
        inject(Router).navigate(['/app/dashboard']);
        return false;
    }
    return true;
};
