export function apiBaseUrl(): string {
  const location = globalThis.location;
  if (!location) return 'http://localhost:5077/api';

  // Angular's development server talks directly to the API on 5077.
  // The HTTPS website gateway proxies /api on the production domain.
  // In built/LAN deployments the .NET host serves both the SPA and API, so use
  // the current HTTPS origin and avoid mixed-content requests.
  return location.port === '4200'
    ? `http://${location.hostname}:5077/api`
    : `${location.origin}/api`;
}
