using System.Data;
using System.Data.Common;
using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LocalProductManager.Api.Services;

public static class ProductInventoryCategories
{
    public const string Unknown = "UNKNOWN";

    public static readonly string[] All =
    [
        "KEYCHAIN", "POM", "CHARM", "EYE", "KIT", "BEAD", "BROOCH", "CABOCHON", Unknown, "BAG", "PACKAGE"
    ];

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (All.Contains(normalized, StringComparer.Ordinal)) return normalized!;
        if (string.IsNullOrWhiteSpace(normalized)) return Unknown;

        return normalized switch
        {
            "KEYCHAINS" or "KEY CHAIN" or "KEY CHAINS" or "钥匙扣" => "KEYCHAIN",
            "POMS" or "POM POM" or "POM POMS" => "POM",
            "CHARMS" => "CHARM",
            "EYES" => "EYE",
            "KITS" => "KIT",
            "BEADS" => "BEAD",
            "BROOCHES" => "BROOCH",
            "CABOCHONS" => "CABOCHON",
            "BAGS" => "BAG",
            "PACK" or "PACKS" or "PACKAGING" or "PACKAGES" => "PACKAGE",
            _ => Unknown
        };
    }

    public static string BuildId(string category, int number) => $"{Normalize(category)}-{number:D3}";
}

public static class ProductInventoryCategorySchema
{
    public static async Task UpgradeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var columns = await GetColumnsAsync(db, "products", cancellationToken);
        if (!columns.Contains("InventoryCategory"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE products ADD COLUMN \"InventoryCategory\" character varying(16) NOT NULL DEFAULT 'UNKNOWN'",
                cancellationToken);
        }
        if (!columns.Contains("CategoryInventoryId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE products ADD COLUMN \"CategoryInventoryId\" character varying(32) NULL",
                cancellationToken);
        }
        if (!columns.Contains("ReadyToList"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE products ADD COLUMN \"ReadyToList\" boolean NOT NULL DEFAULT false",
                cancellationToken);
        }
        if (!columns.Contains("Note"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE products ADD COLUMN \"Note\" character varying(4000) NULL",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            """
                  CREATE TABLE IF NOT EXISTS product_category_sequences (
                      "Category" character varying(16) PRIMARY KEY,
                      "LastNumber" integer NOT NULL
                  )
                  """,
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_products_CategoryInventoryId\" ON products (\"CategoryInventoryId\")",
            cancellationToken);

        var missing = await db.Products
            .Where(product => product.CategoryInventoryId == null || product.CategoryInventoryId == "")
            .OrderBy(product => product.Id)
            .ToListAsync(cancellationToken);
        if (missing.Count == 0) return;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        foreach (var product in missing)
        {
            var sourceCategory = product.InventoryCategory == ProductInventoryCategories.Unknown
                ? product.Category
                : product.InventoryCategory;
            product.InventoryCategory = ProductInventoryCategories.Normalize(sourceCategory);
            product.CategoryInventoryId = ProductInventoryCategories.BuildId(
                product.InventoryCategory,
                await NextNumberAsync(db, product.InventoryCategory, cancellationToken));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task<int> NextNumberAsync(
        AppDbContext db,
        string category,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
              INSERT INTO product_category_sequences ("Category", "LastNumber") VALUES (@category, 1)
              ON CONFLICT ("Category") DO UPDATE
              SET "LastNumber" = product_category_sequences."LastNumber" + 1
              RETURNING "LastNumber"
              """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@category";
        parameter.Value = ProductInventoryCategories.Normalize(category);
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        AppDbContext db,
        string table,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name=@table";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(0));
        return columns;
    }
}
