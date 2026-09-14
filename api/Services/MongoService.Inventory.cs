// Inventory Management Methods Extension for MongoService
using MongoDB.Driver;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public partial class MongoService : IInventoryRepository
{
    #region Inventory Management

    // ==== INVENTORY CRUD ====

    private void EnsureBatchesPopulated(Inventory inventory)
    {
        if (inventory == null) return;
        inventory.Batches ??= new List<StockBatch>();
        if (inventory.Batches.Count == 0 && inventory.CurrentStock > 0)
        {
            inventory.Batches.Add(new StockBatch
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BatchNumber = "INITIAL-STOCK",
                InitialQuantity = inventory.CurrentStock,
                RemainingQuantity = inventory.CurrentStock,
                CostPerUnit = inventory.CostPerUnit,
                PurchasePrice = inventory.LastPurchasePrice ?? (inventory.CurrentStock * inventory.CostPerUnit),
                SupplierName = inventory.SupplierName,
                ExpiryDate = inventory.ExpiryDate,
                ReceivedDate = inventory.CreatedAt
            });
        }
    }

    public async Task<List<Inventory>> GetAllInventoryAsync(string? outletId = null, int? page = null, int? pageSize = null)
    {
        // If no outlet is selected, return empty list instead of all data
        if (outletId == null)
            return new List<Inventory>();
        
        var filter = Builders<Inventory>.Filter.Eq(i => i.OutletId, outletId);
        var fluent = _inventory.Find(filter).SortBy(i => i.IngredientName);
        
        List<Inventory> list;
        if (page.HasValue && pageSize.HasValue)
            list = await fluent.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value).ToListAsync();
        else
            list = await fluent.Limit(Helpers.PaginationHelper.SafetyLimit).ToListAsync();

        foreach (var item in list) EnsureBatchesPopulated(item);
        return list;
    }

    public async Task<long> GetAllInventoryCountAsync(string? outletId = null)
    {
        if (outletId == null) return 0;
        var filter = Builders<Inventory>.Filter.Eq(i => i.OutletId, outletId);
        return await _inventory.CountDocumentsAsync(filter);
    }

    public async Task<List<Inventory>> GetActiveInventoryAsync(string? outletId = null, int? page = null, int? pageSize = null)
    {
        var filterBuilder = Builders<Inventory>.Filter;
        var filters = new List<FilterDefinition<Inventory>>
        {
            filterBuilder.Eq(i => i.IsActive, true)
        };

        if (outletId != null)
        {
            filters.Add(filterBuilder.Eq(i => i.OutletId, outletId));
        }

        var filter = filterBuilder.And(filters);
        var fluent = _inventory.Find(filter).SortBy(i => i.IngredientName);
        
        List<Inventory> list;
        if (page.HasValue && pageSize.HasValue)
            list = await fluent.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value).ToListAsync();
        else
            list = await fluent.Limit(Helpers.PaginationHelper.SafetyLimit).ToListAsync();

        foreach (var item in list) EnsureBatchesPopulated(item);
        return list;
    }

    public async Task<Inventory?> GetInventoryByIdAsync(string id)
    {
        var item = await _inventory.Find(i => i.Id == id).FirstOrDefaultAsync();
        if (item != null) EnsureBatchesPopulated(item);
        return item;
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

    public async Task<List<Inventory>> GetLowStockItemsAsync(string? outletId = null)
    {
        var filterBuilder = Builders<Inventory>.Filter;
        var filters = new List<FilterDefinition<Inventory>>
        {
            filterBuilder.Eq(i => i.IsActive, true),
            filterBuilder.Where(i => i.CurrentStock <= i.MinimumStock && i.CurrentStock > 0)
        };

        if (outletId != null)
        {
            filters.Add(filterBuilder.Eq(i => i.OutletId, outletId));
        }

        var filter = filterBuilder.And(filters);
        return await _inventory.Find(filter)
            .SortBy(i => i.CurrentStock)
            .ToListAsync();
    }

    public async Task<List<Inventory>> GetOutOfStockItemsAsync(string? outletId = null)
    {
        var filterBuilder = Builders<Inventory>.Filter;
        var filters = new List<FilterDefinition<Inventory>>
        {
            filterBuilder.Eq(i => i.IsActive, true),
            filterBuilder.Eq(i => i.CurrentStock, 0)
        };

        if (outletId != null)
        {
            filters.Add(filterBuilder.Eq(i => i.OutletId, outletId));
        }

        var filter = filterBuilder.And(filters);
        return await _inventory.Find(filter)
            .SortBy(i => i.IngredientName)
            .ToListAsync();
    }

    public async Task<List<Inventory>> GetExpiringItemsAsync(int daysThreshold = 7, string? outletId = null)
    {
        var thresholdDate = MongoService.GetIstNow().AddDays(daysThreshold);
        var filterBuilder = Builders<Inventory>.Filter;
        var filters = new List<FilterDefinition<Inventory>>
        {
            filterBuilder.Eq(i => i.IsActive, true),
            filterBuilder.Ne(i => i.ExpiryDate, null),
            filterBuilder.Lte(i => i.ExpiryDate, thresholdDate)
        };

        if (outletId != null)
        {
            filters.Add(filterBuilder.Eq(i => i.OutletId, outletId));
        }

        var filter = filterBuilder.And(filters);
        return await _inventory.Find(filter)
            .SortBy(i => i.ExpiryDate)
            .ToListAsync();
    }

    private async Task SyncInventoryItemToIngredientAsync(Inventory inventory)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(inventory.IngredientName)) return;

        try
        {
            var normName = inventory.IngredientName.Trim();
            var price = inventory.CostPerUnit > 0 ? inventory.CostPerUnit : (inventory.LastPurchasePrice ?? 0);

            Ingredient? existing = null;
            if (!string.IsNullOrEmpty(inventory.IngredientId))
            {
                existing = await _ingredients.Find(i => i.Id == inventory.IngredientId).FirstOrDefaultAsync();
            }

            if (existing == null)
            {
                existing = await _ingredients.Find(i =>
                    i.Name.ToLower() == normName.ToLower() &&
                    (i.OutletId == inventory.OutletId || i.OutletId == null) &&
                    i.IsDeleted != true
                ).FirstOrDefaultAsync();
            }

            if (existing != null)
            {
                existing.Name = normName;
                existing.Category = inventory.Category;
                existing.Unit = inventory.Unit;
                if (price > 0)
                {
                    existing.MarketPrice = price;
                }
                existing.IsActive = inventory.IsActive;
                existing.UpdatedAt = MongoService.GetIstNow();
                existing.LastUpdated = MongoService.GetIstNow();
                existing.PriceSource = "inventory";

                await _ingredients.ReplaceOneAsync(i => i.Id == existing.Id, existing);
                inventory.IngredientId = existing.Id;
            }
            else
            {
                var newIng = new Ingredient
                {
                    Name = normName,
                    Category = inventory.Category,
                    Unit = inventory.Unit,
                    MarketPrice = price,
                    OutletId = inventory.OutletId,
                    IsActive = inventory.IsActive,
                    PriceSource = "inventory",
                    CreatedAt = MongoService.GetIstNow(),
                    UpdatedAt = MongoService.GetIstNow(),
                    LastUpdated = MongoService.GetIstNow()
                };
                await _ingredients.InsertOneAsync(newIng);
                inventory.IngredientId = newIng.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync inventory item {ItemName} to ingredients collection", inventory.IngredientName);
        }
    }

    public async Task<Inventory> CreateInventoryAsync(Inventory inventory)
    {
        inventory.CreatedAt = MongoService.GetIstNow();
        inventory.UpdatedAt = MongoService.GetIstNow();

        // Stock is added later via Stock IN, so initialize clean values if not provided
        if (inventory.CurrentStock <= 0)
        {
            inventory.CurrentStock = 0;
            inventory.CostPerUnit = 0;
            inventory.LastPurchasePrice = 0;
            inventory.TotalValue = 0;
            inventory.Batches = new List<StockBatch>();
        }
        else
        {
            inventory.Batches ??= new List<StockBatch>();
            if (inventory.Batches.Count == 0)
            {
                inventory.Batches.Add(new StockBatch
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    BatchNumber = "INITIAL-STOCK",
                    InitialQuantity = inventory.CurrentStock,
                    RemainingQuantity = inventory.CurrentStock,
                    CostPerUnit = inventory.CostPerUnit,
                    PurchasePrice = inventory.LastPurchasePrice ?? (inventory.CurrentStock * inventory.CostPerUnit),
                    SupplierName = inventory.SupplierName,
                    ExpiryDate = inventory.ExpiryDate,
                    ReceivedDate = MongoService.GetIstNow()
                });
            }
            inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        }

        inventory.Status = DetermineStockStatus(inventory);

        // Sync with ingredients collection so Price Calculator immediately recognizes it
        await SyncInventoryItemToIngredientAsync(inventory);

        await _inventory.InsertOneAsync(inventory);
        return inventory;
    }

    public async Task<bool> UpdateInventoryAsync(string id, Inventory inventory)
    {
        var existing = await GetInventoryByIdAsync(id);
        if (existing == null) return false;

        // Update configurable catalog fields only (Item Name, Category, Unit, Min/Max/Reorder Stock, Storage Location)
        existing.IngredientName = inventory.IngredientName;
        existing.Category = inventory.Category;
        existing.Unit = inventory.Unit;
        existing.MinimumStock = inventory.MinimumStock;
        existing.MaximumStock = inventory.MaximumStock;
        existing.ReorderQuantity = inventory.ReorderQuantity;
        existing.StorageLocation = inventory.StorageLocation;

        // Buy price, Cost per unit, and Stock remain auto-calculated and preserved from stock transactions
        existing.UpdatedAt = MongoService.GetIstNow();
        if (!string.IsNullOrEmpty(inventory.LastUpdatedBy))
        {
            existing.LastUpdatedBy = inventory.LastUpdatedBy;
        }
        existing.TotalValue = existing.CurrentStock * existing.CostPerUnit;
        existing.Status = DetermineStockStatus(existing);

        await SyncInventoryItemToIngredientAsync(existing);

        var result = await _inventory.ReplaceOneAsync(i => i.Id == id, existing);
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
            TransactionDate = MongoService.GetIstNow()
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory — compensate by deleting transaction on failure
        try
        {
            inventory.CurrentStock = stockAfter;
            inventory.UpdatedAt = MongoService.GetIstNow();
            inventory.LastUpdatedBy = performedBy;
            inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
            inventory.Status = DetermineStockStatus(inventory);

            if (type == TransactionType.StockIn)
            {
                inventory.LastRestockDate = MongoService.GetIstNow();
            }

            var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

            // Check and create alerts (best-effort, don't fail the operation)
            try { await CheckAndCreateAlertsAsync(inventory); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to check/create stock alerts for {InventoryId}", inventoryId); }

            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update inventory {InventoryId} after transaction — rolling back transaction {TransactionId}", inventoryId, transaction.Id);
            await _inventoryTransactions.DeleteOneAsync(t => t.Id == transaction.Id);
            throw;
        }
    }

    public async Task<bool> StockInAsync(string inventoryId, decimal quantity, decimal? costPerUnit, string? supplierName, string? referenceNumber, string performedBy, DateTime? expiryDate = null, decimal? purchasePrice = null)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null) return false;

        inventory.Batches ??= new List<StockBatch>();

        // Calculate unit cost if purchase price given but not costPerUnit
        var effectiveCostPerUnit = costPerUnit ?? (purchasePrice.HasValue && quantity > 0 ? purchasePrice.Value / quantity : (decimal?)null);
        var effectivePurchasePrice = purchasePrice ?? (effectiveCostPerUnit.HasValue ? effectiveCostPerUnit.Value * quantity : (decimal?)null);

        // Create new stock batch
        var newBatch = new StockBatch
        {
            BatchNumber = !string.IsNullOrWhiteSpace(referenceNumber) ? referenceNumber : $"BATCH-{MongoService.GetIstNow():yyMMddHHmmss}",
            InitialQuantity = quantity,
            RemainingQuantity = quantity,
            CostPerUnit = effectiveCostPerUnit ?? inventory.CostPerUnit,
            PurchasePrice = effectivePurchasePrice,
            SupplierName = supplierName ?? inventory.SupplierName,
            ReferenceNumber = referenceNumber,
            ExpiryDate = expiryDate ?? inventory.ExpiryDate,
            ReceivedDate = MongoService.GetIstNow()
        };

        inventory.Batches.Add(newBatch);

        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = TransactionType.StockIn,
            Quantity = quantity,
            Unit = inventory.Unit,
            CostPerUnit = effectiveCostPerUnit,
            TotalCost = effectivePurchasePrice,
            StockBefore = inventory.CurrentStock,
            StockAfter = inventory.CurrentStock + quantity,
            SupplierName = supplierName,
            ReferenceNumber = referenceNumber,
            ExpiryDate = expiryDate,
            BatchId = newBatch.Id,
            Reason = "Stock purchase/receipt",
            PerformedBy = performedBy,
            TransactionDate = MongoService.GetIstNow()
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory — compensate by deleting transaction on failure
        try
        {
            inventory.CurrentStock += quantity;

            // Compute weighted average cost per unit across all active batches (or fallback to running formula)
            var activeBatches = inventory.Batches.Where(b => b.RemainingQuantity > 0).ToList();
            if (activeBatches.Any())
            {
                var totalActiveValue = activeBatches.Sum(b => b.RemainingQuantity * b.CostPerUnit);
                var totalActiveQty = activeBatches.Sum(b => b.RemainingQuantity);
                if (totalActiveQty > 0)
                {
                    inventory.CostPerUnit = totalActiveValue / totalActiveQty;
                }
            }
            else if (effectiveCostPerUnit.HasValue)
            {
                decimal totalCost = (inventory.CurrentStock - quantity) * inventory.CostPerUnit + quantity * effectiveCostPerUnit.Value;
                inventory.CostPerUnit = totalCost / inventory.CurrentStock;
            }

            if (effectivePurchasePrice.HasValue)
            {
                inventory.LastPurchasePrice = effectivePurchasePrice.Value;
            }
            else if (effectiveCostPerUnit.HasValue)
            {
                inventory.LastPurchasePrice = effectiveCostPerUnit.Value;
            }

            // Expiry date is earliest active batch expiry date, or provided expiry date
            var earliestActiveExpiry = inventory.Batches
                .Where(b => b.RemainingQuantity > 0 && b.ExpiryDate.HasValue)
                .OrderBy(b => b.ExpiryDate)
                .Select(b => b.ExpiryDate)
                .FirstOrDefault();

            if (earliestActiveExpiry.HasValue)
            {
                inventory.ExpiryDate = earliestActiveExpiry;
            }
            else if (expiryDate.HasValue)
            {
                inventory.ExpiryDate = expiryDate;
            }

            inventory.LastPurchaseDate = MongoService.GetIstNow();
            inventory.LastRestockDate = MongoService.GetIstNow();
            
            if (!string.IsNullOrEmpty(supplierName))
            {
                inventory.SupplierName = supplierName;
            }

            inventory.UpdatedAt = MongoService.GetIstNow();
            inventory.LastUpdatedBy = performedBy;
            inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
            inventory.Status = DetermineStockStatus(inventory);

            var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

            // Sync updated cost per unit to ingredient record
            await SyncInventoryItemToIngredientAsync(inventory);

            // Resolve alerts (best-effort)
            try { await ResolveAlertsAsync(inventoryId, new[] { AlertType.LowStock, AlertType.OutOfStock }, performedBy); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to resolve stock alerts for {InventoryId}", inventoryId); }

            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update inventory {InventoryId} after stock-in — rolling back transaction {TransactionId}", inventoryId, transaction.Id);
            await _inventoryTransactions.DeleteOneAsync(t => t.Id == transaction.Id);
            throw;
        }
    }

    public async Task<bool> StockOutAsync(string inventoryId, decimal quantity, string reason, string performedBy, string? batchId = null)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null || inventory.CurrentStock < quantity) return false;

        inventory.Batches ??= new List<StockBatch>();

        decimal remainingToDeduct = quantity;
        string? targetedBatchNumber = null;

        if (!string.IsNullOrWhiteSpace(batchId))
        {
            var targetBatch = inventory.Batches.FirstOrDefault(b => b.Id == batchId && b.RemainingQuantity > 0);
            if (targetBatch != null)
            {
                targetedBatchNumber = targetBatch.BatchNumber ?? targetBatch.Id;
                if (targetBatch.RemainingQuantity < quantity)
                {
                    return false; // Cannot deduct more than is available in the selected batch
                }

                targetBatch.RemainingQuantity -= quantity;
                remainingToDeduct = 0;
            }
        }

        // If no specific batch targeted or fallback, deduct FIFO ordered by expiry date (earliest first), then received date
        if (remainingToDeduct > 0)
        {
            var eligibleBatches = inventory.Batches
                .Where(b => b.RemainingQuantity > 0)
                .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(b => b.ReceivedDate)
                .ToList();

            foreach (var batch in eligibleBatches)
            {
                if (remainingToDeduct <= 0) break;

                if (batch.RemainingQuantity <= remainingToDeduct)
                {
                    remainingToDeduct -= batch.RemainingQuantity;
                    batch.RemainingQuantity = 0;
                }
                else
                {
                    batch.RemainingQuantity -= remainingToDeduct;
                    remainingToDeduct = 0;
                }
            }
        }

        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = TransactionType.StockOut,
            Quantity = quantity,
            Unit = inventory.Unit,
            StockBefore = inventory.CurrentStock,
            StockAfter = inventory.CurrentStock - quantity,
            Reason = !string.IsNullOrEmpty(targetedBatchNumber) ? $"{reason} (Batch: {targetedBatchNumber})" : reason,
            BatchId = batchId,
            PerformedBy = performedBy,
            TransactionDate = MongoService.GetIstNow()
        };

        await _inventoryTransactions.InsertOneAsync(transaction);

        // Update inventory — compensate by deleting transaction on failure
        try
        {
            inventory.CurrentStock -= quantity;

            // Recalculate weighted cost per unit & earliest expiry date from remaining batches
            var activeBatches = inventory.Batches.Where(b => b.RemainingQuantity > 0).ToList();
            if (activeBatches.Any())
            {
                var totalActiveValue = activeBatches.Sum(b => b.RemainingQuantity * b.CostPerUnit);
                var totalActiveQty = activeBatches.Sum(b => b.RemainingQuantity);
                if (totalActiveQty > 0)
                {
                    inventory.CostPerUnit = totalActiveValue / totalActiveQty;
                }

                var earliestActiveExpiry = activeBatches
                    .Where(b => b.ExpiryDate.HasValue)
                    .OrderBy(b => b.ExpiryDate)
                    .Select(b => b.ExpiryDate)
                    .FirstOrDefault();

                if (earliestActiveExpiry.HasValue)
                {
                    inventory.ExpiryDate = earliestActiveExpiry;
                }
            }

            inventory.UpdatedAt = MongoService.GetIstNow();
            inventory.LastUpdatedBy = performedBy;
            inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
            inventory.Status = DetermineStockStatus(inventory);

            var result = await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

            // Check for low stock (best-effort)
            try { await CheckAndCreateAlertsAsync(inventory); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to check/create stock alerts for {InventoryId}", inventoryId); }

            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update inventory {InventoryId} after stock-out — rolling back transaction {TransactionId}", inventoryId, transaction.Id);
            await _inventoryTransactions.DeleteOneAsync(t => t.Id == transaction.Id);
            throw;
        }
    }

    // ==== 1-CLICK WASTAGE LOGGING FOR BATCHES ====

    public async Task<LogBatchWastageResult> LogBatchWastageAsync(string inventoryId, LogBatchWastageRequest request)
    {
        var inventory = await GetInventoryByIdAsync(inventoryId);
        if (inventory == null)
        {
            return new LogBatchWastageResult { Success = false, Message = "Inventory item not found" };
        }

        inventory.Batches ??= new List<StockBatch>();

        // Find target batch if specified, otherwise pick earliest expired/expiring batch
        StockBatch? targetBatch = null;
        if (!string.IsNullOrWhiteSpace(request.BatchId))
        {
            targetBatch = inventory.Batches.FirstOrDefault(b => b.Id == request.BatchId);
        }

        if (targetBatch == null)
        {
            targetBatch = inventory.Batches
                .Where(b => b.RemainingQuantity > 0)
                .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(b => b.ReceivedDate)
                .FirstOrDefault();
        }

        if (targetBatch == null)
        {
            return new LogBatchWastageResult { Success = false, Message = "No active batch found with stock to waste" };
        }

        decimal qtyToWaste = request.Quantity > 0 ? Math.Min(request.Quantity, targetBatch.RemainingQuantity) : targetBatch.RemainingQuantity;
        if (qtyToWaste <= 0)
        {
            return new LogBatchWastageResult { Success = false, Message = "Quantity to waste must be greater than zero" };
        }

        decimal unitCost = targetBatch.CostPerUnit > 0 ? targetBatch.CostPerUnit : inventory.CostPerUnit;
        decimal financialLoss = Math.Round(qtyToWaste * unitCost, 2);

        // Deduct from batch
        targetBatch.RemainingQuantity -= qtyToWaste;
        inventory.CurrentStock = Math.Max(0, inventory.CurrentStock - qtyToWaste);

        // Recalculate inventory cost and earliest expiry
        var activeBatches = inventory.Batches.Where(b => b.RemainingQuantity > 0).ToList();
        if (activeBatches.Any())
        {
            var totalActiveVal = activeBatches.Sum(b => b.RemainingQuantity * b.CostPerUnit);
            var totalActiveQty = activeBatches.Sum(b => b.RemainingQuantity);
            if (totalActiveQty > 0)
            {
                inventory.CostPerUnit = totalActiveVal / totalActiveQty;
            }
            var earliestExp = activeBatches.Where(b => b.ExpiryDate.HasValue).OrderBy(b => b.ExpiryDate).Select(b => b.ExpiryDate).FirstOrDefault();
            if (earliestExp.HasValue) inventory.ExpiryDate = earliestExp;
        }

        inventory.UpdatedAt = MongoService.GetIstNow();
        inventory.LastUpdatedBy = request.PerformedBy ?? "admin";
        inventory.TotalValue = inventory.CurrentStock * inventory.CostPerUnit;
        inventory.Status = DetermineStockStatus(inventory);

        // Record Inventory Transaction
        var transaction = new InventoryTransaction
        {
            InventoryId = inventoryId,
            IngredientName = inventory.IngredientName,
            Type = TransactionType.Wastage,
            Quantity = qtyToWaste,
            Unit = inventory.Unit,
            CostPerUnit = unitCost,
            TotalCost = financialLoss,
            StockBefore = inventory.CurrentStock + qtyToWaste,
            StockAfter = inventory.CurrentStock,
            Reason = $"Wastage: {request.Reason} (Batch: {targetBatch.BatchNumber ?? targetBatch.Id})",
            BatchId = targetBatch.Id,
            PerformedBy = request.PerformedBy ?? "admin",
            TransactionDate = MongoService.GetIstNow()
        };
        await _inventoryTransactions.InsertOneAsync(transaction);

        // Record in WastageRecords module
        var wastageRecord = new WastageRecord
        {
            OutletId = inventory.OutletId ?? "default",
            Date = MongoService.GetIstNow(),
            Reason = !string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.ToLowerInvariant() : "expired",
            Notes = $"Item: {inventory.IngredientName}, Batch: {targetBatch.BatchNumber ?? targetBatch.Id}. {request.Notes}".Trim(),
            RecordedBy = request.PerformedBy ?? "admin",
            Items = new List<WastageItem>
            {
                new WastageItem
                {
                    ItemName = inventory.IngredientName,
                    IngredientId = inventory.IngredientId,
                    Quantity = qtyToWaste,
                    Unit = inventory.Unit,
                    CostPerUnit = unitCost,
                    TotalCost = financialLoss
                }
            },
            TotalValue = financialLoss,
            CreatedAt = MongoService.GetIstNow()
        };
        await _wastageRecords.InsertOneAsync(wastageRecord);

        await _inventory.ReplaceOneAsync(i => i.Id == inventoryId, inventory);

        return new LogBatchWastageResult
        {
            Success = true,
            Message = $"Logged {qtyToWaste} {inventory.Unit} of {inventory.IngredientName} as wastage (Loss: ₹{financialLoss:F2})",
            QuantityWasted = qtyToWaste,
            Unit = inventory.Unit,
            FinancialLoss = financialLoss,
            RemainingStock = inventory.CurrentStock,
            WastageRecordId = wastageRecord.Id
        };
    }

    // ==== RECIPE-INVENTORY SYNC DEDUCTIONS ====

    public async Task<int> DeductInventoryForOrderRecipesAsync(Order order)
    {
        if (order == null || order.Items == null || order.Items.Count == 0) return 0;
        if (order.InventoryDeducted) return 0; // Prevent double deduction

        int ingredientsDeductedCount = 0;
        var outletId = order.OutletId;

        // Fetch all active recipes
        var allRecipes = await _recipes.Find(_ => true).ToListAsync();
        if (allRecipes.Count == 0) return 0;

        foreach (var orderItem in order.Items)
        {
            var matchingRecipe = allRecipes.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.MenuItemId) && r.MenuItemId == orderItem.MenuItemId) ||
                string.Equals(r.MenuItemName?.Trim(), orderItem.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matchingRecipe == null || matchingRecipe.Ingredients == null || matchingRecipe.Ingredients.Count == 0)
            {
                continue;
            }

            decimal orderItemQty = orderItem.Quantity > 0 ? orderItem.Quantity : 1;

            foreach (var ingredientUsage in matchingRecipe.Ingredients)
            {
                decimal totalDeductQty = ingredientUsage.Quantity * orderItemQty;
                if (totalDeductQty <= 0) continue;

                // Find matching inventory item by IngredientId or Name and outlet
                Inventory? invItem = null;
                if (!string.IsNullOrEmpty(ingredientUsage.IngredientId))
                {
                    var filter = Builders<Inventory>.Filter.Eq(i => i.IngredientId, ingredientUsage.IngredientId);
                    if (!string.IsNullOrEmpty(outletId))
                    {
                        filter = Builders<Inventory>.Filter.And(filter, Builders<Inventory>.Filter.Eq(i => i.OutletId, outletId));
                    }
                    invItem = await _inventory.Find(filter).FirstOrDefaultAsync();
                }

                if (invItem == null && !string.IsNullOrEmpty(ingredientUsage.IngredientName))
                {
                    var filter = Builders<Inventory>.Filter.Regex(i => i.IngredientName,
                        new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(ingredientUsage.IngredientName.Trim())}$", "i"));
                    if (!string.IsNullOrEmpty(outletId))
                    {
                        filter = Builders<Inventory>.Filter.And(filter, Builders<Inventory>.Filter.Eq(i => i.OutletId, outletId));
                    }
                    invItem = await _inventory.Find(filter).FirstOrDefaultAsync();
                }

                if (invItem != null && invItem.Id != null)
                {
                    try
                    {
                        await StockOutAsync(
                            invItem.Id,
                            totalDeductQty,
                            $"Recipe sync: Order #{order.Id?[^6..]} ({orderItem.Name} x{orderItemQty})",
                            "Kitchen-KOT"
                        );
                        ingredientsDeductedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-deduct recipe inventory for {Ingredient} in order {OrderId}", ingredientUsage.IngredientName, order.Id);
                    }
                }
            }
        }

        // Mark order as inventory deducted
        if (order.Id != null)
        {
            await _orders.UpdateOneAsync(
                Builders<Order>.Filter.Eq(o => o.Id, order.Id),
                Builders<Order>.Update.Set(o => o.InventoryDeducted, true)
            );
        }

        return ingredientsDeductedCount;
    }

    // ==== BULK UPLOAD INVENTORY (EXCEL) ====

    public async Task<BulkUploadInventoryResult> BulkUploadInventoryAsync(List<InventoryItemUpload> items, string outletId, string performedBy)
    {
        var result = new BulkUploadInventoryResult
        {
            Total = items?.Count ?? 0
        };

        if (items == null || items.Count == 0)
        {
            result.Message = "No items provided in upload file";
            return result;
        }

        int rowIndex = 1; // Header is row 1
        foreach (var item in items)
        {
            rowIndex++;
            try
            {
                if (string.IsNullOrWhiteSpace(item.ItemName))
                {
                    result.Errors.Add($"Row {rowIndex}: Item name is required");
                    result.Failed++;
                    continue;
                }

                var itemName = item.ItemName.Trim();
                var category = !string.IsNullOrWhiteSpace(item.Category) ? item.Category.Trim() : "Other";
                var unit = !string.IsNullOrWhiteSpace(item.Unit) ? item.Unit.Trim() : "kg";
                var minStock = item.MinimumStock >= 0 ? item.MinimumStock : 5;
                var maxStock = item.MaximumStock >= 0 ? item.MaximumStock : 50;
                var reorderQty = item.ReorderQuantity >= 0 ? item.ReorderQuantity : 10;
                var storageLocation = item.StorageLocation?.Trim();

                // Look for existing inventory item by name in this outlet (case-insensitive)
                var existing = await _inventory
                    .Find(i => i.OutletId == outletId &&
                               i.IngredientName.ToLower() == itemName.ToLower())
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    // Update catalog properties
                    existing.Category = category;
                    existing.Unit = unit;
                    existing.MinimumStock = minStock;
                    existing.MaximumStock = maxStock;
                    existing.ReorderQuantity = reorderQty;
                    if (!string.IsNullOrEmpty(storageLocation))
                    {
                        existing.StorageLocation = storageLocation;
                    }
                    existing.UpdatedAt = MongoService.GetIstNow();
                    existing.LastUpdatedBy = performedBy;

                    await _inventory.ReplaceOneAsync(i => i.Id == existing.Id, existing);
                    await SyncInventoryItemToIngredientAsync(existing);

                    // If initial stock > 0 was provided, add as a new batch
                    if (item.InitialStock > 0)
                    {
                        decimal? unitCost = item.CostPerUnit > 0 ? item.CostPerUnit : (decimal?)null;
                        await StockInAsync(
                            existing.Id!,
                            item.InitialStock,
                            unitCost,
                            item.SupplierName ?? existing.SupplierName,
                            "EXCEL-UPLOAD",
                            performedBy,
                            item.ExpiryDate,
                            unitCost.HasValue ? item.InitialStock * unitCost.Value : (decimal?)null
                        );
                    }

                    result.Success++;
                }
                else
                {
                    // Create new inventory item
                    var newInventory = new Inventory
                    {
                        OutletId = outletId,
                        IngredientName = itemName,
                        Category = category,
                        Unit = unit,
                        MinimumStock = minStock,
                        MaximumStock = maxStock,
                        ReorderQuantity = reorderQty,
                        StorageLocation = storageLocation,
                        CurrentStock = 0,
                        CostPerUnit = 0,
                        LastPurchasePrice = 0,
                        TotalValue = 0,
                        Status = StockStatus.OutOfStock,
                        IsActive = true,
                        CreatedAt = MongoService.GetIstNow(),
                        UpdatedAt = MongoService.GetIstNow(),
                        CreatedBy = performedBy,
                        LastUpdatedBy = performedBy,
                        Batches = new List<StockBatch>()
                    };

                    await _inventory.InsertOneAsync(newInventory);
                    await SyncInventoryItemToIngredientAsync(newInventory);

                    // If initial stock > 0, record as batch via StockInAsync
                    if (item.InitialStock > 0)
                    {
                        decimal? unitCost = item.CostPerUnit > 0 ? item.CostPerUnit : (decimal?)null;
                        await StockInAsync(
                            newInventory.Id!,
                            item.InitialStock,
                            unitCost,
                            item.SupplierName,
                            "EXCEL-UPLOAD",
                            performedBy,
                            item.ExpiryDate,
                            unitCost.HasValue ? item.InitialStock * unitCost.Value : (decimal?)null
                        );
                    }

                    result.Success++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing bulk inventory upload row {Row}: {ItemName}", rowIndex, item.ItemName);
                result.Errors.Add($"Row {rowIndex} ({item.ItemName}): {ex.Message}");
                result.Failed++;
            }
        }

        result.Message = $"Processed {result.Total} rows: {result.Success} succeeded, {result.Failed} failed.";
        return result;
    }

    // ==== TRANSACTIONS ====

    #region Inventory Categories (Outlet-Scoped)

    public async Task<List<InventoryCategory>> GetInventoryCategoriesAsync(string outletId)
    {
        if (string.IsNullOrWhiteSpace(outletId))
            return new List<InventoryCategory>();

        var filter = Builders<InventoryCategory>.Filter.And(
            Builders<InventoryCategory>.Filter.Eq(c => c.OutletId, outletId),
            Builders<InventoryCategory>.Filter.Ne(c => c.IsDeleted, true)
        );

        var categories = await _inventoryCategories.Find(filter)
            .SortBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        if (categories.Count == 0)
        {
            // Seed default inventory categories for this outlet
            var defaults = new (string Name, string Description, int ShelfLife, int Order)[]
            {
                ("Vegetables", "Fresh produce, greens, herbs, and root vegetables", 2, 1),
                ("Dairy", "Milk, cheese, butter, cream, and paneer", 3, 2),
                ("Meats", "Fresh and chilled poultry, chicken, mutton, seafood", 1, 3),
                ("Bakery", "Breads, buns, burger rolls, pizza bases, pastry", 3, 4),
                ("frozen", "Frozen patties, fries, nuggets, and pre-prepped items", 30, 5),
                ("Beverages", "Coffee beans, tea leaves, syrups, juices, concentrates", 90, 6),
                ("Spices", "Whole spices, ground seasonings, masalas, and salt", 180, 7),
                ("Oils", "Cooking oil, deep-frying fats, olive oil, ghee", 180, 8),
                ("Grains", "Rice, flour, pasta, grains, and dry pulses", 180, 9),
                ("Sauces", "Ketchup, mayonnaise, dips, culinary sauces, pastes", 60, 10),
                ("Packaging", "Takeaway boxes, cups, lids, cutlery, bags", 365, 11),
                ("Cleaning", "Dishwashing detergents, sanitizers, housekeeping", 365, 12),
                ("Other", "Miscellaneous kitchen and cafe ingredients", 30, 13)
            };

            var toInsert = defaults.Select(d => new InventoryCategory
            {
                OutletId = outletId,
                Name = d.Name,
                Description = d.Description,
                ShelfLifeDays = d.ShelfLife,
                DisplayOrder = d.Order,
                IsActive = true,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow(),
                CreatedBy = "System"
            }).ToList();

            try
            {
                await _inventoryCategories.InsertManyAsync(toInsert);
                categories = toInsert;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed default inventory categories for outlet {OutletId}", outletId);
            }
        }

        return categories;
    }

    public async Task<InventoryCategory?> GetInventoryCategoryByIdAsync(string id, string outletId)
    {
        var filter = Builders<InventoryCategory>.Filter.And(
            Builders<InventoryCategory>.Filter.Eq(c => c.Id, id),
            Builders<InventoryCategory>.Filter.Eq(c => c.OutletId, outletId),
            Builders<InventoryCategory>.Filter.Ne(c => c.IsDeleted, true)
        );
        return await _inventoryCategories.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<InventoryCategory> CreateInventoryCategoryAsync(InventoryCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.OutletId))
            throw new ArgumentException("OutletId is required");

        category.Name = category.Name.Trim();
        
        // Check for duplicate name in this outlet (case-insensitive)
        var exists = await _inventoryCategories.Find(c =>
            c.OutletId == category.OutletId &&
            c.Name.ToLower() == category.Name.ToLower() &&
            c.IsDeleted != true).AnyAsync();

        if (exists)
        {
            throw new InvalidOperationException($"Category '{category.Name}' already exists for this outlet.");
        }

        category.CreatedAt = MongoService.GetIstNow();
        category.UpdatedAt = MongoService.GetIstNow();
        category.IsDeleted = false;
        category.IsActive = true;

        await _inventoryCategories.InsertOneAsync(category);
        return category;
    }

    public async Task<bool> UpdateInventoryCategoryAsync(string id, InventoryCategory category)
    {
        var existing = await _inventoryCategories.Find(c => c.Id == id && c.OutletId == category.OutletId && c.IsDeleted != true).FirstOrDefaultAsync();
        if (existing == null) return false;

        var oldName = existing.Name;
        var newName = category.Name.Trim();

        // Check if new name conflicts with another category in this outlet
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _inventoryCategories.Find(c =>
                c.OutletId == category.OutletId &&
                c.Id != id &&
                c.Name.ToLower() == newName.ToLower() &&
                c.IsDeleted != true).AnyAsync();

            if (exists)
            {
                throw new InvalidOperationException($"Category '{newName}' already exists for this outlet.");
            }

            // Cascade category rename to existing inventory items in this outlet
            try
            {
                var invFilter = Builders<Inventory>.Filter.And(
                    Builders<Inventory>.Filter.Eq(i => i.OutletId, category.OutletId),
                    Builders<Inventory>.Filter.Regex(i => i.Category, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(oldName)}$", "i"))
                );
                await _inventory.UpdateManyAsync(invFilter, Builders<Inventory>.Update.Set(i => i.Category, newName));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cascade category rename from '{Old}' to '{New}' in outlet {OutletId}", oldName, newName, category.OutletId);
            }
        }

        existing.Name = newName;
        existing.Description = category.Description;
        existing.ShelfLifeDays = category.ShelfLifeDays > 0 ? category.ShelfLifeDays : 7;
        existing.DisplayOrder = category.DisplayOrder;
        existing.IsActive = category.IsActive;
        existing.UpdatedAt = MongoService.GetIstNow();
        if (!string.IsNullOrWhiteSpace(category.LastUpdatedBy))
        {
            existing.LastUpdatedBy = category.LastUpdatedBy;
        }

        var result = await _inventoryCategories.ReplaceOneAsync(c => c.Id == id, existing);
        return result.ModifiedCount > 0;
    }

    public async Task<(bool success, string? errorMessage)> DeleteInventoryCategoryAsync(string id, string outletId, string performedBy)
    {
        var existing = await _inventoryCategories.Find(c => c.Id == id && c.OutletId == outletId && c.IsDeleted != true).FirstOrDefaultAsync();
        if (existing == null)
        {
            return (false, "Category not found.");
        }

        // Check if any active inventory items in this outlet use this category
        var inUseCount = await _inventory.CountDocumentsAsync(i =>
            i.OutletId == outletId &&
            i.IsActive &&
            i.Category.ToLower() == existing.Name.ToLower());

        if (inUseCount > 0)
        {
            return (false, $"Cannot delete category '{existing.Name}' because {inUseCount} active inventory item(s) are assigned to it. Please reassign those items to another category first.");
        }

        var update = Builders<InventoryCategory>.Update
            .Set(c => c.IsDeleted, true)
            .Set(c => c.DeletedAt, MongoService.GetIstNow())
            .Set(c => c.DeletedBy, performedBy);

        var result = await _inventoryCategories.UpdateOneAsync(c => c.Id == id, update);
        return (result.ModifiedCount > 0, null);
    }

    #endregion

    // ==== TRANSACTIONS ====

    public async Task<List<InventoryTransaction>> GetAllInventoryTransactionsAsync(string? outletId = null)
    {
        // If no outlet is selected, return empty list instead of all data
        if (outletId == null)
            return new List<InventoryTransaction>();
        
        var filter = Builders<InventoryTransaction>.Filter.Eq(t => t.OutletId, outletId);
        
        return await _inventoryTransactions.Find(filter)
            .SortByDescending(t => t.TransactionDate)
            .Limit(Helpers.PaginationHelper.SafetyLimit)
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
        // Enforce maximum date range of 1 year
        if ((endDate - startDate).TotalDays > 366)
        {
            endDate = startDate.AddDays(366);
        }
        
        return await _inventoryTransactions.Find(t =>
            t.TransactionDate >= startDate && t.TransactionDate <= endDate)
            .SortByDescending(t => t.TransactionDate)
            .Limit(Helpers.PaginationHelper.SafetyLimit)
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
            .Set(a => a.ResolvedAt, MongoService.GetIstNow())
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
            .Set(a => a.ResolvedAt, MongoService.GetIstNow())
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
                    CreatedAt = MongoService.GetIstNow()
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
                    CreatedAt = MongoService.GetIstNow()
                });
            }
        }

        // Check for expiring stock (considering category-specific shelf-life thresholds)
        var earliestExpiry = inventory.Batches?
            .Where(b => b.RemainingQuantity > 0 && b.ExpiryDate.HasValue)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => b.ExpiryDate)
            .FirstOrDefault() ?? inventory.ExpiryDate;

        if (earliestExpiry.HasValue)
        {
            var daysUntilExpiry = (earliestExpiry.Value.Date - MongoService.GetIstNow().Date).Days;
            var warningThreshold = GetCategoryExpiryWarningDays(inventory.Category);
            
            if (daysUntilExpiry <= warningThreshold && daysUntilExpiry > 0)
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
                        Severity = daysUntilExpiry <= 1 ? AlertSeverity.Critical : AlertSeverity.Warning,
                        Message = $"{inventory.IngredientName} ({inventory.Category}) expires in {daysUntilExpiry} {(daysUntilExpiry == 1 ? "day" : "days")} (Threshold: {warningThreshold}d)",
                        CurrentStock = inventory.CurrentStock,
                        CreatedAt = MongoService.GetIstNow()
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
                        CreatedAt = MongoService.GetIstNow()
                    });
                }
            }
        }
    }

    // ==== REPORTS ====

    public async Task<InventoryReport> GetInventoryReportAsync()
    {
        return await GetInventoryReportAsync(null);
    }

    public async Task<InventoryReport> GetInventoryReportAsync(string? outletId)
    {
        var allInventory = await GetActiveInventoryAsync(outletId);
        foreach (var item in allInventory)
        {
            EnsureBatchesPopulated(item);
        }

        var today = MongoService.GetIstNow().Date;

        var inStock = allInventory.Count(i => i.Status == StockStatus.InStock);
        var lowStock = allInventory.Count(i => i.Status == StockStatus.LowStock);
        var outOfStock = allInventory.Count(i => i.Status == StockStatus.OutOfStock);
        var expiring = allInventory.Count(i => i.Status == StockStatus.Expiring);
        var totalValue = allInventory.Sum(i => i.TotalValue);

        // Compute total batches & active batches
        int totalBatches = 0;
        int activeBatches = 0;
        var expiringBatchesList = new List<ExpiringBatchItem>();

        foreach (var inv in allInventory)
        {
            if (inv.Batches == null) continue;
            totalBatches += inv.Batches.Count;

            foreach (var b in inv.Batches.Where(b => b.RemainingQuantity > 0))
            {
                activeBatches++;

                if (b.ExpiryDate.HasValue)
                {
                    int daysLeft = (b.ExpiryDate.Value.Date - today).Days;
                    int threshold = GetCategoryExpiryWarningDays(inv.Category);

                    if (daysLeft <= Math.Max(30, threshold))
                    {
                        string urgency = daysLeft < 0 ? "expired"
                            : (daysLeft <= 1 ? "critical"
                            : (daysLeft <= threshold ? "warning" : "good"));

                        expiringBatchesList.Add(new ExpiringBatchItem
                        {
                            InventoryId = inv.Id ?? string.Empty,
                            ItemName = inv.IngredientName,
                            Category = inv.Category,
                            BatchId = b.Id,
                            BatchNumber = b.BatchNumber ?? b.ReferenceNumber ?? "Batch",
                            RemainingQuantity = b.RemainingQuantity,
                            Unit = inv.Unit,
                            CostPerUnit = b.CostPerUnit > 0 ? b.CostPerUnit : inv.CostPerUnit,
                            BatchValue = b.RemainingQuantity * (b.CostPerUnit > 0 ? b.CostPerUnit : inv.CostPerUnit),
                            ExpiryDate = b.ExpiryDate,
                            DaysRemaining = daysLeft,
                            ShelfLifeThresholdDays = threshold,
                            Urgency = urgency
                        });
                    }
                }
            }
        }

        // 30-day Wastage Loss
        decimal monthlyWastageLoss = 0;
        try
        {
            var thirtyDaysAgo = MongoService.GetIstNow().AddDays(-30);
            var wastageFilterBuilder = Builders<WastageRecord>.Filter;
            var wastageFilter = wastageFilterBuilder.Gte(w => w.Date, thirtyDaysAgo);
            if (!string.IsNullOrEmpty(outletId))
            {
                wastageFilter = wastageFilterBuilder.And(wastageFilter, wastageFilterBuilder.Eq(w => w.OutletId, outletId));
            }
            var recentWastage = await _wastageRecords.Find(wastageFilter).ToListAsync();
            monthlyWastageLoss = recentWastage.Sum(w => w.TotalValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute monthly wastage loss for inventory report");
        }

        // Category Summaries
        var categorySummaries = allInventory
            .GroupBy(i => !string.IsNullOrWhiteSpace(i.Category) ? i.Category : "Other")
            .Select(g =>
            {
                var catValue = g.Sum(x => x.TotalValue);
                return new CategoryInventorySummary
                {
                    Category = g.Key,
                    ItemCount = g.Count(),
                    TotalValue = catValue,
                    PercentageOfTotal = totalValue > 0 ? Math.Round((catValue / totalValue) * 100, 1) : 0,
                    LowStockCount = g.Count(x => x.Status == StockStatus.LowStock),
                    OutOfStockCount = g.Count(x => x.Status == StockStatus.OutOfStock)
                };
            })
            .OrderByDescending(c => c.TotalValue)
            .ToList();

        // Critical Items with expiry insights
        var criticalItems = allInventory
            .Where(i => i.Status == StockStatus.OutOfStock || i.Status == StockStatus.LowStock || i.Status == StockStatus.Expiring)
            .Select(i =>
            {
                var earliestBatchExpiry = i.Batches?
                    .Where(b => b.RemainingQuantity > 0 && b.ExpiryDate.HasValue)
                    .OrderBy(b => b.ExpiryDate)
                    .Select(b => b.ExpiryDate)
                    .FirstOrDefault() ?? i.ExpiryDate;

                int? daysUntilExp = earliestBatchExpiry.HasValue ? (earliestBatchExpiry.Value.Date - today).Days : null;

                return new InventoryItem
                {
                    Id = i.Id,
                    Name = i.IngredientName,
                    Category = i.Category,
                    CurrentStock = i.CurrentStock,
                    MinimumStock = i.MinimumStock,
                    Unit = i.Unit,
                    Value = i.TotalValue,
                    CostPerUnit = i.CostPerUnit,
                    EarliestExpiryDate = earliestBatchExpiry,
                    DaysUntilExpiry = daysUntilExp,
                    Status = i.Status
                };
            })
            .OrderBy(i => i.Status == StockStatus.OutOfStock ? 0 : (i.Status == StockStatus.Expiring ? 1 : 2))
            .ThenBy(i => i.DaysUntilExpiry ?? 999)
            .Take(15)
            .ToList();

        // Top value items
        var topValueItems = allInventory
            .OrderByDescending(i => i.TotalValue)
            .Take(6)
            .Select(i => new InventoryItem
            {
                Id = i.Id,
                Name = i.IngredientName,
                Category = i.Category,
                CurrentStock = i.CurrentStock,
                Unit = i.Unit,
                Value = i.TotalValue,
                CostPerUnit = i.CostPerUnit,
                Status = i.Status
            })
            .ToList();

        // Recent transactions
        List<InventoryTransaction> recentTransactions;
        try
        {
            recentTransactions = await GetRecentTransactionsAsync(10, outletId);
        }
        catch
        {
            recentTransactions = await GetRecentTransactionsAsync(10);
        }

        return new InventoryReport
        {
            TotalItems = allInventory.Count,
            ActiveItems = allInventory.Count(i => i.IsActive),
            InStockItems = inStock,
            LowStockItems = lowStock,
            OutOfStockItems = outOfStock,
            ExpiringItems = expiring,
            TotalBatchesCount = totalBatches,
            ActiveBatchesCount = activeBatches,
            TotalInventoryValue = totalValue,
            AverageCostPerItem = allInventory.Any() ? Math.Round(totalValue / allInventory.Count, 2) : 0,
            MonthlyWastageLoss = monthlyWastageLoss,
            LastUpdated = MongoService.GetIstNow(),
            TopValueItems = topValueItems,
            CriticalItems = criticalItems,
            ExpiringBatches = expiringBatchesList.OrderBy(b => b.DaysRemaining).Take(10).ToList(),
            CategorySummaries = categorySummaries,
            RecentTransactions = recentTransactions
        };
    }

    // ==== HELPER METHODS ====

    private int GetCategoryExpiryWarningDays(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return 7;
        var lower = category.Trim().ToLowerInvariant();
        if (lower.Contains("chicken") || lower.Contains("meat") || lower.Contains("poultry") || lower.Contains("fish") || lower.Contains("seafood"))
            return 1;
        if (lower.Contains("vegetable") || lower.Contains("fruit") || lower.Contains("produce") || lower.Contains("herb"))
            return 2;
        if (lower.Contains("bakery") || lower.Contains("bread") || lower.Contains("pastry") || lower.Contains("dairy") || lower.Contains("milk"))
            return 3;
        if (lower.Contains("frozen"))
            return 30; // 1 month
        return 7; // Default 7 days
    }

    private StockStatus DetermineStockStatus(Inventory inventory)
    {
        if (inventory.CurrentStock == 0)
            return StockStatus.OutOfStock;

        // Check earliest expiry across batches or direct ExpiryDate
        var earliestExpiry = inventory.Batches?
            .Where(b => b.RemainingQuantity > 0 && b.ExpiryDate.HasValue)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => b.ExpiryDate)
            .FirstOrDefault() ?? inventory.ExpiryDate;

        if (earliestExpiry.HasValue)
        {
            var daysUntilExpiry = (earliestExpiry.Value.Date - MongoService.GetIstNow().Date).Days;
            var warningThreshold = GetCategoryExpiryWarningDays(inventory.Category);
            if (daysUntilExpiry <= warningThreshold)
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

