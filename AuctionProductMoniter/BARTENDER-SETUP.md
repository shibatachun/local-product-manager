# Auction Product Monitor BarTender Setup

这套 Integration 仅供 `AuctionProductMonitor.Wpf` 使用，不修改 Local Product Manager 已有的 BID 和 Inventory Integration。

## 文件

- Integration：`AuctionProductMonitor-BarTender.btin`
- JSON样例：`auction-bartender-print-sample.json`
- 标签模板路径：`bidproductlabel.btw`
- HTTP端点：`http://127.0.0.1:5164/Integration/auction-product-label/Execute`

## 1. 创建标签模板

使用 BarTender Designer 在当前目录创建：

```text
C:\Users\jjgqa\local-product-manager\AuctionProductMoniter\bidproductlabel.btw
```

当前模板包含三个区分大小写的 Embedded Data Named Data Sources：

```text
BuyerName
ProductNumber
SessionId
```

建议标签正面突出显示 `ProductNumber`，下一行显示 `BuyerName`，并显示或保留 `SessionId`。`DetectedAt`和`SessionDate`仍包含在WPF JSON及本地Session记录中，但当前BTW不打印这两个字段。

## 2. 导入和部署 Integration

1. 用 BarTender Integration Builder 打开 `AuctionProductMonitor-BarTender.btin`；
2. 打开 `Print Auction Product Label` action；
3. 确认Document指向上面的 `.btw`；
4. 在 Named Data Sources 页面确认映射：
   - `BuyerName` = `%buyerName%`
   - `ProductNumber` = `%productNumber%`
   - `SessionId` = `%sessionId%`
5. 选择实际标签打印机；
6. 部署 `Production` configuration，并确认Integration处于Running状态。

## 3. JSON协议

```json
{
  "action": "print",
  "source": "AuctionProductMonitor.Wpf",
  "sessionId": "LIVE-20260825-160000",
  "sessionDate": "2026-08-25",
  "buyerName": "silentgems",
  "productNumber": "33",
  "copies": 1,
  "detectedAt": "2026-08-24T16:07:37-04:00",
  "requestedAt": "2026-08-24T20:07:38Z"
}
```

- `action: "test"`：Integration返回响应，但Print action条件不成立，不打印；
- `action: "print"`：正式打印；
- `copies`：打印份数，WPF当前固定为1。

## 4. WPF操作

- `测试连接`：发送 `action=test`；
- `打印选中成交`：对列表选中行发送 `action=print`；
- `收到新成交后自动打印`：默认关闭，启用后每条首次接收的成交会自动请求BarTender；
- WPF按“买家名称 + 商品编号”去重，手动同步不会重复打印已经接收的同一行；但手动点击“打印选中成交”允许重新打印。
