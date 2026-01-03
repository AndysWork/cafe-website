using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    // Note: _frozenItems collection is initialized in main MongoService constructor

    // ===== FROZEN ITEMS CRUD =====

    public async Task<List<FrozenItem>> GetAllFrozenItemsAsync()
    {
        return await _frozenItems.Find(_ => true).ToListAsync();
    }

    public async Task<List<FrozenItem>> GetActiveFrozenItemsAsync()
    {
        return await _frozenItems.Find(item => item.IsActive).ToListAsync();
    }

    public async Task<FrozenItem?> GetFrozenItemByIdAsync(string id)
    {
        return await _frozenItems.Find(item => item.Id == id).FirstOrDefaultAsync();
    }

    public async Task<FrozenItem> CreateFrozenItemAsync(FrozenItem frozenItem)
    {
        frozenItem.CreatedAt = DateTime.UtcNow;
        frozenItem.IsActive = true;
        frozenItem.Category = "frozen";
        await _frozenItems.InsertOneAsync(frozenItem);

        // Sync to inventory
        await SyncFrozenItemToInventoryAsync(frozenItem);

        return frozenItem;
    }

    public async Task<bool> UpdateFrozenItemAsync(string id, FrozenItem frozenItem)
    {
        frozenItem.UpdatedAt = DateTime.UtcNow;
        var result = await _frozenItems.ReplaceOneAsync(item => item.Id == id, frozenItem);

        if (result.ModifiedCount > 0)
        {
            // Sync to inventory
            await SyncFrozenItemToInventoryAsync(frozenItem);
        }

        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteFrozenItemAsync(string id)
    {
        var frozenItem = await GetFrozenItemByIdAsync(id);
        var result = await _frozenItems.DeleteOneAsync(item => item.Id == id);

        if (result.DeletedCount > 0 && frozenItem != null)
        {
            // Remove from inventory or mark as inactive
            var inventoryItem = await _inventory
                .Find(inv => inv.IngredientId == id && inv.Category == "frozen")
                .FirstOrDefaultAsync();

            if (inventoryItem != null)
            {
                inventoryItem.IsActive = false;
                inventoryItem.UpdatedAt = DateTime.UtcNow;
                await _inventory.ReplaceOneAsync(inv => inv.Id == inventoryItem.Id, inventoryItem);
            }
        }

        return result.DeletedCount > 0;
    }

    // ===== SYNC TO INVENTORY =====

    private async Task SyncFrozenItemToInventoryAsync(FrozenItem frozenItem)
    {
        // Check if inventory item already exists for this frozen item
        var existingInventory = await _inventory
            .Find(inv => inv.IngredientId == frozenItem.Id && inv.Category == "frozen")
            .FirstOrDefaultAsync();

        // Calculate per piece price: Buy Price / Quantity
        decimal perPiecePrice = frozenItem.Quantity > 0 ? frozenItem.BuyPrice / frozenItem.Quantity : 0;

        if (existingInventory != null)
        {
            // Update existing inventory
            existingInventory.IngredientName = frozenItem.ItemName;
            existingInventory.CurrentStock = frozenItem.Quantity; // Store quantity in pieces
            existingInventory.Unit = "pc"; // pieces
            existingInventory.SupplierName = frozenItem.Vendor;
            existingInventory.LastPurchasePrice = frozenItem.BuyPrice;
            existingInventory.LastPurchaseDate = DateTime.UtcNow;
            existingInventory.CostPerUnit = perPiecePrice; // Cost per piece
            existingInventory.TotalValue = frozenItem.BuyPrice;
            existingInventory.LastRestockDate = DateTime.UtcNow;
            existingInventory.IsActive = frozenItem.IsActive;
            existingInventory.UpdatedAt = DateTime.UtcNow;
            existingInventory.Notes = $"Packet Weight: {frozenItem.PacketWeight}kg, Per Piece Price: ₹{perPiecePrice:F2}, Per Piece Weight: {frozenItem.PerPieceWeight}gm";

            // Update stock status
            existingInventory.Status = existingInventory.CurrentStock <= existingInventory.MinimumStock
                ? (existingInventory.CurrentStock == 0 ? StockStatus.OutOfStock : StockStatus.LowStock)
                : StockStatus.InStock;

            await _inventory.ReplaceOneAsync(inv => inv.Id == existingInventory.Id, existingInventory);
        }
        else
        {
            // Create new inventory entry
            var newInventory = new Inventory
            {
                IngredientId = frozenItem.Id,
                IngredientName = frozenItem.ItemName,
                Category = "frozen",
                Unit = "pc", // pieces
                CurrentStock = frozenItem.Quantity, // Number of pieces
                MinimumStock = 10, // Minimum 10 pieces
                MaximumStock = frozenItem.Quantity * 2, // 2x current stock
                ReorderQuantity = 50, // 50 pieces
                SupplierName = frozenItem.Vendor,
                LastPurchasePrice = frozenItem.BuyPrice,
                LastPurchaseDate = DateTime.UtcNow,
                CostPerUnit = perPiecePrice, // Cost per piece
                TotalValue = frozenItem.BuyPrice,
                Status = StockStatus.InStock,
                IsActive = frozenItem.IsActive,
                LastRestockDate = DateTime.UtcNow,
                Notes = $"Packet Weight: {frozenItem.PacketWeight}kg, Per Piece Price: ₹{perPiecePrice:F2}, Per Piece Weight: {frozenItem.PerPieceWeight}gm",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _inventory.InsertOneAsync(newInventory);
        }
    }

    // ===== BULK UPLOAD FROM EXCEL =====

    public async Task<(int success, int failed, List<string> errors)> BulkUploadFrozenItemsAsync(List<FrozenItemUpload> items)
    {
        int successCount = 0;
        int failedCount = 0;
        List<string> errors = new List<string>();

        foreach (var item in items)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(item.ItemName))
                {
                    errors.Add($"Row skipped: Item name is required");
                    failedCount++;
                    continue;
                }

                if (item.Quantity <= 0 || item.PacketWeight <= 0 || item.BuyPrice <= 0)
                {
                    errors.Add($"Row '{item.ItemName}' skipped: Invalid quantity, weight, or price");
                    failedCount++;
                    continue;
                }

                // Check if item already exists
                var existingItem = await _frozenItems
                    .Find(f => f.ItemName.ToLower() == item.ItemName.ToLower() && f.Vendor.ToLower() == item.Vendor.ToLower())
                    .FirstOrDefaultAsync();

                if (existingItem != null)
                {
                    // Update existing item - replace values with new data
                    existingItem.Quantity = item.Quantity;
                    existingItem.PacketWeight = item.PacketWeight;
                    existingItem.BuyPrice = item.BuyPrice;
                    existingItem.PerPiecePrice = item.PerPiecePrice;
                    existingItem.PerPieceWeight = item.PerPieceWeight;
                    existingItem.UpdatedAt = DateTime.UtcNow;

                    await _frozenItems.ReplaceOneAsync(f => f.Id == existingItem.Id, existingItem);
                    
                    // Sync to inventory
                    await SyncFrozenItemToInventoryAsync(existingItem);
                }
                else
                {
                    // Create new item
                    var newItem = new FrozenItem
                    {
                        ItemName = item.ItemName,
                        Quantity = item.Quantity,
                        PacketWeight = item.PacketWeight,
                        BuyPrice = item.BuyPrice,
                        PerPiecePrice = item.PerPiecePrice,
                        PerPieceWeight = item.PerPieceWeight,
                        Vendor = item.Vendor,
                        Category = "frozen",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _frozenItems.InsertOneAsync(newItem);
                    
                    // Sync to inventory
                    await SyncFrozenItemToInventoryAsync(newItem);
                }

                successCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing '{item.ItemName}': {ex.Message}");
                failedCount++;
            }
        }

        return (successCount, failedCount, errors);
    }

    // ===== SYNC ALL FROZEN ITEMS TO INVENTORY =====

    public async Task<int> SyncAllFrozenItemsToInventoryAsync()
    {
        var allFrozenItems = await GetAllFrozenItemsAsync();
        int syncedCount = 0;

        foreach (var item in allFrozenItems)
        {
            try
            {
                await SyncFrozenItemToInventoryAsync(item);
                syncedCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing {item.ItemName} to inventory: {ex.Message}");
            }
        }

        return syncedCount;
    }
}
