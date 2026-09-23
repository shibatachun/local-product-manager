# Auction Product Monitor

用于 Whatnot 直播拍卖期间自动记录成交证据、买家名称和产品编号，避免人工贴号漏号，并让直播后的清点和发货能够按编号完成。

## 直播结束后导入 Show Report 并批量打印

现在可以不启动直播 Session，也不依赖浏览器扩展的实时成交检测。直播结束后，从 Whatnot 导出该场 show 的 CSV，在 Monitor 点击 **导入 Show 报表 CSV**。程序创建独立的 `SHOW-日期-校验码` 历史列表，显示买家、商品手写编号、数量、包裹 ID 和打印提交状态。

- 只导入 `cancelled_or_failed` 为空的有效订单；失败/取消行不打印。示例文件中失败的 `New Arrivals #4` 被跳过，成功的 #4 保留。
- 有末尾 `#数字` 手写编号的已付款订单进入打印列表，同一场的普通拍品编号不能重复。`Free Gift #数字` 使用独立编号空间，可与普通拍品复用数字，并把完整的 `Free Gift #数字` 发送给标签打印。没有编号的已付款订单会在确认框逐笔列出，并明确提示不会导入或打印，需单独处理；用户确认后才导入有编号的订单。订单 ID、买家或数量无效时仍停止整份导入，不保存部分行。
- 相同文件再导入会打开已有列表；不同文件如果含有已经导入的 `order_id`，会阻止重复导入。更新过的报表需要先核对差异，不会静默覆盖旧标签状态。
- 导入后点 **全部打印未打印项**，程序会把所有未打印订单按 CSV 顺序合成一个 bulk 请求；中转器再以一个多记录 BarTender 作业打印，让双列标签纸连续填满左右两列。BarTender 接受整批后统一保存打印状态；失败时整批保持未打印，便于重试。成交列表支持 Ctrl/Shift 多选，**打印选中成交**会将多行合并为一个作业，也允许单张人工补打或重打已提交的行。
- 一行 `product_quantity` 会请求相应份数，当前支持每行 1–20 张。界面“已提交”表示中转器接受打印请求，仍需核对打印机实际吐出的标签。
- 标签继续使用现有 BarTender 模板的 `BuyerName`、`ProductNumber`、`SessionId`，是商品内部标签；Whatnot 物流面单另行处理。CSV 内的收货地址不导入本地 Session 数据。

样本 `live-a84387dc-76fc-467c-9d95-e1e712935c9b.csv` 解析结果为 14 笔有效订单、1 笔 failed、14 张标签、3 个包裹。

> 目录名按当前要求保留为 `AuctionProductMoniter`。界面产品名建议显示为 **Auction Product Monitor**。

## 当前可运行原型（Chrome 扩展 + WPF）

当前目录已经包含一个最小端到端原型：

- `chrome-extension/`：监听 Whatnot 页面新增的成交文字，提取买家名和当前商品编号，并发送到本机；
- `AuctionProductMonitor.Wpf/`：监听 `http://127.0.0.1:17891`，接收后在列表中显示时间、买家名称和商品编号；
- 扩展弹窗只显示Whatnot Activity记录。

### 启动 WPF 接收程序

在项目根目录运行：

```powershell
dotnet run --project AuctionProductMoniter\AuctionProductMonitor.Wpf\AuctionProductMonitor.Wpf.csproj
```

看到绿色状态“接收器运行中”后保持程序开启。

### 在 Chrome 加载扩展

1. 打开 `chrome://extensions`；
2. 打开右上角“开发者模式”；
3. 点击“加载已解压的扩展程序”；
4. 选择 `AuctionProductMoniter\chrome-extension`；
5. 打开Whatnot直播页面并保持Activity标签选中；
6. 新成交出现后，WPF列表应立即出现该记录。

扩展弹窗自动保存最近30条Activity读取结果。点击“扫描当前Activity”可以重新读取当前列表，并显示买家名称、商品编号和原始成交文字。

安装新版本或修改扩展代码后，需要在 `chrome://extensions` 中点击该扩展的“重新加载”，并刷新已经打开的 Whatnot 页面。

如果扩展错误页出现 `Extension context invalidated`，表示扩展重新加载后旧页面仍运行着旧版 content script。刷新 Whatnot 直播页面即可。0.6.0 起旧监听器也会在发现上下文失效后自动停止。

0.6.1 起 Activity 区域只使用成交文字和商品标题的精确 `title` 属性，不再运行通用买家字段回退；`Returning Buyer`、`New Buyer` 等页面徽章不会被当成买家名称发送。

