export const environment = {
  production: false,
  // Relative path so browser requests hit the Angular dev-server (localhost:4200)
  // and are proxied to the backend (see proxy.conf.json). This keeps API calls
  // same-origin, so the HttpOnly refresh_token cookie is stored and sent back.
  apiUrl: '/api',
  keycloakUrl: 'http://localhost:8081',
  clientId: 'api-engine-app'
};
