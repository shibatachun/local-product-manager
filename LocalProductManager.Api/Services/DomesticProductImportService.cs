using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class DomesticProductImportService(AppDbContext db)
{
    public async Task<DomesticProductImportResult> ImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var existing = (await db.DomesticProductInfos.ToListAsync(cancellationToken))
            .ToDictionary(item => item.Sku, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var processed = 0;
        var created = 0;
        var updated = 0;
        var skipped = 0;

        await using var stream = file.OpenReadStream();
        foreach (var source in XlsxRows.ReadFirstSheet(stream))
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            var sku = Value(source, "sku").Trim();
            if (string.IsNullOrWhiteSpace(sku)) { skipped++; continue; }

            var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (header, value) in source)
                if (!string.IsNullOrWhiteSpace(value))
                    details[HeaderTranslations.GetValueOrDefault(header, header)] = value.Trim();

            if (!existing.TryGetValue(sku, out var item))
            {
                item = new DomesticProductInfo { Sku = sku, ImportedAt = now };
                existing[sku] = item;
                db.DomesticProductInfos.Add(item);
                created++;
            }
            else updated++;

            item.ProductCode = NullIfEmpty(Value(source, "商品编码"));
            item.ProductName = NullIfEmpty(Value(source, "商品名称"));
            item.Material = NullIfEmpty(Value(source, "材质"));
            item.Specification = NullIfEmpty(Value(source, "规格"));
            item.CostRmb = DecimalValue(source, "成本价(RMB)");
            item.MinimumSalePriceUsd = DecimalValue(source, "最低售价($)");
            item.WarehouseInventory = IntValue(source, "所有仓库库存数量");
            item.SupplierName = NullIfEmpty(Value(source, "供应商名称"));
            item.ChineseDeclarationName = NullIfEmpty(Value(source, "中文申报名"));
            item.EnglishDeclarationName = NullIfEmpty(Value(source, "英文申报名"));
            item.WeightGrams = DecimalValue(source, "重量(克)");
            item.ImagePath = NullIfEmpty(Value(source, "图片路径")) ?? NullIfEmpty(Value(source, "图片"));
            item.DetailsJson = JsonSerializer.Serialize(details);
            item.SourceFileName = Path.GetFileName(file.FileName);
            item.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new(processed, created, updated, skipped, Path.GetFileName(file.FileName));
    }

    private static string Value(IReadOnlyDictionary<string, string> row, string key) => row.GetValueOrDefault(key) ?? "";
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal? DecimalValue(IReadOnlyDictionary<string, string> row, string key) =>
        decimal.TryParse(Value(row, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static int? IntValue(IReadOnlyDictionary<string, string> row, string key) =>
        decimal.TryParse(Value(row, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? decimal.ToInt32(decimal.Truncate(value)) : null;

    private static readonly IReadOnlyDictionary<string, string> HeaderTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["选择"]="Selected", ["停售"]="Discontinued", ["多款式"]="Multiple Variants", ["sku"]="SKU",
        ["商品编码"]="Product Code", ["商品名称"]="Product Name", ["Shop名称"]="Shop Name", ["简拼"]="Abbreviation",
        ["采购渠道"]="Procurement Channel", ["材质"]="Material", ["规格"]="Specification", ["成本价(RMB)"]="Cost (RMB)",
        ["最低售价($)"]="Minimum Sale Price (USD)", ["所有仓库库存数量"]="All Warehouse Inventory", ["样品数量"]="Sample Quantity",
        ["供应商名称"]="Supplier Name", ["原产国"]="Country of Origin", ["中文申报名"]="Chinese Declaration Name",
        ["英文申报名"]="English Declaration Name", ["申报价值(美元)"]="Declared Value (USD)", ["重量(克)"]="Weight (g)",
        ["图片路径"]="Image Path", ["业绩归属1"]="Performance Owner 1", ["业绩归属2"]="Performance Owner 2",
        ["描述"]="Description", ["最小包装数"]="Minimum Pack Quantity", ["零售价格(美元)"]="Retail Price (USD)",
        ["采购员"]="Buyer", ["单位"]="Unit", ["默认发货仓库"]="Default Shipping Warehouse", ["SKU商品状态"]="SKU Product Status",
        ["状态变动时间"]="Status Changed At", ["SKU名称"]="SKU Name", ["商品SKU"]="Product SKU", ["图片"]="Image",
        ["季节"]="Season", ["ASIN码"]="ASIN", ["UPC码"]="UPC", ["货号"]="Item Number", ["商品属性"]="Product Attributes",
        ["1688已关联"]="1688 Linked", ["是否带电"]="Contains Battery", ["最低采购单价"]="Minimum Purchase Unit Price",
        ["开发日期"]="Development Date", ["批发价格"]="Wholesale Price", ["款式"]="Style", ["备注"]="Notes",
        ["内包装成本"]="Inner Packaging Cost", ["包装规格"]="Packaging Specification", ["最高售价"]="Maximum Sale Price",
        ["SKU零售价格(美元)"]="SKU Retail Price (USD)", ["市场参考价"]="Market Reference Price",
        ["采购最小订货量"]="Minimum Order Quantity", ["海关编码"]="Customs Code", ["采购网址1"]="Procurement URL 1",
        ["采购网址2"]="Procurement URL 2", ["采购网址3"]="Procurement URL 3", ["外箱长(cm)"]="Outer Carton Length (cm)",
        ["外箱宽(cm)"]="Outer Carton Width (cm)", ["外箱高(cm)"]="Outer Carton Height (cm)", ["外箱毛重"]="Outer Carton Gross Weight",
        ["外箱净重"]="Outer Carton Net Weight", ["内盒长(cm)"]="Inner Box Length (cm)", ["内盒宽(cm)"]="Inner Box Width (cm)",
        ["内盒高(cm)"]="Inner Box Height (cm)", ["内盒毛重"]="Inner Box Gross Weight", ["内盒净重"]="Inner Box Net Weight",
        ["商品URL"]="Product URL", ["外包装成本"]="Outer Packaging Cost", ["责任归属人1"]="Responsible Owner 1",
        ["责任归属人2"]="Responsible Owner 2", ["店铺名称"]="Store Name", ["是否粉末"]="Is Powder", ["是否液体"]="Is Liquid",
        ["采购网址4"]="Procurement URL 4", ["采购网址5"]="Procurement URL 5", ["采购网址6"]="Procurement URL 6",
        ["是否带磁"]="Contains Magnet", ["包装规格重量G"]="Package Specification Weight (g)", ["停售时间"]="Discontinued At",
        ["停售原因"]="Discontinued Reason", ["包装难度系数"]="Packaging Difficulty Factor", ["运输方式"]="Shipping Method",
        ["头程运费"]="First-Leg Shipping Cost", ["产品使用到期日"]="Product Expiration Date", ["功能"]="Function",
        ["SKU需贴标数"]="SKU Labels Required", ["SKU贴标备注"]="SKU Label Notes", ["出口国申报价(美元)"]="Export Country Declared Price (USD)"
    };

    private static class XlsxRows
    {
        public static IEnumerable<IReadOnlyDictionary<string, string>> ReadFirstSheet(Stream stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var sharedStrings = ReadSharedStrings(archive);
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidOperationException("The workbook does not contain Sheet1.");
            using var sheetStream = sheet.Open();
            using var reader = XmlReader.Create(sheetStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
            string[]? headers = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;
                var values = ReadRow(reader.ReadSubtree(), sharedStrings);
                if (headers is null)
                {
                    headers = values;
                    continue;
                }
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < Math.Min(headers.Length, values.Length); index++)
                    if (!string.IsNullOrWhiteSpace(headers[index])) row[headers[index].Trim()] = values[index];
                yield return row;
            }
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry is null) return [];
            var result = new List<string>();
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si") continue;
                using var subtree = reader.ReadSubtree();
                var text = "";
                while (subtree.Read()) if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName == "t") text += subtree.ReadElementContentAsString();
                result.Add(text);
            }
            return result;
        }

        private static string[] ReadRow(XmlReader rowReader, IReadOnlyList<string> sharedStrings)
        {
            var cells = new Dictionary<int, string>();
            var maximum = -1;
            while (rowReader.Read())
            {
                if (rowReader.NodeType != XmlNodeType.Element || rowReader.LocalName != "c") continue;
                var reference = rowReader.GetAttribute("r") ?? "A1";
                var type = rowReader.GetAttribute("t");
                var column = ColumnIndex(reference);
                using var cellReader = rowReader.ReadSubtree();
                var raw = "";
                while (cellReader.Read())
                    if (cellReader.NodeType == XmlNodeType.Element && cellReader.LocalName is "v" or "t") raw += cellReader.ReadElementContentAsString();
                if (type == "s" && int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                    raw = sharedStrings[sharedIndex];
                cells[column] = raw;
                maximum = Math.Max(maximum, column);
            }
            var values = new string[maximum + 1];
            foreach (var (column, value) in cells) values[column] = value;
            return values;
        }

        private static int ColumnIndex(string reference)
        {
            var value = 0;
            foreach (var character in reference)
            {
                if (!char.IsLetter(character)) break;
                value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
            }
            return value - 1;
        }
    }
}
