import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { KyntusThemeService } from '../../core/kyntus-theme.service';
import { brandLogoSrc } from '../../core/brand-logo';
import { KYNTUS_PUBLIC_URLS } from '../../config/kyntus-public-urls';
import { ThemeToggleButtonComponent } from '../../core/theme-toggle-button.component';

/** Accepte uniquement un chemin relatif SPA (anti open-redirect). */
function sanitizeReturnUrl(raw: string | null | undefined): string | null {
  if (!raw) return null;
  let value = raw.trim();
  if (!value) return null;
  try {
    value = decodeURIComponent(value);
  } catch {
    // déjà décodé
  }
  if (!value.startsWith('/') || value.startsWith('//')) return null;
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(value)) return null;
  return value;
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, ThemeToggleButtonComponent],
  templateUrl: './login.html',
  styleUrls: ['./login.css']
})
export class LoginComponent implements OnInit {
  readonly theme = inject(KyntusThemeService);
  private readonly route = inject(ActivatedRoute);
  loginForm!: FormGroup;
  errorMessage: string = '';
  loading: boolean = false;
  showPassword: boolean = false;
  /** Conservé hors localStorage (clear tokens ne l’efface pas). */
  private returnUrl: string | null = null;

  /** Logo selon thème (page login entièrement claire en mode light) */
  get logoSrc(): string {
    return brandLogoSrc(this.theme.theme());
  }

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.returnUrl = sanitizeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));

    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('currentUser');
      localStorage.removeItem('access_token');
      localStorage.removeItem('refresh_token');
      localStorage.removeItem('user');
      localStorage.removeItem('token_type');
    }

    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]]
    });

    this.authService.loading$.subscribe((loading: boolean) => {
      this.loading = loading;
    });

    // ✅ Supprimé — plus de redirection auto
  }

  onSubmit(): void {
    this.errorMessage = '';

    if (this.loginForm.invalid) {
      this.markFormGroupTouched(this.loginForm);
      return;
    }

    this.loading = true;
    const loginData = this.loginForm.value;

    this.authService.login(loginData).subscribe({
     next: (response: any) => {
  // ✅ Extraire userId depuis le token JWT
  const payload = JSON.parse(atob(response.accessToken.split('.')[1]));
  const nameIdentifier =
    payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
  const sub = payload['sub'];
  const userId =
    typeof sub === 'string' && sub.trim().length > 0
      ? sub.trim()
      : nameIdentifier != null && String(nameIdentifier).trim() !== ''
        ? String(nameIdentifier).trim()
        : '';

  // Stocker les tokens
  localStorage.setItem('access_token', response.accessToken);
  localStorage.setItem('refresh_token', response.refreshToken);
  localStorage.setItem('token_type', response.tokenType);

  // Stocker user AVEC authUserId (int Auth) + subjectId (GUID)
  const authUserIdRaw = nameIdentifier != null ? parseInt(String(nameIdentifier), 10) : NaN;
  const authUserId = Number.isFinite(authUserIdRaw) && authUserIdRaw > 0 ? authUserIdRaw : 0;

  localStorage.setItem('user', JSON.stringify({
    id: userId,
    authUserId,
    subjectId: userId,
    username: response.user?.username || payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'],
    email: response.user?.email || payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'],
    role: response.user?.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
  }));

  // Rediriger vers planning (encode : les refresh tokens Base64 peuvent contenir + / =)
  // theme= : ports 8201/8200 n’ont pas le même localStorage — le cookie + query conservent le mode choisi.
  const theme = this.theme.theme();
  const callback =
    `${KYNTUS_PUBLIC_URLS.planningAuthCallback}?token=${encodeURIComponent(response.accessToken)}&refresh=${encodeURIComponent(response.refreshToken)}&theme=${encodeURIComponent(theme)}`;
  window.location.href = this.returnUrl
    ? `${callback}&returnUrl=${encodeURIComponent(this.returnUrl)}`
    : callback;
},
      error: (error: any) => {
        console.error('Erreur de connexion', error);
        this.loading = false;

        if (error.status === 401) {
          this.errorMessage = 'Email ou mot de passe incorrect';
        } else if (error.status === 0) {
          this.errorMessage = 'Impossible de contacter le serveur';
        } else {
          this.errorMessage = error.error?.message || 'Une erreur est survenue';
        }
      }
    });
  }

  togglePasswordVisibility(): void {
    this.showPassword = !this.showPassword;
  }

  private markFormGroupTouched(formGroup: FormGroup): void {
    Object.keys(formGroup.controls).forEach(key => {
      const control = formGroup.get(key);
      control?.markAsTouched();
    });
  }

  hasError(fieldName: string, errorType: string): boolean {
    const field = this.loginForm.get(fieldName);
    return !!(field?.hasError(errorType) && field?.touched);
  }
}