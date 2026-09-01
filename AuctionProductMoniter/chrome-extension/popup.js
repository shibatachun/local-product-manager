const scanStatusElement = document.getElementById("scanStatus");
const diagnosticsElement = document.getElementById("diagnostics");
const screenshotStatusElement = document.getElementById("screenshotStatus");

function loadScreenshotStatus() {
  chrome.storage.local.get({ screenshotStatus: null }).then(({ screenshotStatus }) => {
    screenshotStatusElement.classList.toggle("failed", screenshotStatus?.ok === false);
    screenshotStatusElement.textContent = screenshotStatus?.message
      ? `Screenshot status: ${screenshotStatus.message}`
      : "Screenshot status: Waiting for a new sale. Keep the Whatnot tab in the foreground while live.";
  });
}

function escapeHtml(value) {
  return String(value || "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function loadDiagnostics() {
  chrome.runtime.sendMessage({ type: "GET_DIAGNOSTICS" }, response => {
    const items = (response?.diagnostics || []).filter(item => item.kind === "activity");
    if (!items.length) {
      diagnosticsElement.innerHTML = '<div class="hint">No Activity records detected yet. Keep the Activity tab open.</div>';
      return;
    }
    diagnosticsElement.innerHTML = items.slice(0, 100).map(item => {
      const title = `${item.buyerName || "Unknown buyer"}  ·  #${item.productNumber || "?"}`;
      return `<div class="result">
        <div class="flags">${escapeHtml(title)}</div>
        <div class="sample">${escapeHtml(item.text).slice(0, 500)}</div>
      </div>`;
    }).join("");
  });
}

document.getElementById("scan").addEventListener("click", async () => {
  scanStatusElement.textContent = "Scanning…";
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url?.includes("whatnot.com")) {
    scanStatusElement.textContent = "Open the Whatnot live page first.";
    return;
  }
  chrome.tabs.sendMessage(tab.id, { type: "RUN_DIAGNOSTIC_SCAN" }, response => {
    if (chrome.runtime.lastError) {
      scanStatusElement.textContent = "The page script is not loaded. Refresh the Whatnot page and try again.";
      return;
    }
    scanStatusElement.textContent = `Scanned ${response?.scanned || 0} Activity records; ${response?.matched || 0} new records found.`;
    setTimeout(loadDiagnostics, 250);
    setTimeout(loadDiagnostics, 750);
    setTimeout(loadDiagnostics, 1500);
  });
});

document.getElementById("sync").addEventListener("click", async () => {
  scanStatusElement.textContent = "Syncing to WPF…";
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url?.includes("whatnot.com")) {
    scanStatusElement.textContent = "Open the Whatnot live page first.";
    return;
  }
  chrome.tabs.sendMessage(tab.id, { type: "SYNC_ACTIVITY_TO_WPF" }, response => {
    if (chrome.runtime.lastError) {
      scanStatusElement.textContent = "The page script is not loaded. Refresh the Whatnot page and try again.";
      return;
    }
    if (response?.error) {
      scanStatusElement.textContent = response.error;
      return;
    }
    scanStatusElement.textContent = `Sent ${response?.sent || 0} Activity records to WPF.`;
  });
});

document.getElementById("clear").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "CLEAR_DIAGNOSTICS" }, () => {
    scanStatusElement.textContent = "Activity records cleared.";
    loadDiagnostics();
  });
});

loadDiagnostics();
loadScreenshotStatus();

chrome.storage.onChanged.addListener((changes, areaName) => {
  if (areaName === "local" && changes.diagnostics) loadDiagnostics();
  if (areaName === "local" && changes.screenshotStatus) loadScreenshotStatus();
});
