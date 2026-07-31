import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(scriptDirectory, '..');
const deploymentPath = resolve(webRoot, '..', 'config', 'deployment.json');
const deployment = JSON.parse(await readFile(deploymentPath, 'utf8'));
const executable = process.platform === 'win32' ? 'npx.cmd' : 'npx';
const child = spawn(
  executable,
  ['ng', 'serve', '--host', '0.0.0.0', '--port', String(deployment.ports.frontendDevelopment)],
  { cwd: webRoot, stdio: 'inherit' },
);

child.on('exit', (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  else process.exit(code ?? 1);
});
