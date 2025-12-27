// Inventory Management Methods Extension for MongoService
using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    #region Inventory Management

    // ==== INVENTORY CRUD ====

    public async Task<List<Inventory>> GetAllInventoryAsync()
    {
        return await _inventory.Find(_ => true).ToListAsync();
    }

    public async Task<List<Inventory>> GetActiveInventoryAsync()
    {
        return await _inventory.Find(i => i.IsActive).ToListAsync();
    }

    public async Task<Inventory?> GetInventoryByIdAsync(string id)
    {
        return await _inventory.Find(i => i.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Inventory?> GetInventoryByIngredientIdAsync(string ingredientId)
    {
        return await _inventory.Find(i => i.IngredientId == ingredientId).FirstOrDefaultAsync();
    }

    public async Task<List<Inventory>> GetInventoryByCategoryAsync(string category)
    {
        return await _inventory.Find(i => i.Category == category && i.IsActive).ToListAsync();
    }

    public async Task<List<Inventory>> GetInventoryByStatusAsync(StockStatus status)
    {
        return await _inventory.Find(i => i.Status == status && i.IsActive).ToListAsync();
    }

    public async Task<List<Inventory>> GetLowStockItemsAsync()
    {
        var filter = Builders<Inventory>.Filter.Where(i =>
            i.IsActive &&
            i.CurrentStock <= i.MinimumStock &&
            i.CurrentStock > 0);
        return await _inventory.Find(filter).ToListAsync();
    }

    public async Task<List<Inventory>> GetOutOfStockItemsAsync()
    {
        return await _inventory.Find(i => i.CurrentStock == 0 && i.IsActive).ToListAsync();
    }

    public async Task<List<Inventory>> GetExpiringItemsAsync(int daysThreshold = 7)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);
        var filter = Builders<Inventory>.Filter.Where(i =>
            i.IsActive &&
            i.ExpiryDate != null &&
            i.ExpiryDate <= thresholdDate);
        return await _inventory.Find(filter).ToListAsync();
    }

    public async Task<Inventory> CreateInventoryAsync(Inventory inventory)
    {
        inventory.CreatedAt = DateTime.UtcNow;
        inventory.UpdatedAt = DateTime.UtcNow;
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        await _inventory.InsertOneAsync(inventory);
        return inventory;
    }

    public async Task<bool> UpdateInventoryAsync(string id, Inventory inventory)
    {
        inventory.UpdatedAt = DateTime.UtcNow;
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        var result = await _inventory.ReplaceOneAsync(i => i.Id == id, inventory);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteInventoryAsync(string id)
    {
        var result = await _inventory.DeleteOneAsync(i => i.Id == id);
        return result.DeletedCount > 0;
    }

    // ==== STOCK OPERATIONS ====

    public async Task<bool> AdjustStockAsync(string inventoryId, decimal quantityChange, TransactionType type, string reason, string performedBy, string? referenceNumber = null)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null) return false;

        var stockBefore = inventory.CurrentStock;
        var stockAfter = stockBefore + quantityChange;

        if (stockAfter < 0) return false; // Cannot have negative stock

        // Create transaction record
        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = type,
            Quantity = Math.Abs(quantityChange),
            Unit = inventory.Unit,
            StockBefore = stockBefore,
            StockAfter = stockAfter,
            Reason = reason,
            ReferenceNumber = referenceNumber,
            PerformedBy = performedBy,
            TransactionDate = DateTime.UtcNow
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory
        inventory.CurrentStock = stockAfter;
        inventory.UpdatedAt = DateTime.UtcNow;
        inventory.LastUpdatedBy = performedBy;
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        if (type == TransactionType.StockIn)
        {
            inventory.LastRestockDate = DateTime.UtcNow;
        }

        var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

        // Check and create alerts
        await CheckAndCreateAlertsAsync(inventory);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> StockInAsync(string inventoryId, decimal quantity, decimal? costPerUnit, string? supplierName, string? referenceNumber, string performedBy)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null) return false;

        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = TransactionType.StockIn,
            Quantity = quantity,
            Unit = inventory.Unit,
            CostPerUnit = costPerUnit,
            TotalCost = costPerUnit.HasValue ? quantity * costPerUnit.Value : null,
            StockBefore = inventory.CurrentStock,
            StockAfter = inventory.CurrentStock + quantity,
            SupplierName = supplierName,
            ReferenceNumber = referenceNumber,
            Reason = "Stock purchase/receipt",
            PerformedBy = performedBy,
            TransactionDate = DateTime.UtcNow
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory
        inventory.CurrentStock += quantity;
        
        if (costPerUnit.HasValue)
        {
            // Update cost per unit with weighted average
            decimal totalCost = (inventory.CurrentStock - quantity) * inventory.CostPerUnit + quantity * costPerUnit.Value;
            inventory.CostPerUnit = totalCost / inventory.CurrentStock;
            inventory.LastPurchasePrice = costPerUnit.Value;
        }

        inventory.LastPurchaseDate = DateTime.UtcNow;
        inventory.LastRestockDate = DateTime.UtcNow;
        
        if (!string.IsNullOrEmpty(supplierName))
        {
            inventory.SupplierName = supplierName;
        }

        inventory.UpdatedAt = DateTime.UtcNow;
        inventory.LastUpdatedBy = performedBy;
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

        // Resolve low stock/out of stock alerts
        await ResolveAlertsAsync(inventoryId, new[] { AlertType.LowStock, AlertType.OutOfStock }, performedBy);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> StockOutAsync(string inventoryId, decimal quantity, string reason, string performedBy)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null || inventory.CurrentStock < quantity) return false;

        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = TransactionType.StockOut,
            Quantity = quantity,
            Unit = inventory.Unit,
            StockBefore = inventory.CurrentStock,
            StockAfter = inventory.CurrentStock - quantity,
            Reason = reason,
            PerformedBy = performedBy,
            TransactionDate = DateTime.UtcNow
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory
        inventory.CurrentStock -= quantity;
        inventory.UpdatedAt = DateTime.UtcNow;
        inventory.LastUpdatedBy = performedBy;
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

        // Check for low stock
        await CheckAndCreateAlertsAsync(inventory);

        return result.ModifiedCount > 0;
    }

    // ==== TRANSACTIONS ====

    public async Task<List<InventoryTransaction>> GetAllInventoryTransactionsAsync()
    {
        return await _inventoryTransactions.Find(_ => true)
            .SortByDescending(t => t.TransactionDate)
            .ToListAsync();
    }

    public async Task<List<InventoryTransaction>> GetTransactionsByInventoryIdAsync(string inventoryId, int limit = 50)
    {
        return await _inventoryTransactions.Find(t => t.InventoryId == inventoryId)
            .SortByDescending(t => t.TransactionDate)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<InventoryTransaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _inventoryTransactions.Find(t =>
            t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .SortByDescending(t => t.TransactionDate)
            .ToListAsync();
    }

    public async Task<List<InventoryTransaction>> GetRecentTransactionsAsync(int limit = 20)
    {
        return await _inventoryTransactions.Find(_ => true)
            .SortByDescending(t => t.TransactionDate)
            .Limit(limit)
            .ToListAsync();
    }

    // ==== ALERTS ====

    public async Task<List<StockAlert>> GetAllAlertsAsync()
    {
        return await _stockAlerts.Find(a => !a.IsResolved)
            .SortByDescending(a => a.Severity)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<StockAlert>> GetAlertsByTypeAsync(AlertType type)
    {
        return await _stockAlerts.Find(a => a.Type == type && !a.IsResolved)
            .SortByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<StockAlert>> GetCriticalAlertsAsync()
    {
        return await _stockAlerts.Find(a => a.Severity == AlertSeverity.Critical && !a.IsResolved)
            .SortByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> ResolveAlertAsync(string alertId, string resolvedBy)
    {
        var update = Builders<StockAlert>.Update
            .Set(a => a.IsResolved, true)
            .Set(a => a.ResolvedAt, DateTime.UtcNow)
            .Set(a => a.ResolvedBy, resolvedBy);

        var result = await _stockAlerts.UpdateOneAsync(a => a.Id == alertId, update);
        return result.ModifiedCount > 0;
    }

    private async Task ResolveAlertsAsync(string inventoryId, AlertType[] alertTypes, string resolvedBy)
    {
        var filter = Builders<StockAlert>.Filter.And(
            Builders<StockAlert>.Filter.Eq(a => a.InventoryId, inventoryId),
            Builders<StockAlert>.Filter.In(a => a.Type, alertTypes),
            Builders<StockAlert>.Filter.Eq(a => a.IsResolved, false)
        );

        var update = Builders<StockAlert>.Update
            .Set(a => a.IsResolved, true)
            .Set(a => a.ResolvedAt, DateTime.UtcNow)
            .Set(a => a.ResolvedBy, resolvedBy);

        await _stockAlerts.UpdateManyAsync(filter, update);
    }

    private async Task CheckAndCreateAlertsAsync(Inventory inventory)
    {
        if (inventory.Id == null) return;

        // Check for low stock
        if (inventory.CurrentStock <= inventory.MinimumStock && inventory.CurrentStock > 0)
        {
            var existingAlert = await _stockAlerts.Find(a =>
                a.InventoryId == inventory.Id &&
                a.Type == AlertType.LowStock &&
                !a.IsResolved).FirstOrDefaultAsync();

            if (existingAlert == null)
            {
                await _stockAlerts.InsertOneAsync(new StockAlert
                {
                    InventoryId = inventory.Id,
                    IngredientName = inventory.IngredientName,
                    Type = AlertType.LowStock,
                    Severity = AlertSeverity.Warning,
                    Message = $"{inventory.IngredientName} is running low. Current: {inventory.CurrentStock}{inventory.Unit}, Minimum: {inventory.MinimumStock}{inventory.Unit}",
                    CurrentStock = inventory.CurrentStock,
                    ThresholdValue = inventory.MinimumStock,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // Check for out of stock
        if (inventory.CurrentStock == 0)
        {
            var existingAlert = await _stockAlerts.Find(a =>
                a.InventoryId == inventory.Id &&
                a.Type == AlertType.OutOfStock &&
                !a.IsResolved).FirstOrDefaultAsync();

            if (existingAlert == null)
            {
                await _stockAlerts.InsertOneAsync(new StockAlert
                {
                    InventoryId = inventory.Id,
                    IngredientName = inventory.IngredientName,
                    Type = AlertType.OutOfStock,
                    Severity = AlertSeverity.Critical,
                    Message = $"{inventory.IngredientName} is OUT OF STOCK!",
                    CurrentStock = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // Check for expiring stock
        if (inventory.ExpiryDate.HasValue)
        {
            var daysUntilExpiry = (inventory.ExpiryDate.Value - DateTime.UtcNow).Days;
            
            if (daysUntilExpiry <= 7 && daysUntilExpiry > 0)
            {
                var existingAlert = await _stockAlerts.Find(a =>
                    a.InventoryId == inventory.Id &&
                    a.Type == AlertType.ExpiringStock &&
                    !a.IsResolved).FirstOrDefaultAsync();

                if (existingAlert == null)
                {
                    await _stockAlerts.InsertOneAsync(new StockAlert
                    {
                        InventoryId = inventory.Id,
                        IngredientName = inventory.IngredientName,
                        Type = AlertType.ExpiringStock,
                        Severity = daysUntilExpiry <= 3 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        Message = $"{inventory.IngredientName} expires in {daysUntilExpiry} days",
                        CurrentStock = inventory.CurrentStock,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (daysUntilExpiry <= 0)
            {
                var existingAlert = await _stockAlerts.Find(a =>
                    a.InventoryId == inventory.Id &&
                    a.Type == AlertType.ExpiredStock &&
                    !a.IsResolved).FirstOrDefaultAsync();

                if (existingAlert == null)
                {
                    await _stockAlerts.InsertOneAsync(new StockAlert
                    {
                        InventoryId = inventory.Id,
                        IngredientName = inventory.IngredientName,
                        Type = AlertType.ExpiredStock,
                        Severity = AlertSeverity.Critical,
                        Message = $"{inventory.IngredientName} has EXPIRED!",
                        CurrentStock = inventory.CurrentStock,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }
    }

    // ==== REPORTS ====

    public async Task<InventoryReport> GetInventoryReportAsync()
    {
        var allInventory = await GetActiveInventoryAsync();

        var report = new InventoryReport
        {
            TotalItems = allInventory.Count,
            InStockItems = allInventory.Count(i => i.Status == StockStatus.InStock),
            LowStockItems = allInventory.Count(i => i.Status == StockStatus.LowStock),
            OutOfStockItems = allInventory.Count(i => i.Status == StockStatus.OutOfStock),
            ExpiringItems = allInventory.Count(i => i.Status == StockStatus.Expiring),
            TotalInventoryValue = allInventory.Sum(i => i.TotalValue),
            AverageCostPerItem = allInventory.Any() ? allInventory.Average(i => i.TotalValue) : 0,
            TopValueItems = allInventory.OrderByDescending(i => i.TotalValue)
                .Take(10)
                .Select(i => new InventoryItem
                {
                    Id = i.Id,
                    Name = i.IngredientName,
                    Category = i.Category,
                    CurrentStock = i.CurrentStock,
                    Unit = i.Unit,
                    Value = i.TotalValue,
                    Status = i.Status
                }).ToList(),
            CriticalItems = allInventory.Where(i =>
                i.Status == StockStatus.OutOfStock ||
                i.Status == StockStatus.LowStock ||
                i.Status == StockStatus.Expiring)
                .Select(i => new InventoryItem
                {
                    Id = i.Id,
                    Name = i.IngredientName,
                    Category = i.Category,
                    CurrentStock = i.CurrentStock,
                    Unit = i.Unit,
                    Value = i.TotalValue,
                    Status = i.Status
                }).ToList(),
            RecentTransactions = await GetRecentTransactionsAsync(10)
        };

        return report;
    }

    // ==== HELPER METHODS ====

    private StockStatus DetermineStockStatus(Inventory inventory)
    {
        if (inventory.CurrentStock == 0)
            return StockStatus.OutOfStock;

        if (inventory.ExpiryDate.HasValue)
        {
            var daysUntilExpiry = (inventory.ExpiryDate.Value - DateTime.UtcNow).Days;
            if (daysUntilExpiry <= 7)
                return StockStatus.Expiring;
        }

        if (inventory.CurrentStock <= inventory.MinimumStock)
            return StockStatus.LowStock;

        if (inventory.MaximumStock > 0 && inventory.CurrentStock > inventory.MaximumStock)
            return StockStatus.Overstock;

        return StockStatus.InStock;
    }

    #endregion
}
