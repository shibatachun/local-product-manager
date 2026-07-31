import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(scriptDirectory, '..');
const source = resolve(webRoot, '..', 'PROJECT_DOCUMENTATION.md');
const destination = resolve(webRoot, 'public', 'PROJECT_DOCUMENTATION.md');

await mkdir(dirname(destination), { recursive: true });
await copyFile(source, destination);
console.log(`Synced project documentation: ${destination}`);
