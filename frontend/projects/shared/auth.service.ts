import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import Keycloak from 'keycloak-js';
import { from, switchMap } from 'rxjs';
import { env } from './env';

const DEV_TOKEN_KEY = 'clinicflow.dev-token';

interface Identity {
  email: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private keycloak?: Keycloak;
  private readonly identity = signal<Identity | null>(null);

  readonly isLoggedIn = computed(() => this.identity() !== null);
  readonly email = computed(() => this.identity()?.email ?? '');
  readonly isAdmin = computed(() => this.identity()?.roles.includes('admin') ?? false);
  readonly isStaff = computed(() => {
    const roles = this.identity()?.roles ?? [];
    return roles.includes('admin') || roles.includes('reception');
  });
  readonly usesDevLogin = env.auth.mode === 'dev';

  async init(): Promise<void> {
    if (env.auth.mode === 'dev') {
      const token = this.readDevToken();
      if (token) {
        this.identity.set(parseDevToken(token));
      }
      return;
    }

    this.keycloak = new Keycloak({ url: env.auth.url, realm: env.auth.realm, clientId: env.auth.clientId });
    await this.keycloak.init({ onLoad: 'check-sso', pkceMethod: 'S256', checkLoginIframe: false });

    const claims = this.keycloak.tokenParsed;
    if (this.keycloak.authenticated && claims) {
      this.identity.set({ email: String(claims['email'] ?? ''), roles: claims.realm_access?.roles ?? [] });
    }
  }

  /** In dev mode pass a token such as "dev:admin". With Keycloak this goes to the login page. */
  login(devToken?: string): void {
    if (this.keycloak) {
      void this.keycloak.login();
      return;
    }

    if (devToken) {
      localStorage.setItem(DEV_TOKEN_KEY, devToken);
      this.identity.set(parseDevToken(devToken));
    }
  }

  logout(): void {
    if (this.keycloak) {
      void this.keycloak.logout({ redirectUri: location.origin });
      return;
    }

    localStorage.removeItem(DEV_TOKEN_KEY);
    this.identity.set(null);
  }

  async getToken(): Promise<string | null> {
    if (!this.keycloak) {
      return this.readDevToken();
    }

    if (!this.keycloak.authenticated) {
      return null;
    }

    await this.keycloak.updateToken(30);
    return this.keycloak.token ?? null;
  }

  private readDevToken(): string | null {
    return localStorage.getItem(DEV_TOKEN_KEY);
  }
}

function parseDevToken(token: string): Identity {
  const [, role, email] = token.split(':');
  return { email: email ?? `${role}@clinicflow.test`, roles: [role] };
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(env.apiUrl)) {
    return next(request);
  }

  const auth = inject(AuthService);
  return from(auth.getToken()).pipe(
    switchMap(token =>
      next(token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request),
    ),
  );
};
