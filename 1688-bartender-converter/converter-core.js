(function attachConverterCore(globalScope) {
  "use strict";

  const GRAMS_PER_OUNCE = 28.349523125;
  const SHIPPING_COST_PER_GRAM = 0.05;
  const DEFAULT_RMB_TO_USD_RATE = 0.15;
  const EXPORT_HEADERS = [
    "UUID",
    "Title",
    "Quantity",
    "Inventory",
    "Unit Price",
    "Weight (g)",
    "Weight (oz)",
    "Material",
    "Total Cost",
    "Whatnot Auction Safe Price",
    "Item Code",
    "Expected Inventory",
  ];

  function uniqueHeaders(rawHeaders) {
    const counts = new Map();
    return rawHeaders.map((rawHeader, index) => {
      const base = String(rawHeader ?? "").trim() || `Column_${index + 1}`;
      const count = (counts.get(base) || 0) + 1;
      counts.set(base, count);
      return count === 1 ? base : `${base}_${count}`;
    });
  }

  function formatCellValue(value, header) {
    if (value instanceof Date && !Number.isNaN(value.valueOf())) {
      const pad = (part) => String(part).padStart(2, "0");
      return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())} ${pad(value.getHours())}:${pad(value.getMinutes())}:${pad(value.getSeconds())}`;
    }
    if (value == null) return "";
    if (/时间$/.test(header) && typeof value === "number") {
      const epoch = new Date(Date.UTC(1899, 11, 30));
      const date = new Date(epoch.getTime() + value * 86400000);
      return formatCellValue(date, header);
    }
    return String(value);
  }

  function flattenWorksheet(XLSX, worksheet) {
    const rows = XLSX.utils.sheet_to_json(worksheet, {
      header: 1,
      raw: true,
      defval: "",
      blankrows: false,
    });
    if (!rows.length) throw new Error("所选工作表没有数据。");

    const merges = Array.isArray(worksheet["!merges"]) ? worksheet["!merges"] : [];
    for (const merge of merges) {
      const value = rows[merge.s.r]?.[merge.s.c] ?? "";
      for (let rowIndex = merge.s.r; rowIndex <= merge.e.r; rowIndex += 1) {
        if (!rows[rowIndex]) rows[rowIndex] = [];
        for (let columnIndex = merge.s.c; columnIndex <= merge.e.c; columnIndex += 1) {
          rows[rowIndex][columnIndex] = value;
        }
      }
    }

    const width = Math.max(...rows.map((row) => row.length));
    rows.forEach((row) => {
      while (row.length < width) row.push("");
    });
    const headers = uniqueHeaders(rows[0]);
    const records = rows.slice(1).map((row) => Object.fromEntries(
      headers.map((header, index) => [header, formatCellValue(row[index], header)]),
    ));
    return { headers, records, mergeCount: merges.length };
  }

  function detectGrams(title) {
    const matches = [...String(title || "").matchAll(/(?<![a-z])(\d+(?:\.\d+)?)\s*g\b/gi)];
    return matches.length ? matches[matches.length - 1][1] : "";
  }

  function detectMaterial(title) {
    const text = String(title || "");
    const materialGroups = [
      ["不锈钢", "不锈钢"],
      ["锌合金", "锌合金"],
      ["铝合金", "铝合金"],
      ["合金", "合金"],
      ["绿松石", "绿松石"],
      ["松石", "松石"],
      ["黄铜", "黄铜"],
      ["铜", "铜"],
      ["铁", "铁"],
      ["铝", "铝"],
      ["亚克力", "亚克力"],
      ["树脂", "树脂"],
      ["塑料", "塑料"],
      ["玻璃", "玻璃"],
      ["水晶", "水晶"],
      ["陶瓷", "陶瓷"],
      ["珍珠", "珍珠"],
      ["贝壳", "贝壳"],
      ["木", "木"],
      ["皮革", "皮革"],
    ];
    const matches = [];
    for (const [keyword, label] of materialGroups) {
      if (!text.includes(keyword)) continue;
      if (keyword === "合金" && matches.some((item) => item.includes("合金"))) continue;
      if (keyword === "松石" && matches.includes("绿松石")) continue;
      if (keyword === "铜" && matches.includes("黄铜")) continue;
      if (keyword === "铝" && matches.includes("铝合金")) continue;
      matches.push(label);
    }
    return matches.join(" / ");
  }

  function calculate(grValue, priceValue, quantityValue, priceType = "per-unit") {
    const grams = Number.parseFloat(grValue);
    const price = Number.parseFloat(priceValue);
    const quantity = Number.parseFloat(quantityValue);
    const hasGrams = Number.isFinite(grams);
    const merchandiseCost = Number.isFinite(price)
      ? priceType === "per-package"
        ? price
        : Number.isFinite(quantity) ? price * quantity : NaN
      : NaN;
    const packageCost = hasGrams && Number.isFinite(merchandiseCost)
      ? SHIPPING_COST_PER_GRAM * grams + merchandiseCost
      : NaN;
    const totalCost = Number.isFinite(packageCost) ? packageCost.toFixed(2) : "";
    return {
      ounces: hasGrams ? (grams / GRAMS_PER_OUNCE).toFixed(4).replace(/0+$/, "").replace(/\.$/, "") : "",
      totalCost,
      whatnotSafePrice: calculateWhatnotSafePrice(totalCost),
    };
  }

  function calculateWhatnotSafePrice(totalCostRmb, rmbToUsdRate = DEFAULT_RMB_TO_USD_RATE) {
    const cost = Number.parseFloat(totalCostRmb);
    const exchangeRate = Number.parseFloat(rmbToUsdRate);
    if (!Number.isFinite(cost) || !Number.isFinite(exchangeRate) || exchangeRate <= 0) return "";
    const commissionRate = 0.08;
    const processingRate = 0.029;
    const feeTaxReserveRate = 0.01;
    const fixedProcessingFee = 0.30;
    const assumedBuyerShippingAndTax = 10.00;
    const packagingReserve = 0.25;
    const hostLaborCostPerPackage = 25 / 60;
    const costSafetyBufferRate = 0.10;
    const protectedCost = cost * exchangeRate * (1 + costSafetyBufferRate) + packagingReserve + hostLaborCostPerPackage;
    const fixedAndAncillaryFees = fixedProcessingFee + assumedBuyerShippingAndTax * processingRate;
    const retainedSaleRate = 1 - commissionRate - processingRate - feeTaxReserveRate;
    return Math.ceil((protectedCost + fixedAndAncillaryFees) / retainedSaleRate).toFixed(2);
  }

  function priceToItemCode(suggestedPrice) {
    let value = Math.round(Number.parseFloat(suggestedPrice));
    if (!Number.isFinite(value) || value < 1) return "";
    let code = "";
    while (value > 0) {
      value -= 1;
      code = String.fromCharCode(65 + (value % 26)) + code;
      value = Math.floor(value / 26);
    }
    return code;
  }

  function bytesToUuid(bytes) {
    const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, "0")).join("").toUpperCase();
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
  }

  async function stableUuid(seed) {
    let subtle = globalScope.crypto?.subtle;
    if (!subtle && typeof require === "function") {
      subtle = require("node:crypto").webcrypto.subtle;
    }
    if (!subtle) throw new Error("当前浏览器不支持生成二维码唯一码所需的加密 API。");
    const digest = new Uint8Array(await subtle.digest("SHA-256", new TextEncoder().encode(seed)));
    const bytes = digest.slice(0, 16);
    bytes[6] = (bytes[6] & 0x0f) | 0x50;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    return bytesToUuid(bytes);
  }

  async function enrichRecords(flattened) {
    const records = [];
    const groupedSources = new Map();
    flattened.records.forEach((source, index) => {
      const itemNumber = String(source["单品货号"] || "").trim();
      const skuId = String(source["SKU ID"] || "").trim();
      const identity = itemNumber && skuId
        ? `product:${itemNumber}|sku:${skuId}`
        : JSON.stringify(Object.keys(source)
          .sort()
          .map((key) => [key, String(source[key] ?? "").trim()]));
      const existing = groupedSources.get(identity);
      if (existing) {
        existing.inventory += 1;
      } else {
        groupedSources.set(identity, { source, firstIndex: index, inventory: 1, itemNumber, skuId });
      }
    });

    for (const { source, firstIndex, inventory, itemNumber, skuId } of groupedSources.values()) {
      const index = firstIndex;
      const seed = `${source["订单编号"] || ""}|${source["SKU ID"] || ""}|${source["单品货号"] || ""}|${index}`;
      const bidCode = itemNumber && skuId
        ? `BID|${itemNumber}|${skuId}`
        : `BID|${await stableUuid(seed)}`;
      const grams = detectGrams(source["货品标题"]);
      const calculated = calculate(grams, source["单价(元)"], source["数量"], "per-unit");
      records.push({
        UUID: bidCode,
        Title: source["货品标题"] || "",
        Quantity: source["数量"] || "",
        Inventory: 0,
        "Expected Inventory": inventory,
        "Price Type": "per-unit",
        "Unit Price": source["单价(元)"] || "",
        "Weight (g)": grams,
        "Weight (oz)": calculated.ounces,
        Material: detectMaterial(source["货品标题"]),
        "Total Cost": calculated.totalCost,
        "Whatnot Auction Safe Price": calculated.whatnotSafePrice,
        "Item Code": priceToItemCode(calculated.whatnotSafePrice),
      });
    }
    return {
      headers: EXPORT_HEADERS,
      records,
      mergeCount: flattened.mergeCount,
    };
  }

  function updateRecord(record, grams) {
    record["Weight (g)"] = String(grams ?? "").trim();
    const calculated = calculate(
      record["Weight (g)"],
      record["Unit Price"],
      record.Quantity,
      record["Price Type"],
    );
    record["Weight (oz)"] = calculated.ounces;
    record["Total Cost"] = calculated.totalCost;
    record["Whatnot Auction Safe Price"] = calculated.whatnotSafePrice;
    record["Item Code"] = priceToItemCode(calculated.whatnotSafePrice);
    return record;
  }

  async function createBlankRecord(seed = `${Date.now()}|${globalScope.crypto?.randomUUID?.() || Math.random()}`) {
    return {
      UUID: `BID|${await stableUuid(`manual|${seed}`)}`,
      Title: "",
      Quantity: 1,
      Inventory: 0,
      "Expected Inventory": 1,
      "Price Type": "per-package",
      "Unit Price": "",
      "Weight (g)": "",
      "Weight (oz)": "",
      Material: "",
      "Total Cost": "",
      "Whatnot Auction Safe Price": "",
      "Item Code": "",
    };
  }

  function escapeCsv(value) {
    const text = String(value ?? "");
    return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
  }

  const TWO_DECIMAL_EXPORT_FIELDS = new Set([
    "Unit Price",
    "Weight (g)",
    "Weight (oz)",
    "Total Cost",
    "Whatnot Auction Safe Price",
  ]);

  function formatExportValue(header, value) {
    if (!TWO_DECIMAL_EXPORT_FIELDS.has(header) || value === "" || value === null || value === undefined) return value;
    const number = Number.parseFloat(value);
    return Number.isFinite(number) ? number.toFixed(2) : value;
  }

  function toCsv(data) {
    const lines = [data.headers.map(escapeCsv).join(",")];
    for (const record of data.records) {
      lines.push(data.headers.map((header) => escapeCsv(formatExportValue(header, record[header]))).join(","));
    }
    return `\uFEFF${lines.join("\r\n")}`;
  }

  const api = {
    GRAMS_PER_OUNCE,
    SHIPPING_COST_PER_GRAM,
    DEFAULT_RMB_TO_USD_RATE,
    EXPORT_HEADERS: [...EXPORT_HEADERS],
    flattenWorksheet,
    detectGrams,
    detectMaterial,
    calculate,
    calculateWhatnotSafePrice,
    priceToItemCode,
    stableUuid,
    enrichRecords,
    createBlankRecord,
    updateRecord,
    toCsv,
  };

  globalScope.ConverterCore = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
}(typeof globalThis !== "undefined" ? globalThis : window));
