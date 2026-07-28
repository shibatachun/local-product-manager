export function apiBaseUrl(): string {
  const location = globalThis.location;
  if (!location) return 'http://localhost:5077/api';

  // Angular's development server runs on 4200; the API remains on 5077 there.
  // In built/LAN deployments the .NET host serves both the SPA and API, so use
  // the current HTTPS origin and avoid mixed-content requests.
  return location.port === '4200'
    ? `http://${location.hostname}:5077/api`
    : `${location.origin}/api`;
}
