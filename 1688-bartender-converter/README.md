# 1688 → BarTender CSV Web Converter

This is a standalone browser tool. Source workbooks always remain local. Exported CSV files can optionally be sent to a configured final-file receiver.

## Open the tool

Double-click `open_converter.bat`, or open `index.html` in a modern browser.

## Features

- Reads the first worksheet in a 1688 `.xlsx` order export.
- Supports **Create blank template** without a 1688 order. The blank editor allows custom BID code, title, quantity, inventory, unit price, weight, and material, with add/delete row controls before downloading the same BarTender CSV format.
- Supports per-item and per-package pricing. 1688 imports default to `per-unit`; blank templates default to `per-package`. The pricing mode stays inside the editor, while `Total Cost` always represents one product/package and never multiplies by Inventory.
- Expands genuine Excel merged ranges into complete row-level records.
- Generates a fresh `BID|UUID` set for every conversion using the conversion timestamp plus cryptographic randomness. Repeated rows inside that conversion remain grouped under one code.
- Groups repeated source rows with the same `单品货号 + SKU ID` and counts them in `Inventory`.
- Adds `重量(g)`, `重量(oz)`, and `总成本`.
- Detects weights such as `7.8g` from product titles.
- Extracts common material keywords from product titles and allows corrections.
- Allows grams to be entered or corrected in the browser.
- Recalculates `重量(oz) = 重量(g) / 28.349523125` immediately.
- Recalculates `总成本 = 0.05 × 重量(g) + 单价(元) × 数量` immediately.
- Exports UTF-8 BOM CSV for BarTender and Chinese Windows compatibility.
- Names every exported file using the local generation time, for example `20260910_143025_BarTender.csv`.
- Asks whether an export is a test or final version. Test exports only download locally; final exports upload the exact CSV blob to the configured receiver and then download the same file locally.

The exported CSV includes `UUID`, `Title`, `Quantity`, `Inventory`, `Unit Price`, `Weight (g)`, `Weight (oz)`, `Material`, `Total Cost`, `Whatnot Auction Safe Price`, `Item Code`, and `Expected Inventory`. `Item Code` is derived from the calculated safe price (`$1 = A`, `$2 = B`, ... `$26 = Z`, `$27 = AA`). Decimal fields are exported with two decimal places. `Inventory` is actual/received stock; `Expected Inventory` is the imported or ordered stock. For shipments prepared in China, the converted quantity populates `Expected Inventory`, while `Inventory` starts at zero until the US warehouse physically receives and counts the goods.

The Whatnot safe start uses the same break-even model as Local Product Manager: RMB cost converted at the safe fallback rate (0.15), 10% cost buffer, $0.25 packaging reserve, 8% commission, 2.9% processing, 1% fee-tax reserve, $0.30 fixed processing fee, and processing reserve on an assumed $10 buyer shipping/tax amount. The result rounds upward to a whole-dollar starting price and exports with two decimals.

The included SheetJS Community Edition browser build is pinned to version 0.20.3 under `vendor/` so the converter works offline.

## Optional final-file upload

The receiver endpoint is configured in `upload-config.js`. If `authorizationToken` is blank, the converter asks for the token on the first final upload. After the server validates it, the token is encrypted with AES-GCM; only ciphertext is stored in local storage and the non-exportable encryption key is kept in IndexedDB. On later exports the saved token is used silently and the token input stays hidden. A rejected or expired token is cleared automatically and the input is shown again. The converter sends a `POST` request using `multipart/form-data` with:

- `file`: the generated CSV file
- `sourceName`: the original workbook name without `.xlsx`
- `uploadedAt`: the browser's ISO timestamp

The receiver should return any HTTP `2xx` response after it has safely stored the file. If the endpoint needs a simple bearer token, set `authorizationToken`; remember that any token stored in browser JavaScript is visible to users of the page and should only have permission to upload into this one inbox.

If the converter is opened through `file://`, the receiver must explicitly allow that browser request or the converter should be hosted over HTTPS alongside the receiver.

## Data handling

All workbook processing is performed in browser memory. Selecting a workbook does not change or upload the source file. Only a CSV explicitly confirmed as final is sent to the configured receiver.
