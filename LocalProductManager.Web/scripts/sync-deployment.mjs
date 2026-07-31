import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(scriptDirectory, '..');
const repositoryRoot = resolve(webRoot, '..');
const deploymentPath = resolve(repositoryRoot, 'config', 'deployment.json');
const deployment = JSON.parse(await readFile(deploymentPath, 'utf8'));
const httpsPort = Number(deployment.ports.https);
const inventoryBaseUrl = `https://${deployment.lanHost}${httpsPort === 443 ? '' : `:${httpsPort}`}`;

const projectDocumentSource = resolve(repositoryRoot, 'PROJECT_DOCUMENTATION.md');
const projectDocumentDestination = resolve(webRoot, 'public', 'PROJECT_DOCUMENTATION.md');
await mkdir(dirname(projectDocumentDestination), { recursive: true });
await copyFile(projectDocumentSource, projectDocumentDestination);

const tauriConfigPath = resolve(webRoot, 'src-tauri', 'tauri.conf.json');
const tauriConfig = JSON.parse(await readFile(tauriConfigPath, 'utf8'));
tauriConfig.app.windows[0].url = inventoryBaseUrl;
tauriConfig.plugins.updater.endpoints = [
  `${inventoryBaseUrl}${deployment.updates.path}/latest.json`,
];
await writeFile(tauriConfigPath, `${JSON.stringify(tauriConfig, null, 2)}\n`, 'utf8');

const capabilityPath = resolve(webRoot, 'src-tauri', 'capabilities', 'default.json');
const capability = JSON.parse(await readFile(capabilityPath, 'utf8'));
capability.remote.urls = [`${inventoryBaseUrl}/*`];
await writeFile(capabilityPath, `${JSON.stringify(capability, null, 2)}\n`, 'utf8');

const generatedConfigPath = resolve(webRoot, 'src', 'app', 'core', 'deployment.generated.ts');
const browserDeployment = {
  lanHost: deployment.lanHost,
  ports: {
    https: deployment.ports.https,
    api: deployment.ports.api,
    frontendDevelopment: deployment.ports.frontendDevelopment,
  },
  updates: {
    path: deployment.updates.path,
    installerFileName: deployment.updates.installerFileName,
  },
};
const generatedConfig = `// Generated from config/deployment.json by scripts/sync-deployment.mjs.\n` +
  `// Edit the JSON source, then run npm start or npm run build.\n` +
  `export const deploymentConfig = ${JSON.stringify(browserDeployment, null, 2)} as const;\n`;
await writeFile(generatedConfigPath, generatedConfig, 'utf8');

console.log(`Synced deployment configuration from: ${deploymentPath}`);
