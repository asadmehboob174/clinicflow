import { AppEnv } from './models';

/** Default: run the API with `dotnet run`. Sign-in uses local dev tokens, no Keycloak needed. */
export const env: AppEnv = {
  apiUrl: 'http://localhost:5080',
  auth: { mode: 'dev' },
};
