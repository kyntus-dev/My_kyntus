import { TestBed } from '@angular/core/testing';
import { authInterceptor } from '../../interceptors/auth.interceptor';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../../services/auth.service';

describe('Mandatory first login', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({ imports: [LoginComponent],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()]
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function start() {
    const fixture = TestBed.createComponent(LoginComponent); fixture.detectChanges();
    const c = fixture.componentInstance;
    c.loginForm.setValue({ email: 'pilote@test.invalid', password: 'Temporary-Tests-2026!' }); c.onSubmit();
    http.expectOne(r => r.url.endsWith('/Auth/login')).flush({
      code: 'PASSWORD_CHANGE_REQUIRED', changeToken: 'A'.repeat(64), expiresIn: 600
    }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    return { fixture, c };
  }
  it('shows mandatory form without storing session and returns to login after saving', () => {
    const { fixture, c } = start();
    expect(c.requiresPasswordChange).toBe(true);
    expect(fixture.nativeElement.querySelector('#new-password')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#email')).toBeNull();
    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
    expect(localStorage.getItem('access_token')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('A'.repeat(64));
    c.passwordChangeForm.setValue({ password: 'Personal-Tests-2026!', confirmation: 'Personal-Tests-2026!' }); c.completeFirstLogin();
    const request = http.expectOne(r => r.url.endsWith('/Auth/complete-first-login'));
    expect(request.request.body).toEqual({ changeToken: 'A'.repeat(64), newPassword: 'Personal-Tests-2026!', confirmPassword: 'Personal-Tests-2026!' });
    request.flush({ message: 'OK' }); fixture.detectChanges();
    expect(c.requiresPasswordChange).toBe(false);
    expect(c.successMessage).toContain('enregistré');
    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
  });
  it('rejects mismatched confirmation and clears expired challenge', () => {
    const { c } = start();
    c.passwordChangeForm.setValue({ password: 'Personal-Tests-2026!', confirmation: 'Other-Tests-2026!' }); c.completeFirstLogin();
    http.expectNone(r => r.url.endsWith('/Auth/complete-first-login'));
    expect(c.errorMessage).toContain('correspondent');
    c.passwordChangeForm.patchValue({ confirmation: 'Personal-Tests-2026!' }); c.completeFirstLogin();
    http.expectOne(r => r.url.endsWith('/Auth/complete-first-login')).flush({ message: 'Reconnectez-vous.' }, { status: 401, statusText: 'Unauthorized' });
    expect(c.requiresPasswordChange).toBe(false);
    c.completeFirstLogin(); http.expectNone(r => r.url.endsWith('/Auth/complete-first-login'));
  });
});
