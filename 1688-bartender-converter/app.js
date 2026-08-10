(function initializeApp() {
  "use strict";

  const elements = {
    fileInput: document.querySelector("#file-input"),
    dropZone: document.querySelector("#drop-zone"),
    dropTitle: document.querySelector("#drop-title"),
    dropSubtitle: document.querySelector("#drop-subtitle"),
    workspace: document.querySelector("#workspace"),
    rowCount: document.querySelector("#row-count"),
    mergeCount: document.querySelector("#merge-count"),
    weightCount: document.querySelector("#weight-count"),
    missingCount: document.querySelector("#missing-count"),
    searchInput: document.querySelector("#search-input"),
    recordBody: document.querySelector("#record-body"),
    emptyFilter: document.querySelector("#empty-filter"),
    exportButton: document.querySelector("#export-button"),
    message: document.querySelector("#message"),
  };

  let convertedData = null;
  let sourceName = "1688-order";

  function setMessage(message = "") {
    elements.message.textContent = message;
    elements.message.classList.toggle("hidden", !message);
  }

  function updateSummary() {
    if (!convertedData) return;
    const withWeight = convertedData.records.filter((record) => record["Weight (g)"] !== "").length;
    elements.rowCount.textContent = convertedData.records.length;
    elements.mergeCount.textContent = convertedData.mergeCount;
    elements.weightCount.textContent = withWeight;
    elements.missingCount.textContent = convertedData.records.length - withWeight;
  }

  function createCell(text, className = "") {
    const cell = document.createElement("td");
    cell.textContent = text ?? "";
    if (className) cell.className = className;
    if (text) cell.title = text;
    return cell;
  }

  function displayWeight(value, unit) {
    return value === "" || value == null ? "—" : `${value} ${unit}`;
  }

  function renderRecords() {
    if (!convertedData) return;
    const query = elements.searchInput.value.trim().toLocaleLowerCase();
    const fragment = document.createDocumentFragment();
    let visible = 0;

    convertedData.records.forEach((record, index) => {
      const searchable = [record.UUID, record.Title, record.Material]
        .join(" ")
        .toLocaleLowerCase();
      if (query && !searchable.includes(query)) return;
      visible += 1;

      const row = document.createElement("tr");
      row.append(createCell(index + 1, "row-number"));
      row.append(createCell(record.UUID, "uuid"));

      const titleCell = document.createElement("td");
      titleCell.className = "title-cell";
      const titleInput = document.createElement("textarea");
      titleInput.className = "title-input";
      titleInput.rows = 2;
      titleInput.value = record.Title;
      titleInput.placeholder = "输入产品标题";
      titleInput.setAttribute("aria-label", `第${index + 1}行产品标题`);
      titleInput.addEventListener("input", () => { record.Title = titleInput.value.trim(); });
      titleCell.append(titleInput);
      row.append(titleCell);

      row.append(createCell(record.Quantity, "numeric"));

      const inventoryCell = document.createElement("td");
      inventoryCell.className = "numeric";
      const inventoryInput = document.createElement("input");
      inventoryInput.className = "inventory-input";
      inventoryInput.type = "number";
      inventoryInput.inputMode = "numeric";
      inventoryInput.min = "0";
      inventoryInput.step = "1";
      inventoryInput.value = record.Inventory;
      inventoryInput.setAttribute("aria-label", `第${index + 1}行库存数量`);
      inventoryInput.addEventListener("input", () => {
        const value = Number.parseInt(inventoryInput.value, 10);
        record.Inventory = Number.isFinite(value) && value >= 0 ? value : 0;
      });
      inventoryCell.append(inventoryInput);
      row.append(inventoryCell);
      row.append(createCell(record["Unit Price"], "numeric"));

      const weightCell = document.createElement("td");
      weightCell.className = "numeric";
      const weightField = document.createElement("div");
      weightField.className = "weight-field";
      const input = document.createElement("input");
      input.className = `weight-input${record["Weight (g)"] === "" ? " missing" : ""}`;
      input.type = "number";
      input.inputMode = "decimal";
      input.min = "0";
      input.step = "any";
      input.value = record["Weight (g)"];
      input.setAttribute("aria-label", `第${index + 1}行重量，单位克`);
      input.addEventListener("input", () => {
        ConverterCore.updateRecord(record, input.value);
        input.classList.toggle("missing", record["Weight (g)"] === "");
        row.children[7].textContent = displayWeight(record["Weight (oz)"], "oz");
        row.children[9].textContent = record["Total Cost"];
        updateSummary();
      });
      const unit = document.createElement("span");
      unit.className = "weight-unit";
      unit.textContent = "g";
      weightField.append(input, unit);
      weightCell.append(weightField);
      row.append(weightCell);
      row.append(createCell(displayWeight(record["Weight (oz)"], "oz"), "numeric weight-output"));

      const materialCell = document.createElement("td");
      const materialInput = document.createElement("input");
      materialInput.className = "material-input";
      materialInput.type = "text";
      materialInput.value = record.Material;
      materialInput.placeholder = "输入材质";
      materialInput.setAttribute("aria-label", `第${index + 1}行材质`);
      materialInput.addEventListener("input", () => { record.Material = materialInput.value.trim(); });
      materialCell.append(materialInput);
      row.append(materialCell);
      row.append(createCell(record["Total Cost"], "numeric"));
      fragment.append(row);
    });

    elements.recordBody.replaceChildren(fragment);
    elements.emptyFilter.classList.toggle("hidden", visible !== 0);
  }

  async function processFile(file) {
    setMessage();
    if (!file || !file.name.toLowerCase().endsWith(".xlsx")) {
      setMessage("请选择1688导出的 .xlsx 文件。");
      return;
    }
    if (!window.XLSX) {
      setMessage("Excel 解析组件没有加载。请确认 vendor/xlsx.full.min.js 文件存在。");
      return;
    }

    elements.exportButton.disabled = true;
    elements.dropTitle.textContent = "正在转换…";
    elements.dropSubtitle.textContent = file.name;
    try {
      const buffer = await file.arrayBuffer();
      const workbook = XLSX.read(buffer, { type: "array", cellDates: true });
      if (!workbook.SheetNames.length) throw new Error("工作簿中没有工作表。");
      const worksheet = workbook.Sheets[workbook.SheetNames[0]];
      const flattened = ConverterCore.flattenWorksheet(XLSX, worksheet);
      convertedData = await ConverterCore.enrichRecords(flattened);
      sourceName = file.name.replace(/\.xlsx$/i, "");
      elements.dropTitle.textContent = file.name;
      elements.dropSubtitle.textContent = `已读取工作表：${workbook.SheetNames[0]}`;
      elements.searchInput.value = "";
      updateSummary();
      renderRecords();
      elements.workspace.classList.remove("hidden");
      elements.workspace.scrollIntoView({ behavior: "smooth", block: "start" });
    } catch (error) {
      console.error(error);
      convertedData = null;
      elements.workspace.classList.add("hidden");
      elements.dropTitle.textContent = "选择或拖入 Excel 文件";
      elements.dropSubtitle.textContent = "原始文件不会被修改或上传";
      setMessage(`转换失败：${error.message || error}`);
    } finally {
      elements.exportButton.disabled = false;
      elements.fileInput.value = "";
    }
  }

  function downloadCsv() {
    if (!convertedData) return;
    const csv = ConverterCore.toCsv(convertedData);
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${sourceName}_BarTender.csv`;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  elements.fileInput.addEventListener("change", () => processFile(elements.fileInput.files[0]));
  elements.searchInput.addEventListener("input", renderRecords);
  elements.exportButton.addEventListener("click", downloadCsv);

  ["dragenter", "dragover"].forEach((eventName) => {
    elements.dropZone.addEventListener(eventName, (event) => {
      event.preventDefault();
      elements.dropZone.classList.add("dragging");
    });
  });
  ["dragleave", "drop"].forEach((eventName) => {
    elements.dropZone.addEventListener(eventName, (event) => {
      event.preventDefault();
      elements.dropZone.classList.remove("dragging");
    });
  });
  elements.dropZone.addEventListener("drop", (event) => processFile(event.dataTransfer.files[0]));
}());
