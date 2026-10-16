const RECEIVER_URL = "http://127.0.0.1:17891/api/sales";
const SCREENSHOT_URL = "http://127.0.0.1:17891/api/screenshots";
let diagnosticWriteQueue = Promise.resolve();

chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({ diagnostics: [], screenshotStatus: null });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "DOM_DIAGNOSTIC") {
    diagnosticWriteQueue = diagnosticWriteQueue
      .catch(() => undefined)
      .then(() => storeDiagnostic(message.diagnostic, sender.tab?.url));
    diagnosticWriteQueue
      .then(() => sendResponse({ ok: true }))
      .catch(error => sendResponse({ ok: false, error: error.message }));
    return true;
  }

  if (message?.type === "GET_DIAGNOSTICS") {
    chrome.storage.local.get({ diagnostics: [] }).then(({ diagnostics }) => {
      sendResponse({ ok: true, diagnostics });
    });
    return true;
  }

  if (message?.type === "CLEAR_DIAGNOSTICS") {
    chrome.storage.local.set({ diagnostics: [] }).then(() => sendResponse({ ok: true }));
    return true;
  }

  if (message?.type !== "SALE_DETECTED") return false;

  const payload = {
    buyerName: String(message.sale?.buyerName || "").trim(),
    productNumber: String(message.sale?.productNumber || "").trim(),
    productName: String(message.sale?.productName || "").trim().slice(0, 500),
    detectedAt: message.sale?.detectedAt || new Date().toISOString(),
    sourceText: String(message.sale?.sourceText || "").slice(0, 2000),
    pageUrl: sender.tab?.url || message.sale?.pageUrl || ""
  };

  handleSale(payload, sender.tab, Boolean(message.sale?.captureScreenshot))
    .then(sendResponse)
    .catch(error => sendResponse({ ok: false, error: error.message }));

  return true;
});

async function handleSale(payload, sourceTab, captureScreenshot) {
  const response = await fetch(RECEIVER_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  const body = await response.text();
  if (response.ok && captureScreenshot) {
    await new Promise(resolve => setTimeout(resolve, 1000));
    await captureAndSendScreenshot(sourceTab, payload);
  }
  return { ok: response.ok, status: response.status, body };
}

async function captureAndSendScreenshot(sourceTab, sale) {
  if (!sourceTab?.id || sourceTab.windowId == null) {
    await setScreenshotStatus(false, "Could not identify the Whatnot live tab.", sale);
    return false;
  }
  const [activeTab] = await chrome.tabs.query({ active: true, windowId: sourceTab.windowId });
  if (activeTab?.id !== sourceTab.id) {
    await setScreenshotStatus(false, "The Whatnot tab was not in the foreground one second after the sale, so no screenshot was captured.", sale);
    return false;
  }
  try {
    const imageDataUrl = await chrome.tabs.captureVisibleTab(sourceTab.windowId, { format: "jpeg", quality: 90 });
    const response = await fetch(SCREENSHOT_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        buyerName: sale.buyerName,
        productNumber: sale.productNumber,
        imageDataUrl,
        capturedAt: new Date().toISOString(),
        pageUrl: sourceTab.url || sale.pageUrl || ""
      })
    });
    const body = await response.text();
    if (!response.ok) {
      await setScreenshotStatus(false, `WPF did not save the screenshot (HTTP ${response.status}): ${body}`, sale);
      return false;
    }
    await setScreenshotStatus(true, `Screenshot saved: ${sale.buyerName} · #${sale.productNumber}`, sale);
    return true;
  } catch (error) {
    console.warn("Auction Product Monitor screenshot failed:", error);
    await setScreenshotStatus(false, `Screenshot failed: ${error?.message || error}. Click the extension icon once on the Whatnot page to grant screenshot permission.`, sale);
    return false;
  }
}

async function setScreenshotStatus(ok, message, sale) {
  await chrome.storage.local.set({ screenshotStatus: {
    ok, message,
    buyerName: sale?.buyerName || "",
    productNumber: sale?.productNumber || "",
    updatedAt: new Date().toISOString()
  }});
}

async function storeDiagnostic(diagnostic, pageUrl) {
  const result = await chrome.storage.local.get({ diagnostics: [] });
  const entry = {
    ...diagnostic,
    pageUrl: pageUrl || diagnostic?.pageUrl || "",
    detectedAt: diagnostic?.detectedAt || new Date().toISOString()
  };
  const entryKey = `${String(entry.buyerName || "").toLowerCase()}|${String(entry.productNumber || "").toLowerCase()}`;
  const diagnostics = [
    entry,
    ...result.diagnostics.filter(item =>
      `${String(item.buyerName || "").toLowerCase()}|${String(item.productNumber || "").toLowerCase()}` !== entryKey)
  ].slice(0, 200);
  await chrome.storage.local.set({ diagnostics });
}
