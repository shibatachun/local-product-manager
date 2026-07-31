// Generated from config/deployment.json by scripts/sync-deployment.mjs.
// Edit the JSON source, then run npm start or npm run build.
export const deploymentConfig = {
  "lanHost": "192.168.1.154",
  "ports": {
    "https": 443,
    "api": 5077,
    "frontendDevelopment": 4200
  },
  "updates": {
    "path": "/updates",
    "installerFileName": "LocalProductManager-setup.exe"
  }
} as const;
