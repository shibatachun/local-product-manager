using System.IO.Compression;
using System.Xml.Linq;

namespace LocalProductManager.Api.Services;

public static class SimpleXlsxReader
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<List<string>> ReadFirstSheet(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetPath = ResolveFirstWorksheetPath(archive);
        var entry = archive.GetEntry(sheetPath) ??
            throw new InvalidOperationException("The Excel workbook does not contain a readable worksheet.");

        using var sheetStream = entry.Open();
        var document = XDocument.Load(sheetStream);
        var rows = new List<List<string>>();
        foreach (var row in document.Descendants(MainNs + "row"))
        {
            var values = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(MainNs + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? "";
                var columnIndex = ColumnIndex(reference);
                if (columnIndex < 0) continue;
                values[columnIndex] = ReadCellValue(cell, sharedStrings);
            }

            if (values.Count == 0)
            {
                rows.Add([]);
                continue;
            }

            var max = values.Keys.Max();
            var rowValues = new List<string>();
            for (var index = 0; index <= max; index++)
            {
                rowValues.Add(values.GetValueOrDefault(index, ""));
            }
            rows.Add(rowValues);
        }

        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Descendants(MainNs + "si")
            .Select(item => string.Concat(item.Descendants(MainNs + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ??
            throw new InvalidOperationException("The Excel workbook is missing workbook.xml.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ??
            throw new InvalidOperationException("The Excel workbook is missing worksheet relationships.");

        using var workbookStream = workbookEntry.Open();
        using var relsStream = relsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var rels = XDocument.Load(relsStream);

        var firstSheetRid = workbook
            .Descendants(MainNs + "sheet")
            .Select(sheet => sheet.Attribute(RelationshipNs + "id")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(firstSheetRid))
        {
            throw new InvalidOperationException("The Excel workbook does not list any worksheets.");
        }

        var target = rels
            .Descendants(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(rel => rel.Attribute("Id")?.Value == firstSheetRid)
            ?.Attribute("Target")
            ?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("The first worksheet relationship could not be resolved.");
        }

        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target.TrimStart('/')}";
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(MainNs + "t").Select(text => text.Value)).Trim();
        }

        var raw = cell.Element(MainNs + "v")?.Value ?? "";
        if (type == "s" && int.TryParse(raw, out var sharedIndex))
        {
            return sharedIndex >= 0 && sharedIndex < sharedStrings.Count
                ? sharedStrings[sharedIndex].Trim()
                : "";
        }

        return raw.Trim();
    }

    private static int ColumnIndex(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        if (letters.Length == 0) return -1;

        var index = 0;
        foreach (var letter in letters)
        {
            index = index * 26 + (letter - 'A' + 1);
        }

        return index - 1;
    }
}