0.7.0 起扩展弹窗移除全页面DOM诊断，只保存和显示 `seller-live-activity-list` 内的Activity记录。

0.7.1 修复批量扫描Activity时异步存储互相覆盖的问题。后台改为顺序写入，按“买家+商品编号”去重，最多保存200条并在弹窗显示最近100条。

0.7.2 修复Whatnot整体替换Activity列表节点时，最新成交被误当成初始化历史记录而没有发送到WPF的问题。现在只有第一次连接列表时忽略历史，后续列表替换仍会发送未见过的成交。

0.8.0 增加“手动同步到WPF”按钮，按Activity从旧到新发送当前列表中的全部有效记录。WPF按“买家+商品编号”去重，可反复同步并只补充缺失记录。

0.8.1 让打开着的扩展弹窗随Activity存储变化实时刷新，并新增WPF专用BarTender Integration、JSON协议、测试连接、打印选中成交和可选自动打印。具体部署见 `BARTENDER-SETUP.md`。

WPF现支持直播Session：点击“开始直播”创建带日期和时间的Session，只有活动Session接收成交；点击“结束直播”后写入历史Session列表。Session和成交实时保存到 `%LocalAppData%\AuctionProductMonitor\monitor-sessions.json`，程序重启会恢复未结束直播和历史记录。BarTender JSON同步包含 `sessionId` 与 `sessionDate`。

### 当前自动识别范围

原型监听包含 `sold`、`purchased`、`bought`、`won` 或 `winner` 的新增页面节点，并尝试读取：

- `purchased by USER`、`won by USER`、`USER purchased` 等格式的买家名称；
- `Product #17`、`Auction 17`、`AUC-017` 等商品编号；
- Whatnot 页面 pinned/listing 区域中单独显示的 `1`–`100` 标题。
- Activity 中的 `silentgems won the auction · now / Brooches #12` 格式；
- 顶部通知中的 `silentgems just purchased Brooches #13!` 格式。

插件启动时会先记住 Activity 列表里的已有记录，不发送历史成交。之后会直接监听 `seller-live-activity-list` 的子节点、文字和 `title` 属性；容器一旦更新，就重新读取所有行，并把首次出现的“买家 + 商品编号”发送给WPF。该逻辑不再依赖 `now` 文案，因此即使页面更新时间直接变成 `1m ago` 也不会漏掉新行。

经实际页面确认，扩展会优先读取以下 Whatnot DOM：

```text
[data-testid="seller-live-activity-list"]
[data-testid^="seller-live-activity-row-"]
strong[title="买家 won the auction · now"]
strong[title="Brooches #15"]
```

读取 `title` 属性比依赖页面样式类名更稳定；Whatnot 的 Tailwind class 变化不会影响当前规则。

真实成交提示的 DOM 结构仍需用一次 Rehearsal Mode 或私密直播校准。当前版本不会截图、打印或保存数据库，只完成本阶段要求的“插件识别并发送，WPF接收并显示”。

## 要解决的问题

当前流程是提前创建 1–100 个 Whatnot 商品标题，直播时逐个展示，成交后手动给实物贴编号。主要风险是：

- 忘记贴号或跳号；
- 同一个编号重复使用；
- 截图、买家名和实物编号没有绑定；
- 直播结束后只能依靠录像和人工逐单核对；
- 订单顺序、商品顺序和实物摆放顺序容易错位。

## 推荐方案：自动检测 + 一键确认

不建议第一版完全依赖 OCR 自动判定成交。直播网络、Whatnot 页面布局和提示动画都会变化。更稳妥的方案是：

1. 监控 Whatnot 直播窗口的成交提示区域。
2. 检测到 “SOLD / buyer” 变化时，立即保存整窗截图和成交区域裁剪图。
3. OCR 读取买家名，同时保留原始截图作为证据。
4. 自动领取下一个连续产品编号，例如 `AUC-001`。
5. 操作员只需确认买家名和编号；确认后编号才正式占用。
6. 自动打印一张小标签贴到实物上，内容为产品编号、买家名和二维码。
7. 所有成交记录写入本地 SQLite，并实时导出 CSV 备份。
8. 直播结束后按编号查看 1–100 的完成、缺失、重复和待确认状态。

这种方式把“每次人工记号”变成“系统自动建档，人工只确认异常”，同时保留人工兜底。

## 操作流程

### 直播开始前

