using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public sealed class InventoryController(AppDbContext db, AuthService auth) : ControllerBase
{
    [HttpGet("warehouse")]
    public async Task<ActionResult<WarehouseDto?>> GetWarehouse(CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.AsNoTracking()
            .Include(item => item.Shelves)
                .ThenInclude(shelf => shelf.Levels)
                    .ThenInclude(level => level.Inventory)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return warehouse is null ? NoContent() : Ok(ToDto(warehouse));
    }

    [HttpPost("warehouses")]
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse(
        CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var next = (await db.Warehouses.MaxAsync(item => (int?)item.Id, cancellationToken) ?? 0) + 1;
        var warehouse = new Warehouse
        {
            Code = $"WH{next:00}",
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"Warehouse {next}" : request.Name.Trim()
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(warehouse));
    }

    [HttpPost("warehouses/{warehouseId:int}/shelves")]
    public async Task<ActionResult<WarehouseShelfDto>> CreateShelf(
        int warehouseId, CreateShelfRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Only administrators can add shelves.");

        var warehouse = await db.Warehouses.FindAsync([warehouseId], cancellationToken);
        if (warehouse is null) return NotFound();

        var zone = string.IsNullOrWhiteSpace(request.ZoneCode)
            ? "A"
            : request.ZoneCode.Trim().ToUpperInvariant()[..1];
        var aisle = Math.Clamp(request.AisleNumber, 1, 99);
        var levelCount = Math.Clamp(request.LevelCount, 1, 20);
        var rackNumber = (await db.WarehouseShelves
            .Where(shelf => shelf.WarehouseId == warehouseId && shelf.ZoneCode == zone && shelf.AisleNumber == aisle)
            .MaxAsync(shelf => (int?)shelf.RackNumber, cancellationToken) ?? 0) + 1;
        var code = $"{warehouse.Code}-{zone}-{aisle:00}-R{rackNumber:000}";
        var shelf = new WarehouseShelf
        {
            WarehouseId = warehouseId,
            Code = code,
            ZoneCode = zone,
            AisleNumber = aisle,
            RackNumber = rackNumber,
            LevelCount = levelCount,
            X = Math.Clamp(request.X, 0, 82),
            Y = Math.Clamp(request.Y, 0, 88),
            Levels = Enumerable.Range(1, levelCount)
                .Select(number => new ShelfLevel { LevelNumber = number, Code = $"{code}-L{number:00}" })
                .ToList()
        };
        db.WarehouseShelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(shelf));
    }

    [HttpGet("shelves/{shelfId:int}")]
    public async Task<ActionResult<ShelfDetailDto>> GetShelf(int shelfId, CancellationToken cancellationToken)
    {
        var shelf = await db.WarehouseShelves.AsNoTracking()
            .Include(item => item.Levels)
                .ThenInclude(level => level.Inventory)
                    .ThenInclude(inventory => inventory.Product)
            .FirstOrDefaultAsync(item => item.Id == shelfId, cancellationToken);
        return shelf is null ? NotFound() : Ok(ToDetailDto(shelf));
    }

    [HttpPost("shelves/{shelfId:int}/levels")]
    public async Task<ActionResult<ShelfDetailDto>> AddShelfLevel(int shelfId, CancellationToken cancellationToken)
    {
        var shelf = await db.WarehouseShelves
            .Include(item => item.Levels)
                .ThenInclude(level => level.Inventory)
                    .ThenInclude(inventory => inventory.Product)
            .FirstOrDefaultAsync(item => item.Id == shelfId, cancellationToken);
        if (shelf is null) return NotFound();
        if (shelf.Levels.Count >= 20) return Conflict("A shelf cannot have more than 20 levels.");

        var nextNumber = (shelf.Levels.Max(level => (int?)level.LevelNumber) ?? 0) + 1;
        shelf.Levels.Add(new ShelfLevel
        {
            LevelNumber = nextNumber,
            Code = $"{shelf.Code}-L{nextNumber:00}"
        });
        shelf.LevelCount = shelf.Levels.Count;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailDto(shelf));
    }

    [HttpDelete("levels/{levelId:int}")]
    public async Task<ActionResult<ShelfDetailDto>> DeleteShelfLevel(int levelId, CancellationToken cancellationToken)
    {
        var level = await db.ShelfLevels
            .Include(item => item.Inventory)
            .Include(item => item.WarehouseShelf!)
                .ThenInclude(shelf => shelf.Levels)
                    .ThenInclude(item => item.Inventory)
                        .ThenInclude(inventory => inventory.Product)
            .FirstOrDefaultAsync(item => item.Id == levelId, cancellationToken);
        if (level is null || level.WarehouseShelf is null) return NotFound();
        if (level.Inventory.Any(item => item.Qty > 0))
            return Conflict("This level still contains inventory. Clear it or move it to another location before deleting the level.");

        var shelf = level.WarehouseShelf;
        db.ShelfLevels.Remove(level);
        shelf.LevelCount = Math.Max(0, shelf.Levels.Count - 1);
        await db.SaveChangesAsync(cancellationToken);

        var updated = await db.WarehouseShelves.AsNoTracking()
            .Include(item => item.Levels)
                .ThenInclude(item => item.Inventory)
                    .ThenInclude(inventory => inventory.Product)
            .FirstAsync(item => item.Id == shelf.Id, cancellationToken);
        return Ok(ToDetailDto(updated));
    }

    [HttpDelete("shelves/{shelfId:int}")]
    public async Task<IActionResult> DeleteShelf(int shelfId, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Only administrators can delete shelves.");

        var shelf = await db.WarehouseShelves
            .Include(item => item.Levels)
                .ThenInclude(level => level.Inventory)
            .FirstOrDefaultAsync(item => item.Id == shelfId, cancellationToken);
        if (shelf is null) return NotFound();
        if (shelf.Levels.SelectMany(level => level.Inventory).Any(item => item.Qty > 0))
            return Conflict("This shelf still contains inventory. Clear it or move it to another shelf before deleting the shelf.");

        db.WarehouseShelves.Remove(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("shelves/{shelfId:int}/layout")]
    public async Task<ActionResult<WarehouseShelfDto>> UpdateShelfLayout(
        int shelfId, UpdateShelfLayoutRequest request, CancellationToken cancellationToken)
    {
        var shelf = await db.WarehouseShelves
            .Include(item => item.Levels).ThenInclude(level => level.Inventory)
            .FirstOrDefaultAsync(item => item.Id == shelfId, cancellationToken);
        if (shelf is null) return NotFound();

        shelf.Rotation = request.Rotation % 180 == 90 ? 90 : 0;
        var effectiveWidth = shelf.Rotation == 90 ? shelf.Height : shelf.Width;
        var effectiveHeight = shelf.Rotation == 90 ? shelf.Width : shelf.Height;
        shelf.X = Math.Clamp(request.X, 0, 100 - effectiveWidth);
        shelf.Y = Math.Clamp(request.Y, 0, 100 - effectiveHeight);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(shelf));
    }

    [HttpPut("levels/{levelId:int}/inventory")]
    public async Task<ActionResult<ShelfLevelDto>> SetLevelInventory(
        int levelId, SetLevelInventoryRequest request, CancellationToken cancellationToken)
    {
        var level = await db.ShelfLevels.FindAsync([levelId], cancellationToken);
        if (level is null || !await db.Products.AnyAsync(item => item.Id == request.ProductId, cancellationToken))
            return NotFound();

        if (request.BinNumber is < 1 or > 2)
            return BadRequest("Bin number must be 1 or 2.");

        var row = await db.ShelfLevelInventories.FirstOrDefaultAsync(
            item => item.ShelfLevelId == levelId
                && item.BinNumber == request.BinNumber
                && item.ProductId == request.ProductId,
            cancellationToken);
        var qty = Math.Max(0, request.Qty);
        if (row is null && qty > 0)
        {
            db.ShelfLevelInventories.Add(new ShelfLevelInventory
                {
                    ShelfLevelId = levelId,
                    BinNumber = request.BinNumber,
                    ProductId = request.ProductId,
                    Qty = qty
                });
        }
        else if (row is not null && qty == 0)
        {
            db.ShelfLevelInventories.Remove(row);
        }
        else if (row is not null)
        {
            row.Qty = qty;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        var product = await db.Products.FindAsync([request.ProductId], cancellationToken);
        if (product is not null)
        {
            product.Qty = await db.ShelfLevelInventories
                .Where(item => item.ProductId == request.ProductId)
                .SumAsync(item => item.Qty, cancellationToken);
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var updated = await db.ShelfLevels.AsNoTracking()
            .Include(item => item.Inventory).ThenInclude(item => item.Product)
            .FirstAsync(item => item.Id == levelId, cancellationToken);
        return Ok(ToDto(updated));
    }

    private static WarehouseDto ToDto(Warehouse warehouse) => new(
        warehouse.Id, warehouse.Code, warehouse.Name,
        warehouse.Shelves.OrderBy(item => item.Code).Select(ToDto).ToArray());

    private static WarehouseShelfDto ToDto(WarehouseShelf shelf) => new(
        shelf.Id, shelf.Code, shelf.ZoneCode, shelf.AisleNumber, shelf.RackNumber, shelf.LevelCount,
        shelf.X, shelf.Y, shelf.Width, shelf.Height, shelf.Rotation,
        shelf.Levels.SelectMany(level => level.Inventory).Sum(item => item.Qty));

    private static ShelfDetailDto ToDetailDto(WarehouseShelf shelf) => new(
        shelf.Id, shelf.Code, shelf.LevelCount,
        shelf.Levels.OrderByDescending(item => item.LevelNumber).Select(ToDto).ToArray());

    private static ShelfLevelDto ToDto(ShelfLevel level) => new(
        level.Id, level.Code, level.LevelNumber, level.Inventory.Sum(item => item.Qty),
        level.Inventory.OrderBy(item => item.BinNumber).ThenBy(item => item.Product!.Sku)
            .Select(item => new ShelfInventoryItemDto(
                item.ProductId, item.Product!.Sku, item.Product.ProductName, item.Qty, item.BinNumber))
            .ToArray());
}
