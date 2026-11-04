import { AppEnv } from './models';

/** Used by `--configuration docker`, when the full stack runs in Docker Compose. */
export const env: AppEnv = {
  apiUrl: 'http://localhost:5080',
  auth: { mode: 'keycloak', url: 'http://localhost:8080', realm: 'clinicflow', clientId: 'clinicflow-web' },
};