- 创建一场直播 Session。
- 设置编号范围，例如 1–100。
- 选择 Whatnot 窗口、成交提示截图区域和标签打印机。
- 做一次测试成交识别。
- 系统预生成 `AUC-001` 到 `AUC-100`，状态全部为 `Ready`。

### 每次成交

- 屏幕监控检测成交提示。
- 自动保存：
  - `full-screen.png`：完整直播窗口；
  - `sold-banner.png`：买家/成交提示区域；
  - `product-frame.png`：直播画面中的商品区域。
- OCR 识别买家名。
- 自动分配下一个可用编号。
- 播放提示音并显示确认卡片。
- 确认后打印标签，记录变为 `Confirmed`。
- 如果 OCR 不确定，记录进入 `Needs review`，但截图不会丢失。

### 直播结束后

- 显示 1–100 编号网格：
  - 绿色：已确认；
  - 黄色：待核对；
  - 灰色：未使用；
  - 红色：重复、跳号或缺少截图。
- 可按编号、买家名、成交时间搜索。
- 一键导出 `auction-summary.csv` 和截图文件夹。
- 发货清点时扫描标签二维码，系统标记 `Packed`。

## 状态模型

```text
Ready -> Detected -> NeedsReview -> Confirmed -> Labeled -> Packed
                     |                 |
                     +---- Cancelled <-+
```

编号只有在 `Confirmed` 时才正式占用。误识别可以取消，不会造成跳号。

## 数据结构

每次成交记录至少包含：

```json
{
  "sessionId": "2026-08-24-evening",
  "auctionNumber": 17,
  "productCode": "AUC-017",
  "whatnotListingTitle": "17",
  "buyerName": "examplebuyer",
  "buyerNameRaw": "examplebuyer",
  "ocrConfidence": 0.94,
  "detectedAt": "2026-08-24T20:41:12-04:00",
  "confirmedAt": "2026-08-24T20:41:15-04:00",
  "status": "Labeled",
  "screenshots": {
    "full": "screenshots/AUC-017-full.png",
    "sale": "screenshots/AUC-017-sale.png",
    "product": "screenshots/AUC-017-product.png"
  }
}
```

## 建议技术架构

- **桌面应用**：Tauri + Angular，和当前 Local Product Manager 技术栈一致。
- **屏幕采集**：Windows Graphics Capture，允许选择单个 Whatnot 浏览器窗口，不录制整个桌面。
- **变化检测**：先对成交提示区域做像素差异检测，只有发生变化时才运行 OCR，减少 CPU 占用。
- **OCR**：Windows OCR 或 Tesseract；保留置信度和原始裁剪图。
- **本地数据库**：SQLite；每次确认后立即事务写入。
- **截图目录**：按 Session 和产品编号存储，禁止覆盖已有截图。
- **标签打印**：复用本项目现有 BarTender / Label Printing 接口。
- **现有数据联动**：后续可将 `productCode` 关联到 Live Stream BID Product 或本地 Product，但 MVP 不强制依赖商品数据库。

## 可靠性设计

- 每个编号有唯一约束，数据库禁止重复确认。
- 截图先落盘，再写成交记录；写入失败时进入恢复队列。
- 每次成交同时写 SQLite 和追加 CSV journal。
- OCR 不确定时绝不静默确认。
- 提供 `Undo last sale`，撤销后编号回到可用队列，但保留审计记录。
- 提供快捷键：
  - `Space`：确认成交；
  - `R`：重新 OCR；
  - `S`：手动截图；
  - `U`：撤销上一笔；
  - `P`：重新打印标签。
- 浏览器页面变化导致自动检测失效时，始终可以点击 **Capture sale** 手动建档，编号仍由系统分配。

## MVP 范围

第一版建议只做：

- Session 和 1–100 编号池；
- Whatnot 窗口选择；
- 手动 Capture sale + 自动截图；
- 买家名 OCR 和人工确认；
- 自动连续编号；
- 标签打印；
- 成交记录列表、缺号检查和 CSV 导出。

第二版再增加自动检测成交提示、录像回溯、扫码打包和 Local Product Manager 数据关联。

## 文件布局建议

```text
AuctionProductMoniter/
  README.md
  src/
    capture/
    detection/
    ocr/
    numbering/
    printing/
    storage/
    ui/
  data/
    sessions/
      2026-08-24-evening/
        auction.db
        auction-journal.csv
        screenshots/
```

## 关键决定

编号应由本程序负责，Whatnot 上的 1–100 标题只是展示顺序，不再作为唯一成交记录。真正的唯一键是 Session + Auction Number。这样即使 Whatnot 页面识别失败，编号、截图和买家记录仍然不会丢失。
