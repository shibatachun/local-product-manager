# Local Product Manager

[![English](https://img.shields.io/badge/Language-English-2563eb)](README.md)
[![简体中文](https://img.shields.io/badge/语言-简体中文-dc2626)](README.zh-CN.md)

**English** | [简体中文](README.zh-CN.md)

Local Product Manager is an internally developed system for product, warehouse, live-commerce, and marketing operations. It brings product data, inventory locations, scanning, BID batches, label printing, platform pricing, and marketing data into one LAN application, reducing manual spreadsheets, duplicate entry, and inventory inconsistencies.

Current version: `0.1.6`

Production URL:

```text
https://inventory.jjgqaz.com/
```

Compatibility URL:

```text
https://inventory.jjgqaz.com:7265/
```

The system is intended for the company network only and must not be exposed directly to the public internet.

## Features

### Products and Warehouses

- Product, parent/child SKU, INV Number, category number, and extended-data management
- Product cost, weight, dimensions, platform links, and image-folder management
- Warehouse, shelf, level, Bin, and physical location inventory management
- Stock In, Stock Out, inventory transfers, and operation history
- Handheld scanner and mobile-camera support for LPM QR codes
- Bulk Excel/CSV import, export, and data maintenance

### Label Maker and BarTender

- Built-in browser label design and printing
- Product labels, location labels, single/double-column rolls, and custom presets
- Administrator-configurable third-party label-printing HTTP endpoint
- BarTender 2022 Integration Builder integration
- Direct BID Product printing from the website using a UUID and copy count
- BarTender queries the matching PostgreSQL View record and prints a `.btw` template
- Products already labeled overseas through an external CSV are excluded from local BarTender printing

BarTender data View:

```text
bartender_bid_products
```

For compatibility with existing templates, the `Inventory` heading represents physical inventory. The originally expected quantity is exposed as `Expected Inventory`.

BarTender Integration example:

```text
POST http://localhost:5160/Integration/lpm-label-print/Execute
```

Main request variables:

```json
{
  "action": "print",
  "recordId": "BID|B000001|P010",
  "copies": 1
}
```

Configure the actual endpoint, template, and printer under Admin → Third-party label printing in production. `localhost` means the server running BarTender Integration Service; use its LAN IP when it runs on another host.

### Live Stream BID Products

- BID batches as top-level entries, with expandable product creation, editing, and deletion
- Import from 1688/external BarTender CSV files or create local batches in the application
- Stable QR identifiers based on batch and product sequence, such as `BID|B000001|P010`
- Actual SKU, pack quantity, cost, weight, material, and Whatnot safe starting price
- Batch-level and product-level BarTender CSV export
- Direct BarTender label printing for the selected product
- Scan-based live-sale confirmation with sale price, exchange rate, cost, and profit tracking

BID inventory has two values:

- `Expected inventory`: quantity expected from the batch or source CSV
- `Actual inventory`: quantity physically received and counted at the warehouse

Newly imported external CSV products start with actual inventory `0`. Locally created products or products transferred from warehouse inventory initialize it from the current quantity. BID scanner mode can increase or decrease actual inventory. Confirming a sale requires actual inventory above zero and reduces both inventory values.

### Whatnot Safe Starting Price

The safety-price model includes:

- Product cost in CNY and a live or fallback CNY/USD exchange rate
- 10% product-cost safety buffer
- `$0.25` packaging allowance
- Host labor: `$25 ÷ 60 packages = $0.4167/package`
- 8% platform commission
- 2.9% payment-processing fee
- 1% fee-tax allowance
- `$0.30` fixed processing fee
- Processing fees caused by assumed buyer shipping and taxes

Labor and packaging are added to break-even cost before reversing the platform retention rate. The result is rounded up to a whole US dollar. The information button beside the BID heading shows the full calculation. This is a break-even safety estimate, not a guaranteed sale price or profit.

### Converter and Auto Listing

- `1688-bartender-converter/` can create and edit BarTender data without an existing 1688 order
- Supports per-package price/per-unit price, cost, weight, inventory, and Whatnot safety price
- CSV headings remain compatible with the BarTender template; numeric values use two decimal places
- Auto Listing reserves channels for eBay, Walmart, and Etsy
- The current eBay workflow focuses on manually reviewed drafts and can generate Markdown, Seller Hub CSV, and image assets; Walmart and Etsy production APIs are not connected yet

### Marketing, Analytics, and Administration

- Amazon ASIN, Attribution link, and marketing-product management
- YouTube, Meta, Pinterest, Instagram, Facebook, and TikTok data connections/imports
- Social metrics, video trends, and AI analysis background tasks
- Platform pricing estimates, unit conversion, file storage, and 3D product-scene editing
- Administrator, user, session, and granular permissions
- Social-data import, export, and maintenance
- PWA, Windows Tauri desktop shell, and signed automatic updates

## Architecture

```text
Angular 22 / PWA / Tauri
          │
          ▼
ASP.NET Core HTTPS Gateway
          │
          ▼
ASP.NET Core API (.NET 10)
          │
          ├── PostgreSQL (production)
          ├── PostgreSQL (the only runtime database)
          ├── BarTender Integration Builder
          └── Image folders, marketing platforms, and background services
```

Production services are orchestrated by the .NET Aspire AppHost:

- `inventory-api`: internal API, port `5077` by default
- `inventory-https-gateway`: Angular static files, HTTPS, and API reverse proxy
- PostgreSQL: production business data
- Aspire Dashboard: resource status and logs

## Repository Structure

```text
LocalProductManager.Api/               ASP.NET Core API, business logic, and schema upgrades
LocalProductManager.Api.Tests/         API and inventory workflow tests
LocalProductManager.AppHost/           .NET Aspire production orchestration
LocalProductManager.Gateway/           HTTPS, frontend static files, and API reverse proxy
LocalProductManager.DatabaseMigration/ Database migration helper
LocalProductManager.Web/               Angular, PWA, and Tauri desktop client
1688-bartender-converter/               Standalone BarTender CSV conversion tool
config/deployment.json                  Local deployment config (never commit real passwords)
tools/                                  Aspire, backup, certificate, and installer scripts
updates/                                Desktop automatic-update files
```

## Local Development

Requirements:

- Windows 10/11 or Windows Server
- .NET 10 SDK
- Node.js and npm 11
- PostgreSQL when developing against the production-style stack
- Rust/Cargo and WebView2 only when building the Tauri installer
- BarTender 2022 Automation/Enterprise only when testing one-click label printing

Install frontend dependencies:

```powershell
cd LocalProductManager.Web
npm ci
```

Start the Angular development server:

```powershell
npm start
```

Default URL:

```text
http://localhost:4200/
```

Start the API separately:

```powershell
dotnet run --project LocalProductManager.Api --launch-profile http
```

Default URL:

```text
http://localhost:5077/
```

Start the complete environment with Aspire:

```text
start_aspire.bat
```

You can also use:

```text
manage_aspire.bat
```

## Deployment Configuration

Copy and complete:

```text
config/deployment.example.json → config/deployment.json
```

The configuration includes:

- Static LAN IP
- HTTPS, API, and Dashboard ports
- PFX path and password
- PostgreSQL host, database, username, and password
- Automatic-update path

Never commit real database passwords, OAuth secrets, API keys, PFX passwords, or private keys.

Before a frontend build, `scripts/sync-deployment.mjs` automatically copies deployment addresses that are safe to expose into the Angular configuration. Database credentials are never copied into the frontend.

## Build and Verification

Frontend:

```powershell
cd LocalProductManager.Web
npm run build
```

API:

```powershell
dotnet build LocalProductManager.Api/LocalProductManager.Api.csproj
```

Tests:

```powershell
dotnet test LocalProductManager.Api.Tests/LocalProductManager.Api.Tests.csproj
```

Current Angular output directory:

```text
LocalProductManager.Web/dist/LocalProductManager.Web/browser
```

## Database Upgrades and Backups

At startup, the API automatically creates missing PostgreSQL table columns, indexes, and the BarTender View. Legacy SQLite migration code is archived under `deprecated/sqlite` and is not part of the API runtime.

Install daily PostgreSQL backups:

```text
install_postgres_daily_backup.bat
```

Manual utility:

```text
manage_postgres.bat
```

Backups should include at least:

- PostgreSQL database
- Product images and shared-file directories
- A secured copy of `config/deployment.json`
- HTTPS PFX and certificate-renewal configuration
- Tauri updater private key
- BarTender `.btw` templates and `.btin` Integration files

## BarTender Requirements

One-click printing from the website requires:

- BarTender Integration Service
- BarTender Print Scheduler Service
- A deployed and Running Web Service Integration
- Integration Service access to the `.btw` template, PostgreSQL, and target printer
- Firewall access to the configured Integration port (currently `5160` in the example)

The template receives Integration Variable `recordId` through Query Prompt `BID_UUID`; Copies uses variable `copies`. A test request with `action=test` does not print. Only `action=print` executes Print Document.

## Windows Desktop App and Updates

Build the installer:

```text
build_installer.bat
```

Installer and update metadata are published to `updates/`. Changes limited to Angular pages or the API usually do not require rebuilding the Tauri shell. Changes to Rust, window permissions, icons, or the updater require a new installer.

Versions follow semantic versioning:

- Patch: compatible bug fixes
- Minor: compatible new features
- Major: significant or incompatible changes

## Troubleshooting

### The Old UI Is Still Visible After an Update

- Run `npm run build` again
- Confirm that the Gateway points to the correct Angular `dist` directory
- Restart Aspire resources
- Force-refresh or clear the site/PWA Service Worker cache

### BarTender Test Connection Succeeds but Nothing Prints

- Confirm that the Integration is deployed and Running
- Confirm that Print Document requires `action = print`
- Confirm Query Prompt `BID_UUID = recordId`
- Confirm that `recordId` and `copies` are declared as Integration Variables and overwritten by JSON Variables
- Inspect the actual values under Messages, Action Status, and Variables in Integration Builder

### BarTender Reports “No Records Found”

- Confirm the UUID exists in `bartender_bid_products`
- The UUID must include the `BID|` prefix
- Confirm that the template Query Prompt is named `BID_UUID`
- External CSV products are excluded from the local label View by default

### Mobile Scanner Cannot Start the Camera

- Use HTTPS
- Check browser camera permissions
- Prefer Safari/PWA on iPhone
- Confirm that no other application is using the camera

### View Upgrade Fails During API Startup

PostgreSQL `CREATE OR REPLACE VIEW` requires existing column names and order to remain unchanged. Append new columns to the end of the View, or explicitly drop and recreate it during a maintenance window.

## Security Principles

- Use only on the company LAN or a controlled VPN
- Assign individual employee accounts and apply least privilege
- Never store credentials in code, screenshots, chat, logs, or commits
- Verify related data before deleting warehouse structures or inventory
- Regularly test recovery of databases, images, certificates, and updater private keys
- Allow third-party printing endpoints only for trusted internal services
