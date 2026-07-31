import { deploymentConfig } from './deployment.generated';

export function apiBaseUrl(): string {
  const location = globalThis.location;
  if (!location) return `http://localhost:${deploymentConfig.ports.api}/api`;

  // Angular's development server talks directly to the API on 5077.
  // The HTTPS website gateway proxies /api on the production domain.
  // In built/LAN deployments the .NET host serves both the SPA and API, so use
  // the current HTTPS origin and avoid mixed-content requests.
  return location.port === String(deploymentConfig.ports.frontendDevelopment)
    ? `http://${location.hostname}:${deploymentConfig.ports.api}/api`
    : `${location.origin}/api`;
}
