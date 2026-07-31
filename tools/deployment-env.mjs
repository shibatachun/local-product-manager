import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const deployment = JSON.parse(readFileSync(resolve('config', 'deployment.json'), 'utf8'));
const httpsPort = Number(deployment.ports.https);
const website = `https://${deployment.lanHost}${httpsPort === 443 ? '' : `:${httpsPort}`}`;
const values = {
  INVENTORY_URL: website,
  DASHBOARD_PORT: deployment.ports.dashboard,
  DASHBOARD_OTLP_PORT: deployment.ports.dashboardOtlp,
  RESOURCE_SERVICE_PORT: deployment.ports.resourceService,
  DB_HOST: deployment.database.host,
  DB_PORT: deployment.database.port,
  DB_NAME: deployment.database.name,
  DB_USERNAME: deployment.database.username,
  DB_PASSWORD: deployment.database.password,
};
for (const [key, value] of Object.entries(values)) console.log(`${key}=${value}`);
