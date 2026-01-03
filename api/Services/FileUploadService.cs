using Cafe.Api.Models;
using CsvHelper;
using CsvHelper.Configuration;
using OfficeOpenXml;
using System.Globalization;

namespace Cafe.Api.Services;

public class FileUploadService
{
    public class CategoryUploadDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public string SubCategoryName { get; set; } = string.Empty;
    }

    public class UploadResult
    {
        public bool Success { get; set; }
        public int CategoriesProcessed { get; set; }
        public int SubCategoriesProcessed { get; set; }
        public List<string> Errors { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public async Task<UploadResult> ProcessExcelFile(Stream fileStream, MongoService mongoService, string uploadedBy)
    {
        var result = new UploadResult();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        try
        {
            using var package = new ExcelPackage(fileStream);
            var worksheet = package.Workbook.Worksheets[0];
            var rowCount = worksheet.Dimension?.Rows ?? 0;

            if (rowCount < 2)
            {
                result.Errors.Add("File is empty or missing headers");
                return result;
            }

            var categories = new Dictionary<string, MenuCategory>();
            var subCategories = new List<MenuSubCategory>();
            var subCategoryKeys = new HashSet<string>(); // Track unique subcategories

            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var categoryName = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                    var subCategoryName = worksheet.Cells[row, 2].Value?.ToString()?.Trim();

                    if (string.IsNullOrEmpty(categoryName))
                    {
                        result.Errors.Add($"Row {row}: Category name is required");
                        continue;
                    }

                    // Add or get category (skip if already exists)
                    if (!categories.ContainsKey(categoryName))
                    {
                        categories[categoryName] = new MenuCategory
                        {
                            Name = categoryName
                        };
                    }

                    // Add subcategory if provided and not same as category name
                    if (!string.IsNullOrEmpty(subCategoryName) && 
                        !subCategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Create unique key for subcategory (category:subcategory)
                        var subCategoryKey = $"{categoryName}:{subCategoryName}";
                        
                        // Only add if not duplicate
                        if (!subCategoryKeys.Contains(subCategoryKey))
                        {
                            subCategories.Add(new MenuSubCategory
                            {
                                CategoryName = categoryName,
                                Name = subCategoryName
                            });
                            subCategoryKeys.Add(subCategoryKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                }
            }

            // Save categories first
            foreach (var category in categories.Values)
            {
                var created = await mongoService.CreateCategoryAsync(category);
                result.CategoriesProcessed++;

                // Update subcategories with the created category ID
                foreach (var subCat in subCategories.Where(sc => sc.CategoryName == category.Name))
                {
                    subCat.CategoryId = created.Id;
                }
            }

            // Save subcategories
            foreach (var subCat in subCategories)
            {
                if (!string.IsNullOrEmpty(subCat.CategoryId))
                {
                    await mongoService.CreateSubCategoryAsync(subCat);
                    result.SubCategoriesProcessed++;
                }
            }

            if (result.CategoriesProcessed > 0 || result.SubCategoriesProcessed > 0)
            {
                result.Success = true;
                result.Message = $"Successfully imported {result.CategoriesProcessed} categories and {result.SubCategoriesProcessed} subcategories";
            }
            else
            {
                result.Success = false;
                result.Message = "No data was imported. Please check your file format.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"File processing error: {ex.Message}");
            result.Message = "Upload failed due to an error.";
        }

        return result;
    }

    public async Task<UploadResult> ProcessCsvFile(Stream fileStream, MongoService mongoService, string uploadedBy)
    {
        var result = new UploadResult();

        try
        {
            using var reader = new StreamReader(fileStream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<CategoryUploadDto>().ToList();

            if (!records.Any())
            {
                result.Errors.Add("File is empty");
                return result;
            }

            var categories = new Dictionary<string, MenuCategory>();
            var subCategories = new List<MenuSubCategory>();
            var subCategoryKeys = new HashSet<string>(); // Track unique subcategories

            foreach (var record in records)
            {
                try
                {
                    if (string.IsNullOrEmpty(record.CategoryName))
                    {
                        result.Errors.Add($"Category name is required");
                        continue;
                    }

                    // Add or get category (skip if already exists)
                    if (!categories.ContainsKey(record.CategoryName))
                    {
                        categories[record.CategoryName] = new MenuCategory
                        {
                            Name = record.CategoryName
                        };
                    }

                    // Add subcategory if provided and not same as category name
                    if (!string.IsNullOrEmpty(record.SubCategoryName) && 
                        !record.SubCategoryName.Equals(record.CategoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Create unique key for subcategory (category:subcategory)
                        var subCategoryKey = $"{record.CategoryName}:{record.SubCategoryName}";
                        
                        // Only add if not duplicate
                        if (!subCategoryKeys.Contains(subCategoryKey))
                        {
                            subCategories.Add(new MenuSubCategory
                            {
                                CategoryName = record.CategoryName,
                                Name = record.SubCategoryName
                            });
                            subCategoryKeys.Add(subCategoryKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error processing record: {ex.Message}");
                }
            }

            // Save categories first
            foreach (var category in categories.Values)
            {
                var created = await mongoService.CreateCategoryAsync(category);
                result.CategoriesProcessed++;

                // Update subcategories with the created category ID
                foreach (var subCat in subCategories.Where(sc => sc.CategoryName == category.Name))
                {
                    subCat.CategoryId = created.Id;
                }
            }

            // Save subcategories
            foreach (var subCat in subCategories)
            {
                if (!string.IsNullOrEmpty(subCat.CategoryId))
                {
                    await mongoService.CreateSubCategoryAsync(subCat);
                    result.SubCategoriesProcessed++;
                }
            }

            if (result.CategoriesProcessed > 0 || result.SubCategoriesProcessed > 0)
            {
                result.Success = true;
                result.Message = $"Successfully imported {result.CategoriesProcessed} categories and {result.SubCategoriesProcessed} subcategories";
            }
            else
            {
                result.Success = false;
                result.Message = "No data was imported. Please check your file format.";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"CSV processing error: {ex.Message}");
            result.Message = "Upload failed due to an error.";
        }

        return result;
    }

    // Parse Zomato/Swiggy Excel and create OnlineSale records
    public async Task<OnlineSaleUploadResult> ProcessOnlineSaleExcel(
        Stream fileStream,
        string platform,
        MongoService mongoService,
        string uploadedBy)
    {
        // Route to appropriate parser based on platform
        if (platform == "Zomato")
        {
            return await ProcessZomatoExcel(fileStream, mongoService, uploadedBy);
        }
        else if (platform == "Swiggy")
        {
            return await ProcessSwiggyExcel(fileStream, mongoService, uploadedBy);
        }
        else
        {
            var result = new OnlineSaleUploadResult();
            result.Errors.Add($"Unsupported platform: {platform}. Must be 'Zomato' or 'Swiggy'");
            return result;
        }
    }

    // Parse Zomato Excel format
    // Columns: ZomatoOrderId, CustomerName, OrderAt, Distance, OrderedItems, Instructions, 
    // DiscountCoupon, BillSubTotal, PackagingCharges, DiscountAmount, TotalCommissionable, 
    // Payout, ZomatoDeduction, Investment, MiscCharges, Rating, Review, KPT, RWT, OrderMarking, Complain
    private async Task<OnlineSaleUploadResult> ProcessZomatoExcel(
        Stream fileStream,
        MongoService mongoService,
        string uploadedBy)
    {
        var result = new OnlineSaleUploadResult();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        try
        {
            using var package = new ExcelPackage(fileStream);
            var worksheet = package.Workbook.Worksheets[0];
            var rowCount = worksheet.Dimension?.Rows ?? 0;

            if (rowCount < 2)
            {
                result.Errors.Add("File is empty or missing headers");
                return result;
            }

            var sales = new List<OnlineSale>();

            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var orderId = worksheet.Cells[row, 1].Text.Trim();
                    if (string.IsNullOrEmpty(orderId))
                        continue;

                    var customerName = worksheet.Cells[row, 2].Text.Trim();
                    var orderAtStr = worksheet.Cells[row, 3].Text.Trim();
                    var distanceStr = worksheet.Cells[row, 4].Text.Trim();
                    var orderedItemsStr = worksheet.Cells[row, 5].Text.Trim();
                    var instructions = worksheet.Cells[row, 6].Text.Trim();
                    var discountCoupon = worksheet.Cells[row, 7].Text.Trim();
                    var billSubTotalStr = worksheet.Cells[row, 8].Text.Trim();
                    var packagingChargesStr = worksheet.Cells[row, 9].Text.Trim();
                    var discountAmountStr = worksheet.Cells[row, 10].Text.Trim();
                    var freebiesStr = worksheet.Cells[row, 11].Text.Trim();
                    var totalCommissionableStr = worksheet.Cells[row, 12].Text.Trim();
                    var payoutStr = worksheet.Cells[row, 13].Text.Trim();
                    var deductionStr = worksheet.Cells[row, 14].Text.Trim();
                    var investmentStr = worksheet.Cells[row, 15].Text.Trim();
                    var miscChargesStr = worksheet.Cells[row, 16].Text.Trim();
                    var ratingStr = worksheet.Cells[row, 17].Text.Trim();
                    var review = worksheet.Cells[row, 18].Text.Trim();
                    var kptStr = worksheet.Cells[row, 19].Text.Trim();
                    var rwtStr = worksheet.Cells[row, 20].Text.Trim();
                    var orderMarking = worksheet.Cells[row, 21].Text.Trim();
                    var complain = worksheet.Cells[row, 22].Text.Trim();

                    // Parse date (format: 01-Nov-25 or 1-Nov-25 or 01-12-2025 08:46 or 12-1-25 8:46 or 12-13-25 9:08)
                    DateTime orderAt;
                    // Normalize double spaces to single space for parsing
                    var normalizedOrderAtStr = System.Text.RegularExpressions.Regex.Replace(orderAtStr, @"\s+", " ");
                    
                    if (!DateTime.TryParseExact(normalizedOrderAtStr, new[] { 
                        "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy", 
                        "dd-MM-yyyy", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "d-M-yy", "dd-M-yy",
                        "dd-MM-yyyy HH:mm", "d-M-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "d-M-yyyy HH:mm:ss",
                        "dd-MM-yyyy H:mm", "d-M-yyyy H:mm", "dd-MM-yyyy H:mm:ss", "d-M-yyyy H:mm:ss",
                        "d-M-yy H:mm", "dd-M-yy H:mm", "d-M-yy HH:mm", "dd-M-yy HH:mm",
                        "d-M-yy H:mm:ss", "dd-M-yy H:mm:ss", "d-M-yy HH:mm:ss", "dd-M-yy HH:mm:ss",
                        "M-d-yy H:mm", "MM-d-yy H:mm", "M-dd-yy H:mm", "MM-dd-yy H:mm",
                        "M-d-yy HH:mm", "MM-d-yy HH:mm", "M-dd-yy HH:mm", "MM-dd-yy HH:mm",
                        "M-d-yy H:mm:ss", "MM-d-yy H:mm:ss", "M-dd-yy H:mm:ss", "MM-dd-yy H:mm:ss"
                    },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out orderAt))
                    {
                        result.Errors.Add($"Row {row}: Invalid date format '{orderAtStr}'");
                        continue;
                    }

                    // If the parsed date has a time component, use it; otherwise set to noon IST
                    DateTime istOrderAt;
                    if (orderAt.TimeOfDay.TotalSeconds > 0)
                    {
                        // Time component exists, use it as-is (assumed to be in IST)
                        istOrderAt = DateTime.SpecifyKind(orderAt, DateTimeKind.Unspecified);
                    }
                    else
                    {
                        // No time component, set to noon IST to avoid date boundary issues
                        istOrderAt = new DateTime(orderAt.Year, orderAt.Month, orderAt.Day, 12, 0, 0, DateTimeKind.Unspecified);
                    }

                    // Parse ordered items (e.g., "1 x Chicken Red Sauce Pasta, 1 x Bread Egg Toast")
                    var orderedItems = await ParseOrderedItems(orderedItemsStr, mongoService);

                    var sale = new OnlineSale
                    {
                        Platform = "Zomato", // Clearly mark as Zomato
                        OrderId = orderId,
                        CustomerName = string.IsNullOrEmpty(customerName) ? null : customerName,
                        OrderAt = istOrderAt,
                        Distance = ParseDecimal(distanceStr),
                        OrderedItems = orderedItems,
                        Instructions = string.IsNullOrEmpty(instructions) ? null : instructions,
                        DiscountCoupon = string.IsNullOrEmpty(discountCoupon) ? null : discountCoupon,
                        BillSubTotal = ParseDecimal(billSubTotalStr),
                        PackagingCharges = ParseDecimal(packagingChargesStr),
                        DiscountAmount = ParseDecimal(discountAmountStr),
                        GST = 0, // Zomato doesn't provide GST separately
                        TotalCommissionable = ParseDecimal(totalCommissionableStr),
                        Payout = ParseDecimal(payoutStr),
                        PlatformDeduction = ParseDecimal(deductionStr),
                        Investment = ParseDecimal(investmentStr),
                        MiscCharges = ParseDecimal(miscChargesStr),
                        Rating = ParseNullableDecimal(ratingStr),
                        Review = string.IsNullOrEmpty(review) ? null : review,
                        KPT = ParseNullableDecimal(kptStr),
                        RWT = ParseNullableDecimal(rwtStr),
                        OrderMarking = string.IsNullOrEmpty(orderMarking) ? null : orderMarking,
                        Complain = string.IsNullOrEmpty(complain) ? null : complain,
                        Freebies = ParseDecimal(freebiesStr),
                        UploadedBy = uploadedBy,
                        CreatedAt = MongoService.GetIstNow(),
                        UpdatedAt = MongoService.GetIstNow()
                    };

                    sales.Add(sale);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                }
            }

            // Bulk insert
            if (sales.Any())
            {
                var bulkResult = await mongoService.BulkCreateOnlineSalesAsync(sales);
                result.SalesProcessed = bulkResult.InsertedCount;
                result.Success = bulkResult.Success;
                
                if (bulkResult.SkippedCount > 0)
                {
                    result.Message = $"Successfully imported {bulkResult.InsertedCount} Zomato sales. Skipped {bulkResult.SkippedCount} duplicates.";
                    result.Errors.AddRange(bulkResult.Duplicates);
                }
                else
                {
                    result.Message = $"Successfully imported {bulkResult.InsertedCount} Zomato sales";
                }
                
                if (!string.IsNullOrEmpty(bulkResult.ErrorMessage))
                {
                    result.Errors.Add($"Database error: {bulkResult.ErrorMessage}");
                }
            }
            else
            {
                result.Success = false;
                result.Message = "No valid Zomato sales data found in file";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Excel processing error: {ex.Message}");
            result.Message = "Upload failed due to an error.";
        }

        return result;
    }

    // Parse Swiggy Excel format
    // Swiggy may have different column structure - adjust based on actual Swiggy report format
    // Placeholder columns: SwiggyOrderId, OrderDate, CustomerName, Items, Total, Payout, etc.
    private async Task<OnlineSaleUploadResult> ProcessSwiggyExcel(
        Stream fileStream,
        MongoService mongoService,
        string uploadedBy)
    {
        var result = new OnlineSaleUploadResult();
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        try
        {
            using var package = new ExcelPackage(fileStream);
            var worksheet = package.Workbook.Worksheets[0];
            var rowCount = worksheet.Dimension?.Rows ?? 0;

            if (rowCount < 2)
            {
                result.Errors.Add("File is empty or missing headers");
                return result;
            }

            var sales = new List<OnlineSale>();

            // Swiggy Excel Column Mapping (22 columns) - No Freebies for Swiggy:
            // A: SwiggyOrderId, B: CustomerName, C: OrderAt, D: Distance, E: OrderedItems, F: Instructions
            // G: DiscountCoupon, H: BillSubTotal, I: PackagingCharges, J: DiscountAmount, K: GST
            // L: TotalCommissionable, M: Payout, N: SwiggyDeduction, O: Investment, P: MiscCharges
            // Q: Rating, R: Review, S: KPT, T: RWT, U: OrderMarking, V: Complain
            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    // A: Swiggy Order ID
                    var orderId = worksheet.Cells[row, 1].Text.Trim();
                    if (string.IsNullOrEmpty(orderId))
                        continue;

                    // B: Customer Name
                    var customerName = worksheet.Cells[row, 2].Text.Trim();
                    
                    // C: Order Date
                    var orderAtStr = worksheet.Cells[row, 3].Text.Trim();
                    
                    // D: Distance
                    var distanceStr = worksheet.Cells[row, 4].Text.Trim();
                    
                    // E: Ordered Items
                    var orderedItemsStr = worksheet.Cells[row, 5].Text.Trim();
                    
                    // F: Instructions
                    var instructions = worksheet.Cells[row, 6].Text.Trim();
                    
                    // G: Discount Coupon
                    var discountCoupon = worksheet.Cells[row, 7].Text.Trim();
                    
                    // H: Bill SubTotal
                    var billSubTotalStr = worksheet.Cells[row, 8].Text.Trim();
                    
                    // I: Packaging Charges
                    var packagingChargesStr = worksheet.Cells[row, 9].Text.Trim();
                    
                    // J: Discount Amount
                    var discountAmountStr = worksheet.Cells[row, 10].Text.Trim();
                    
                    // K: GST
                    var gstStr = worksheet.Cells[row, 11].Text.Trim();
                    
                    // L: Total Commissionable
                    var totalCommissionableStr = worksheet.Cells[row, 12].Text.Trim();
                    
                    // M: Payout
                    var payoutStr = worksheet.Cells[row, 13].Text.Trim();
                    
                    // N: Swiggy Deduction
                    var deductionStr = worksheet.Cells[row, 14].Text.Trim();
                    
                    // O: Investment
                    var investmentStr = worksheet.Cells[row, 15].Text.Trim();
                    
                    // P: Misc Charges
                    var miscChargesStr = worksheet.Cells[row, 16].Text.Trim();
                    
                    // Q: Rating
                    var ratingStr = worksheet.Cells[row, 17].Text.Trim();
                    
                    // R: Review
                    var review = worksheet.Cells[row, 18].Text.Trim();
                    
                    // S: KPT
                    var kptStr = worksheet.Cells[row, 19].Text.Trim();
                    
                    // T: RWT
                    var rwtStr = worksheet.Cells[row, 20].Text.Trim();
                    
                    // U: Order Marking
                    var orderMarking = worksheet.Cells[row, 21].Text.Trim();
                    
                    // V: Complain
                    var complain = worksheet.Cells[row, 22].Text.Trim();

                    // Parse date
                    DateTime orderAt;
                    if (!DateTime.TryParseExact(orderAtStr, new[] { "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy", "dd-MM-yyyy", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "yyyy-MM-dd" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out orderAt))
                    {
                        result.Errors.Add($"Row {row}: Invalid date format '{orderAtStr}'");
                        continue;
                    }

                    // Assume the date in Excel is in IST, set the time to noon IST to avoid date boundary issues
                    var istOrderAt = new DateTime(orderAt.Year, orderAt.Month, orderAt.Day, 12, 0, 0, DateTimeKind.Unspecified);

                    // Parse ordered items
                    var orderedItems = await ParseOrderedItems(orderedItemsStr, mongoService);

                    var sale = new OnlineSale
                    {
                        Platform = "Swiggy",
                        OrderId = orderId,
                        CustomerName = string.IsNullOrEmpty(customerName) ? null : customerName,
                        OrderAt = istOrderAt,
                        Distance = ParseDecimal(distanceStr),
                        OrderedItems = orderedItems,
                        Instructions = string.IsNullOrEmpty(instructions) ? null : instructions,
                        DiscountCoupon = string.IsNullOrEmpty(discountCoupon) ? null : discountCoupon,
                        BillSubTotal = ParseDecimal(billSubTotalStr),
                        PackagingCharges = ParseDecimal(packagingChargesStr),
                        DiscountAmount = ParseDecimal(discountAmountStr),
                        Freebies = 0, // Swiggy doesn't have freebies
                        GST = ParseDecimal(gstStr),
                        TotalCommissionable = ParseDecimal(totalCommissionableStr),
                        Payout = ParseDecimal(payoutStr),
                        PlatformDeduction = ParseDecimal(deductionStr),
                        Investment = ParseDecimal(investmentStr),
                        MiscCharges = ParseDecimal(miscChargesStr),
                        Rating = ParseNullableDecimal(ratingStr),
                        Review = string.IsNullOrEmpty(review) ? null : review,
                        KPT = ParseNullableDecimal(kptStr),
                        RWT = ParseNullableDecimal(rwtStr),
                        OrderMarking = string.IsNullOrEmpty(orderMarking) ? null : orderMarking,
                        Complain = string.IsNullOrEmpty(complain) ? null : complain,
                        UploadedBy = uploadedBy,
                        CreatedAt = MongoService.GetIstNow(),
                        UpdatedAt = MongoService.GetIstNow()
                    };

                    sales.Add(sale);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                }
            }

            // Bulk insert
            if (sales.Any())
            {
                var bulkResult = await mongoService.BulkCreateOnlineSalesAsync(sales);
                result.SalesProcessed = bulkResult.InsertedCount;
                result.Success = bulkResult.Success;
                
                if (bulkResult.SkippedCount > 0)
                {
                    result.Message = $"Successfully imported {bulkResult.InsertedCount} Swiggy sales. Skipped {bulkResult.SkippedCount} duplicates.";
                    result.Errors.AddRange(bulkResult.Duplicates);
                }
                else
                {
                    result.Message = $"Successfully imported {bulkResult.InsertedCount} Swiggy sales";
                }
                
                if (!string.IsNullOrEmpty(bulkResult.ErrorMessage))
                {
                    result.Errors.Add($"Database error: {bulkResult.ErrorMessage}");
                }
            }
            else
            {
                result.Success = false;
                result.Message = "No valid Swiggy sales data found in file";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Excel processing error: {ex.Message}");
            result.Message = "Upload failed due to an error.";
        }

        return result;
    }

    // Parse ordered items string like "1 x Chicken Red Sauce Pasta, 1 x Bread Egg Toast"
    private async Task<List<OrderedItem>> ParseOrderedItems(string orderedItemsStr, MongoService mongoService)
    {
        var items = new List<OrderedItem>();

        if (string.IsNullOrWhiteSpace(orderedItemsStr))
            return items;

        // Split by comma
        var itemParts = orderedItemsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var itemPart in itemParts)
        {
            try
            {
                // Parse "1 x Chicken Red Sauce Pasta"
                var parts = itemPart.Trim().Split(" x ", StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2)
                {
                    var quantityStr = parts[0].Trim();
                    var itemName = parts[1].Trim();

                    if (int.TryParse(quantityStr, out int quantity))
                    {
                        // Try to match with menu item
                        var menuItemId = await mongoService.FindMenuItemIdByNameAsync(itemName);

                        items.Add(new OrderedItem
                        {
                            Quantity = quantity,
                            ItemName = itemName,
                            MenuItemId = menuItemId
                        });
                    }
                }
            }
            catch
            {
                // Skip invalid items
                continue;
            }
        }

        return items;
    }

    private decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;

        return 0;
    }

    private decimal? ParseNullableDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;

        return null;
    }
}

public class OnlineSaleUploadResult
{
    public bool Success { get; set; }
    public int SalesProcessed { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

