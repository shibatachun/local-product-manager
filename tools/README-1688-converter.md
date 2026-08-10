# 1688 Order to BarTender CSV Converter

Run `convert_1688_order.bat` from the repository root.

## Workflow

1. Select the original `.xlsx` workbook exported by 1688.
2. Review the flattened order rows in the grid.
3. Enter or correct `重量(g)` where necessary.
4. The tool automatically recalculates:
   - `重量(oz) = 重量(g) / 28.349523125`
   - `总成本 = 0.05 × 重量(g) + 单价(元) × 数量`
5. Select **Export CSV**.

The exported CSV uses UTF-8 with BOM for Chinese text compatibility. The source workbook is opened read-only and is never modified.

## Generated fields

- `Unicode`: Stable UUID generated from the order ID, SKU ID, item SKU, and source row. It can be used as the QR code value.
- `重量(g)`: Automatically detected when the product title contains a value such as `7.8g`; otherwise it can be entered manually.
- `重量(oz)`: Recalculated whenever grams, unit price, or quantity is edited.
- `总成本`: Recalculated whenever grams, unit price, or quantity is edited.

## Command-line conversion

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\convert-1688-order.ps1 `
  -InputPath "C:\Orders\1688-order.xlsx" `
  -OutputPath "C:\Orders\1688-order_BarTender.csv" `
  -NoGui
```

When `-OutputPath` is omitted, the CSV is created beside the source workbook.
