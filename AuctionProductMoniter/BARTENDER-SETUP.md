# Auction Product Monitor BarTender Setup

这套 Integration 仅供 `AuctionProductMonitor.Wpf` 使用，不修改 Local Product Manager 已有的 BID 和 Inventory Integration。

## 文件

- Integration：`AuctionProductMonitor-BarTender.btin`
- CSV 记录集样例：`auction-bartender-print-sample.csv`
- 标签模板：`bidproductlabel.btw`
- HTTP 端点：`http://127.0.0.1:5164/Integration/auction-product-label/Execute`

## 1. 把 BTW 模板连接到记录集

为让双列纸连续填满左右两列，模板必须使用一个文本数据库连接，而不是 Embedded Data Named Data Sources。

1. 用 BarTender Designer 打开 `bidproductlabel.btw`；
2. 打开“数据库连接设置”，添加“文本文件”连接，选择同目录的 `auction-bartender-print-sample.csv`；
3. 选择逗号分隔，并勾选“第一行包含字段名”；
4. 把模板上的三个文本对象分别绑定到数据库字段 `BuyerName`、`ProductNumber`、`SessionId`；`action` 不需要放到标签上；
5. 保存模板。数据库连接设置里应能看到该文本文件连接。

建议标签正面突出显示 `ProductNumber`，下一行显示 `BuyerName`，并显示或保留 `SessionId`。这里沿用字段名 `ProductNumber` 以兼容现有 `.btw` 模板，但字段值现在优先是 Whatnot 的完整商品名（通常包含末尾 `#编号`），而非只有编号。商品名较长时请在 BarTender 中检查自动换行和字号。

## 2. 导入和部署 Integration

1. 用 BarTender Integration Builder 打开 `AuctionProductMonitor-BarTender.btin`；
2. 打开 `Print Auction Product Label Batch` action；
3. 确认 Document 指向上面的 `.btw`；
4. 确认 Record Sets 使用 `%EventData%`，不要配置逐条 For Each；
5. 选择实际标签打印机；
6. 部署 `Production` configuration，并确认 Integration 处于 Running 状态。

## 3. BarTender CSV 协议

```csv
action,BuyerName,ProductNumber,SessionId
print,"silentgems","Blue beads #33","LIVE-20260825-160000"
print,"second_buyer","Red charms #33","LIVE-20260825-160000"
```

- 每次 HTTP 请求的所有 CSV 行由同一个 Print action 处理，因此是一项打印作业，双列纸会按模板的页面顺序填充左右两列；
- `action` 列现在只记录正式打印行；Print action 不再用 `%action%` 作条件，因为 CSV 输入不会更新旧版 JSON 变量；
- 数量大于 1 时，中转器会在同一 CSV 中重复该标签行，不会拆成多个打印任务；
- Show Report 以 `order_id` 去重，不再因不同商品复用同一个 `#编号` 而拒绝导入；旧数据仍保留编号，导入记录可从保存的原始商品名打印；没有可靠商品名的旧直播记录则继续打印编号；
- 中转器限制每个 bulk 作业最多 1000 张标签。

## 4. WPF 操作

- `测试连接`：中转器检查 BarTender HTTP 端口是否可连接，不提交打印作业；
- `打印选中成交`：支持 Ctrl/Shift 多选；选中一行时发送单记录作业，多行时按列表顺序提交一个批量作业。已提交的行也可补打；
- `全部打印未打印项`：把全部未打印订单合成一个 `/api/print/bulk` 请求；中转器再以一份多行 CSV 提交给 BarTender；
- `收到新成交后自动打印`：仍以单记录作业自动打印；
- Show Report 导入按 Whatnot `order_id` 去重并排除失败/取消订单。中转器接受后的状态会保存在本地 Session；状态不是打印机物理出签的证明。
