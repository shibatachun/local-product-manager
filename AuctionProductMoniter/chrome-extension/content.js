(() => {
  const SALE_WORDS = /\b(sold|purchased|bought|won|winner)\b/i;
  const recentEvents = new Map();
  const recentDiagnostics = new Map();
  const seenActivitySales = new Set();
  let activityObserver = null;
  let documentObserver = null;
  let observedActivityList = null;
  let hasInitializedActivity = false;
  let activityRefreshTimer = 0;

  function stopMonitoring() {
    clearTimeout(activityRefreshTimer);
    activityObserver?.disconnect();
    documentObserver?.disconnect();
    activityObserver = null;
    documentObserver = null;
  }

  function safeSendMessage(message) {
    try {
      if (!chrome.runtime?.id) {
        stopMonitoring();
        return;
      }
      const result = chrome.runtime.sendMessage(message);
      result?.catch?.(error => {
        if (/context invalidated/i.test(error?.message || "")) stopMonitoring();
      });
    } catch (error) {
      if (/context invalidated/i.test(error?.message || "")) stopMonitoring();
    }
  }

  function clean(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function firstMatch(text, patterns) {
    for (const pattern of patterns) {
      const match = text.match(pattern);
      if (match?.[1]) return clean(match[1]);
    }
    return "";
  }

  function extractBuyer(text, element) {
    const explicit = element.querySelector?.(
      '[data-testid*="buyer" i], [aria-label*="buyer" i], [class*="buyer" i], [data-testid*="winner" i]'
    );
    const explicitText = clean(explicit?.textContent);
    const isBuyerBadge = /^(?:returning|new|top|verified)\s+buyer$/i.test(explicitText);
    if (explicitText && explicitText.length <= 80 && !isBuyerBadge) return explicitText;

    return firstMatch(text, [
      /(?:purchased\s+by|bought\s+by|won\s+by|winner[:\s]+)@?([\w.-]{2,64})/i,
      /@([\w.-]{2,64})\s+(?:purchased|bought|won)/i,
      /^@?([\w.-]{2,64})\s+(?:just\s+)?(?:purchased|bought|won)\b/i
    ]);
  }

  function extractProductNumber(text) {
    return firstMatch(text, [
      /(?:product|item|auction|lot|listing)\s*(?:number|no\.?|#)?\s*[:#-]?\s*([A-Z]{0,8}-?\d{1,5})/i,
      /#\s*([A-Z]{0,8}-?\d{1,5})\b/i
    ]) || (text.match(/\b(?:AUC|INV|LOT|ITEM)-?\d{1,5}\b/i)?.[0] || "");
  }

  function isFreshSale(text, element) {
    if (/\bjust\s+(?:purchased|bought)\b/i.test(text)) return true;
    if (/\bwon\s+the\s+auction\b[\s\S]*?(?:·|\b)(?:\s*just\s+)?now\b/i.test(text)) return true;
    if (/\b(?:sold|purchased|bought|won|winner)\b[\s\S]*?(?:·|\b)(?:\s*just\s+)?now\b/i.test(text)) return true;
    return element.matches?.('[role="alert"], [role="status"]') && SALE_WORDS.test(text);
  }

  function findCurrentProductNumber() {
    const selectors = [
      '[data-testid*="pinned" i]',
      '[data-testid*="listing" i]',
      '[aria-label*="pinned" i]',
      '[class*="pinned" i]'
    ];

    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        const elementText = clean(element.textContent);
        const plainNumber = elementText.match(/^#?\s*(\d{1,3})\s*$/)?.[1] || "";
        const value = extractProductNumber(elementText) || plainNumber;
        if (value) return value;
      }
    }

    return "";
  }

  function parseActivityRow(element) {
    const row = element.matches?.('[data-testid^="seller-live-activity-row-"]')
      ? element
      : element.closest?.('[data-testid^="seller-live-activity-row-"]');
    if (!row) return null;

    const titledElements = Array.from(row.querySelectorAll('[title]'));
    const activityText = clean(
      titledElements.find(item => /\b(?:won|purchased|bought|sold)\b/i.test(item.getAttribute("title") || ""))
        ?.getAttribute("title") || row.textContent
    );
    const productTitle = titledElements.find(item =>
      /#\s*[A-Z]{0,8}-?\d{1,5}\b/i.test(item.getAttribute("title") || "") &&
      !/\b(?:won|purchased|bought|sold)\b/i.test(item.getAttribute("title") || "")
    )?.getAttribute("title") || "";
    const productText = clean(productTitle || row.textContent);
    const buyerName = extractBuyer(activityText, row);
    const productNumber = extractProductNumber(productText);
    if (!buyerName || !productNumber) return null;

    return {
      row,
      buyerName,
      productNumber,
      productName: clean(productTitle),
      activityText,
      productText,
      sourceText: clean(`${activityText} ${productText}`),
      fresh: /(?:·|\b)\s*(?:just\s+)?now\b/i.test(activityText)
    };
  }

  function sendSale(buyerName, productNumber, sourceText, force = false, productName = "") {
    const key = `${buyerName.toLowerCase()}|${productNumber.toLowerCase()}|${productName.toLowerCase()}`;
    const now = Date.now();
    if (!force && now - (recentEvents.get(key) || 0) < 10000) return false;
    recentEvents.set(key, now);

    safeSendMessage({
      type: "SALE_DETECTED",
      sale: {
        buyerName,
        productNumber,
        productName,
        detectedAt: new Date().toISOString(),
        sourceText,
        pageUrl: location.href,
        captureScreenshot: !force
      }
    });
    return true;
  }

  function syncActivityToWpf() {
    const list = document.querySelector('[data-testid="seller-live-activity-list"]');
    if (!list) return { rows: 0, sent: 0, error: "The Activity list is not open." };

    const rows = Array.from(list.querySelectorAll('[data-testid^="seller-live-activity-row-"]'));
    let sent = 0;
    for (const row of rows.reverse()) {
      const activity = parseActivityRow(row);
      if (!activity) continue;
      if (sendSale(activity.buyerName, activity.productNumber, activity.sourceText, true, activity.productName)) sent++;
    }
    return { rows: rows.length, sent };
  }

  function activitySignature(activity) {
    return `${activity.buyerName.toLowerCase()}|${activity.productNumber.toLowerCase()}|${activity.productName.toLowerCase()}`;
  }

  function readActivityRows(sendNewRows) {
    const list = document.querySelector('[data-testid="seller-live-activity-list"]');
    if (!list) return { rows: 0, newSales: 0 };

    const rows = Array.from(list.querySelectorAll('[data-testid^="seller-live-activity-row-"]'));
    let newSales = 0;
    for (const row of rows) {
      const activity = parseActivityRow(row);
      if (!activity) continue;

      reportDiagnostic(row, activity.sourceText);
      const signature = activitySignature(activity);
      if (seenActivitySales.has(signature)) continue;
      seenActivitySales.add(signature);

      if (sendNewRows) {
        newSales++;
        sendSale(activity.buyerName, activity.productNumber, activity.sourceText, false, activity.productName);
      }
    }

    return { rows: rows.length, newSales };
  }

  function scheduleActivityRefresh() {
    clearTimeout(activityRefreshTimer);
    activityRefreshTimer = setTimeout(() => readActivityRows(true), 60);
  }

  function attachActivityObserver() {
    const list = document.querySelector('[data-testid="seller-live-activity-list"]');
    if (!list || list === observedActivityList) return Boolean(list);

    activityObserver?.disconnect();
    observedActivityList = list;
    readActivityRows(hasInitializedActivity);
    hasInitializedActivity = true;
    activityObserver = new MutationObserver(scheduleActivityRefresh);
    activityObserver.observe(list, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["title", "data-testid"]
    });
    return true;
  }

  function reportCandidate(element) {
    if (!(element instanceof HTMLElement)) return;

    const insideActivity = element.matches?.('[data-testid="seller-live-activity-list"]') ||
      element.closest?.('[data-testid="seller-live-activity-list"]');
    if (insideActivity) {
      const activity = parseActivityRow(element);
      if (activity) {
        reportDiagnostic(activity.row, activity.sourceText);
        if (activity.fresh) {
          sendSale(activity.buyerName, activity.productNumber, activity.sourceText, false, activity.productName);
        }
      }
      return;
    }

    const text = clean(element.innerText || element.textContent);
    if (!text || text.length > 2000) return;

    if (!SALE_WORDS.test(text)) return;
    if (!isFreshSale(text, element)) return;

    const buyerName = extractBuyer(text, element);
    const productNumber = extractProductNumber(text) || findCurrentProductNumber();
    if (!buyerName || !productNumber) return;

    sendSale(buyerName, productNumber, text);
  }

  function reportDiagnostic(element, text) {
    const attributeText = clean(Array.from(element.attributes || [])
      .map(attribute => `${attribute.name}=${attribute.value}`)
      .join(" "));
    const descendant = element.querySelector?.(
      '[data-testid*="buyer" i], [aria-label*="buyer" i], [class*="buyer" i], ' +
      '[data-testid*="winner" i], [aria-label*="winner" i], [class*="winner" i], ' +
      '[data-testid*="listing" i], [aria-label*="listing" i], [class*="listing" i], ' +
      '[data-testid*="pinned" i], [aria-label*="pinned" i], [class*="pinned" i]'
    );
    const descendantAttributes = descendant
      ? clean(Array.from(descendant.attributes).map(a => `${a.name}=${a.value}`).join(" "))
      : "";
    const allAttributes = `${attributeText} ${descendantAttributes}`;
    const hasSaleKeyword = SALE_WORDS.test(text);
    const hasBuyerField = /buyer|winner/i.test(allAttributes) || Boolean(extractBuyer(text, element));
    const productNumber = extractProductNumber(text) || findCurrentProductNumber();
    const hasProductField = /product|item|auction|lot|listing|pinned/i.test(allAttributes) || Boolean(productNumber);

    if (!hasSaleKeyword && !hasBuyerField && !hasProductField) return;

    const buyerName = extractBuyer(text, element);
    const signature = `${hasSaleKeyword}|${hasBuyerField}|${hasProductField}|${text.slice(0, 180)}`;
    const now = Date.now();
    if (now - (recentDiagnostics.get(signature) || 0) < 15000) return;
    recentDiagnostics.set(signature, now);

    safeSendMessage({
      type: "DOM_DIAGNOSTIC",
      diagnostic: {
        kind: "activity",
        hasSaleKeyword,
        hasBuyerField,
        hasProductField,
        buyerName,
        productNumber,
        tagName: element.tagName.toLowerCase(),
        attributes: attributeText.slice(0, 500),
        text: text.slice(0, 1000),
        detectedAt: new Date().toISOString()
      }
    });
  }

  function scanVisibleDom() {
    let scanned = 0;
    let matched = 0;
    const elements = document.querySelectorAll('[data-testid^="seller-live-activity-row-"]');
    for (const element of elements) {
      if (!(element instanceof HTMLElement)) continue;
      scanned++;
      const activity = parseActivityRow(element);
      if (!activity) continue;
      const before = recentDiagnostics.size;
      reportDiagnostic(element, activity.sourceText);
      if (recentDiagnostics.size > before) matched++;
    }
    return { scanned, matched };
  }

  documentObserver = new MutationObserver(mutations => {
    attachActivityObserver();
    for (const mutation of mutations) {
      for (const node of mutation.addedNodes) {
        let element = node instanceof HTMLElement ? node : node.parentElement;
        for (let level = 0; element && level < 6; level++, element = element.parentElement) {
          reportCandidate(element);
        }
      }
    }
  });

  documentObserver.observe(document.documentElement, { childList: true, subtree: true });

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type === "RUN_DIAGNOSTIC_SCAN") {
      recentDiagnostics.clear();
      sendResponse({ ok: true, ...scanVisibleDom() });
      return false;
    }
    if (message?.type === "SYNC_ACTIVITY_TO_WPF") {
      sendResponse({ ok: true, ...syncActivityToWpf() });
      return false;
    }
    return false;
  });

  setTimeout(() => {
    attachActivityObserver();
    scanVisibleDom();
  }, 1500);
})();
