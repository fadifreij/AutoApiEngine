import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from '../../auth/store/auth.store';

// ── Auth Guard — blocks unauthenticated users ──────────────────────────────
export const authGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (authStore.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/auth/signin'], {
    queryParams: { returnUrl: state.url },
  });
};

// ── Guest Guard — blocks already-logged-in users ───────────────────────────
export const guestGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};

// ── Role Guard factory ─────────────────────────────────────────────────────
import { UserRole } from '../../auth/store/auth.store';

export const roleGuard = (requiredRole: UserRole): CanActivateFn => {
  return () => {
    const authStore = inject(AuthStore);
    const router = inject(Router);
    const user = authStore.currentUser();

    if (!user) return router.createUrlTree(['/auth/signin']);

    const hierarchy: UserRole[] = ['viewer', 'developer', 'admin'];
    const userLevel = hierarchy.indexOf(user.role);
    const requiredLevel = hierarchy.indexOf(requiredRole);

    if (userLevel >= requiredLevel) return true;

    return router.createUrlTree(['/dashboard']);
  };
};
