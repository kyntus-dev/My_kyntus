import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { authGuard } from './auth-guard';

describe('authGuard', () => {
  it('denies access without a session and preserves the return path', () => {
    const navigate = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: AuthService, useValue: { isLoggedIn: () => false } },
      { provide: Router, useValue: { navigate } }
    ] });
    const allowed = TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, { url: '/dashboard' } as RouterStateSnapshot));
    expect(allowed).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/dashboard' } });
  });
});
