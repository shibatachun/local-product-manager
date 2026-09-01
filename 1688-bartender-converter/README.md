# 1688 → BarTender CSV Web Converter

This is a standalone browser tool. It does not upload source workbooks or converted data.

## Open the tool

Double-click `open_converter.bat`, or open `index.html` in a modern browser.

## Features

- Reads the first worksheet in a 1688 `.xlsx` order export.
- Supports **Create blank template** without a 1688 order. The blank editor allows custom BID code, title, quantity, inventory, unit price, weight, and material, with add/delete row controls before downloading the same BarTender CSV format.
- Supports per-item and per-package pricing. 1688 imports default to `per-unit`; blank templates default to `per-package`. The pricing mode stays inside the editor, while `Total Cost` always represents one product/package and never multiplies by Inventory.
- Expands genuine Excel merged ranges into complete row-level records.
- Uses `BID|单品货号|SKU ID` in the first `UUID` column for QR codes and scanner routing.
- Groups repeated source rows with the same `单品货号 + SKU ID` and counts them in `Inventory`.
- Falls back to a stable `BID|UUID` only when either source identifier is missing.
- Adds `重量(g)`, `重量(oz)`, and `总成本`.
- Detects weights such as `7.8g` from product titles.
- Extracts common material keywords from product titles and allows corrections.
- Allows grams to be entered or corrected in the browser.
- Recalculates `重量(oz) = 重量(g) / 28.349523125` immediately.
- Recalculates `总成本 = 0.05 × 重量(g) + 单价(元) × 数量` immediately.
- Exports UTF-8 BOM CSV for BarTender and Chinese Windows compatibility.

The exported CSV includes `UUID`, `Title`, `Quantity`, `Inventory`, `Unit Price`, `Weight (g)`, `Weight (oz)`, `Material`, `Total Cost`, `Whatnot Auction Safe Price`, `Item Code`, and `Expected Inventory`. `Item Code` is derived from the calculated safe price (`$1 = A`, `$2 = B`, ... `$26 = Z`, `$27 = AA`). Decimal fields are exported with two decimal places. `Inventory` is actual/received stock; `Expected Inventory` is the imported or ordered stock. For shipments prepared in China, the converted quantity populates `Expected Inventory`, while `Inventory` starts at zero until the US warehouse physically receives and counts the goods.

The Whatnot safe start uses the same break-even model as Local Product Manager: RMB cost converted at the safe fallback rate (0.15), 10% cost buffer, $0.25 packaging reserve, 8% commission, 2.9% processing, 1% fee-tax reserve, $0.30 fixed processing fee, and processing reserve on an assumed $10 buyer shipping/tax amount. The result rounds upward to a whole-dollar starting price and exports with two decimals.

The included SheetJS Community Edition browser build is pinned to version 0.20.3 under `vendor/` so the converter works offline.

## Data handling

All processing is performed in browser memory. Selecting a workbook does not change the source file, and no data is sent to the inventory API or any external service.
