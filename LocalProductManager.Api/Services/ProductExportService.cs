using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class ProductExportService(AppDbContext db)
{
    private static readonly string[] Headers =
    [
        "Product ID",
        "Local Inventory ID",
        "SKU",
        "Product Name",
        "Product Type",
        "SKU Base",
        "Parent Product ID",
        "Amazon Parent ASIN",
        "Category",
        "Cost",
        "First-mile Cost",
        "Sale Price",
        "Qty",
        "Weight",
        "Length",
        "Width",
        "Height",
        "Image Source",
        "Image Path",
        "Platforms",
        "Amazon URL",
        "Walmart URL",
        "eBay URL",
        "Etsy URL",
        "ASINs",
        "Parent ASINs",
        "Child ASINs",
        "Amazon Attribution Done",
        "Pinterest Attribution",
        "Facebook Attribution",
        "Instagram Attribution",
        "TikTok Attribution",
        "YouTube Attribution",
        "Shopify Attribution",
        "Created At",
        "Updated At"
    ];

    public async Task<byte[]> ExportProductsAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Include(product => product.Listings)
            .Include(product => product.AmazonAsins)
            .Include(product => product.AmazonAttributionLinks)
            .OrderBy(product => product.Sku)
            .ToListAsync(cancellationToken);

        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", ContentTypesXml());
            WriteEntry(archive, "_rels/.rels", RootRelationshipsXml());
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml());
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
            WriteEntry(archive, "xl/worksheets/sheet1.xml", SheetXml(products));
        }

        return stream.ToArray();
    }

    private static string SheetXml(IReadOnlyList<Product> products)
    {
        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        xml.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        xml.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        xml.Append("""<sheetFormatPr defaultRowHeight="18"/>""");
        xml.Append("""<cols>""");
        for (var index = 1; index <= Headers.Length; index++)
        {
            var width = index is 3 or 15 or >= 17 and <= 30 ? 32 : 16;
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{index}" max="{index}" width="{width}" customWidth="1"/>""");
        }
        xml.Append("""</cols><sheetData>""");
        xml.Append("""<row r="1">""");
        for (var column = 0; column < Headers.Length; column++)
        {
            AppendTextCell(xml, column, 1, Headers[column]);
        }
        xml.Append("""</row>""");

        for (var rowIndex = 0; rowIndex < products.Count; rowIndex++)
        {
            var product = products[rowIndex];
            var row = rowIndex + 2;
            var values = ProductRow(product);
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{row}">""");
            for (var column = 0; column < values.Length; column++)
            {
                AppendCell(xml, column, row, values[column]);
            }
            xml.Append("""</row>""");
        }

        xml.Append(CultureInfo.InvariantCulture, $"""</sheetData><autoFilter ref="A1:{ColumnName(Headers.Length - 1)}1"/>""");
        xml.Append("""</worksheet>""");
        return xml.ToString();
    }

    private static object?[] ProductRow(Product product)
    {
        var listingByPlatform = product.Listings
            .GroupBy(listing => listing.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().ListingUrl, StringComparer.OrdinalIgnoreCase);
        var attributionByPlatform = product.AmazonAttributionLinks
            .GroupBy(link => link.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().AttributionUrl, StringComparer.OrdinalIgnoreCase);
        var parentAsins = product.AmazonAsins
            .Where(asin => asin.AsinType.Equals("parent", StringComparison.OrdinalIgnoreCase))
            .Select(asin => asin.Asin)
            .Order()
            .ToArray();
        var childAsins = product.AmazonAsins
            .Where(asin => !asin.AsinType.Equals("parent", StringComparison.OrdinalIgnoreCase))
            .Select(asin => asin.Asin)
            .Order()
            .ToArray();

        return
        [
            product.Id,
            product.LocalInventoryId,
            product.Sku,
            product.ProductName,
            product.ProductType,
            product.SkuBase,
            product.ParentProductId,
            product.AmazonParentAsin,
            product.Category,
            product.Cost,
            product.OriginalCost,
            product.SalePrice,
            product.Qty,
            product.Weight,
            product.Length,
            product.Width,
            product.Height,
            product.ImageSource,
            product.ImagePath,
            string.Join(", ", product.Listings.Select(listing => listing.Platform).Order()),
            listingByPlatform.GetValueOrDefault("amazon"),
            listingByPlatform.GetValueOrDefault("walmart"),
            listingByPlatform.GetValueOrDefault("ebay"),
            listingByPlatform.GetValueOrDefault("etsy"),
            string.Join(", ", product.AmazonAsins.Select(asin => asin.Asin).Order()),
            string.Join(", ", parentAsins),
            string.Join(", ", childAsins),
            product.AmazonAsins.Any(asin => asin.AmazonAttributeDone) ? "Yes" : "No",
            attributionByPlatform.GetValueOrDefault("pinterest"),
            attributionByPlatform.GetValueOrDefault("facebook"),
            attributionByPlatform.GetValueOrDefault("instagram"),
            attributionByPlatform.GetValueOrDefault("tiktok"),
            attributionByPlatform.GetValueOrDefault("youtube"),
            attributionByPlatform.GetValueOrDefault("shopify"),
            product.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            product.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        ];
    }

    private static void AppendCell(StringBuilder xml, int columnIndex, int rowIndex, object? value)
    {
        switch (value)
        {
            case null:
                AppendTextCell(xml, columnIndex, rowIndex, "");
                return;
            case decimal decimalValue:
                AppendNumberCell(xml, columnIndex, rowIndex, decimalValue);
                return;
            case int intValue:
                AppendNumberCell(xml, columnIndex, rowIndex, intValue);
                return;
            default:
                AppendTextCell(xml, columnIndex, rowIndex, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                return;
        }
    }

    private static void AppendTextCell(StringBuilder xml, int columnIndex, int rowIndex, string value)
    {
        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{CellRef(columnIndex, rowIndex)}" t="inlineStr"><is><t""");
        if (value.Length != value.Trim().Length)
        {
            xml.Append(" xml:space=\"preserve\"");
        }
        xml.Append('>');
        xml.Append(XmlEscape(value));
        xml.Append("""</t></is></c>""");
    }

    private static void AppendNumberCell(StringBuilder xml, int columnIndex, int rowIndex, decimal value) =>
        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{CellRef(columnIndex, rowIndex)}"><v>{value}</v></c>""");

    private static string CellRef(int columnIndex, int rowIndex) => $"{ColumnName(columnIndex)}{rowIndex}";

    private static string ColumnName(int index)
    {
        var dividend = index + 1;
        var name = "";
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            dividend = (dividend - modulo) / 26;
        }

        return name;
    }

    private static string XmlEscape(string value) =>
        SecurityElementEscape(value);

    private static string SecurityElementEscape(string value)
    {
        using var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment });
        writer.WriteString(value);
        writer.Flush();
        return stringWriter.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static string ContentTypesXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string RootRelationshipsXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Products" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelationshipsXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;
}
