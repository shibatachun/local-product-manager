# 1688 → BarTender CSV Web Converter

This is a standalone browser tool. It does not upload source workbooks or converted data.

## Open the tool

Double-click `open_converter.bat`, or open `index.html` in a modern browser.

## Features

- Reads the first worksheet in a 1688 `.xlsx` order export.
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

The exported CSV contains only these English headings: `UUID`, `Title`, `Quantity`, `Unit Price`, `Weight (g)`, `Weight (oz)`, `Material`, and `Total Cost`.

The included SheetJS Community Edition browser build is pinned to version 0.20.3 under `vendor/` so the converter works offline.

## Data handling

All processing is performed in browser memory. Selecting a workbook does not change the source file, and no data is sent to the inventory API or any external service.
