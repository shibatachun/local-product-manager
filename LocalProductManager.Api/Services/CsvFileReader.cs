using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public static class CsvFileReader
{
    public static List<Dictionary<string, string>> Read(Stream stream)
    {
        using var parser = new TextFieldParser(stream)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()?
            .Select(header => header.Trim())
            .ToArray() ?? [];
        var rows = new List<Dictionary<string, string>>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                row[headers[index]] = index < fields.Length ? fields[index].Trim() : string.Empty;
            }
            rows.Add(row);
        }

        return rows;
    }
}
