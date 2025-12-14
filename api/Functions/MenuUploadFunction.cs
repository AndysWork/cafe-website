using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using OfficeOpenXml;
using System.Text.RegularExpressions;

namespace Cafe.Api.Functions;

public class MenuUploadFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public MenuUploadFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<MenuUploadFunction>();
    }

    [Function("UploadMenuExcel")]
    public async Task<HttpResponseData> UploadMenuExcel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu/upload")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Check if we should clear existing menu items before upload
            var queryString = req.Url.Query;
            var clearExisting = queryString.Contains("clearExisting=true", StringComparison.OrdinalIgnoreCase);
            
            if (clearExisting)
            {
                _log.LogInformation("Clearing existing menu items before upload...");
                await _mongo.ClearMenuItemsAsync();
            }
            
            // Set EPPlus license context
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Read the entire request body
            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No file data received" });
                return badRequest;
            }

            // Try to find the Excel file data in the multipart content
            var excelData = ExtractExcelFromMultipart(fileBytes);
            
            if (excelData == null || excelData.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No valid Excel file found in request" });
                return badRequest;
            }

            // Process the Excel file
            var result = await ProcessMenuExcel(excelData);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading menu Excel file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message, details = ex.ToString() });
            return errorResponse;
        }
    }

    private byte[] ExtractExcelFromMultipart(byte[] data)
    {
        try
        {
            // Look for Excel file signature (PK\x03\x04 for .xlsx files)
            var excelSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == excelSignature[0] &&
                    data[i + 1] == excelSignature[1] &&
                    data[i + 2] == excelSignature[2] &&
                    data[i + 3] == excelSignature[3])
                {
                    // Found Excel file start, extract from here to end
                    var excelData = new byte[data.Length - i];
                    Array.Copy(data, i, excelData, 0, excelData.Length);
                    return excelData;
                }
            }

            // If no multipart wrapper found, assume entire body is the Excel file
            return data;
        }
        catch
        {
            return data;
        }
    }

    private async Task<object> ProcessMenuExcel(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes);
        using var package = new ExcelPackage(stream);
        
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet == null)
        {
            throw new Exception("No worksheet found in Excel file");
        }

        var rowCount = worksheet.Dimension?.Rows ?? 0;
        if (rowCount <= 1)
        {
            throw new Exception("Excel file is empty or has no data rows");
        }

        // Read header row to find column positions
        var columnMap = new Dictionary<string, int>();
        var colCount = worksheet.Dimension?.Columns ?? 0;
        
        _log.LogInformation($"Reading headers from {colCount} columns");
        
        for (int col = 1; col <= colCount; col++)
        {
            var headerValue = worksheet.Cells[1, col].Text.Trim().ToLower().Replace(" ", "_");
            if (!string.IsNullOrEmpty(headerValue))
            {
                columnMap[headerValue] = col;
                _log.LogInformation($"Column {col}: '{headerValue}'");
            }
        }

        _log.LogInformation($"Column mappings: {string.Join(", ", columnMap.Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Validate required columns
        var requiredColumns = new[] { "category_name", "subcategory_name", "catalogue_name", "current_price" };
        var missingColumns = requiredColumns.Where(c => !columnMap.ContainsKey(c)).ToList();
        if (missingColumns.Any())
        {
            throw new Exception($"Missing required columns: {string.Join(", ", missingColumns)}. Found columns: {string.Join(", ", columnMap.Keys)}");
        }

        // Get all categories and subcategories for matching
        var allCategories = await _mongo.GetCategoriesAsync();
        var allSubCategories = await _mongo.GetSubCategoriesAsync();

        // Debug: Show first data row (row 2) to see actual values
        _log.LogInformation("=== FIRST DATA ROW (Row 2) DEBUG ===");
        for (int col = 1; col <= colCount; col++)
        {
            var cellValue = worksheet.Cells[2, col].Value?.ToString() ?? "(null)";
            var cellText = worksheet.Cells[2, col].Text ?? "(null)";
            _log.LogInformation($"Col {col}: Value='{cellValue}', Text='{cellText}'");
        }
        _log.LogInformation("=== END DEBUG ===");

        var menuItems = new List<CafeMenuItem>();
        var errors = new List<string>();
        var processedItems = new Dictionary<string, CafeMenuItem>(); // Key: catalogue_name

        // Expected columns: category_name, subcategory_name, catalogue_name, variant_name, current_price, description
        for (int row = 2; row <= rowCount; row++)
        {
            try
            {
                // Use .Text property instead of .Value to get the formatted text value
                var categoryName = worksheet.Cells[row, columnMap["category_name"]].Text?.Trim() ?? string.Empty;
                var subCategoryName = worksheet.Cells[row, columnMap["subcategory_name"]].Text?.Trim() ?? string.Empty;
                var catalogueName = worksheet.Cells[row, columnMap["catalogue_name"]].Text?.Trim() ?? string.Empty;
                var variantName = columnMap.ContainsKey("variant_name") 
                    ? worksheet.Cells[row, columnMap["variant_name"]].Text?.Trim() ?? string.Empty
                    : string.Empty;
                var priceText = worksheet.Cells[row, columnMap["current_price"]].Text?.Trim() ?? string.Empty;
                var description = columnMap.ContainsKey("description") 
                    ? worksheet.Cells[row, columnMap["description"]].Text?.Trim() ?? string.Empty
                    : string.Empty;

                // Log the values for debugging
                _log.LogInformation($"Row {row}: Category='{categoryName}', SubCat='{subCategoryName}', Item='{catalogueName}', Variant='{variantName}', Price='{priceText}', Desc='{description}'");

                if (string.IsNullOrWhiteSpace(catalogueName))
                {
                    errors.Add($"Row {row}: Missing catalogue name");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    errors.Add($"Row {row}: Missing category name");
                    continue;
                }

                // Find matching category
                var category = allCategories.FirstOrDefault(c => 
                    c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                
                if (category == null)
                {
                    errors.Add($"Row {row}: Category '{categoryName}' not found in database. Available categories: {string.Join(", ", allCategories.Select(c => c.Name))}");
                    continue;
                }

                // If category and subcategory names match, skip subcategory (leave it blank/null)
                MenuSubCategory? subCategory = null;
                if (!string.IsNullOrWhiteSpace(subCategoryName) && 
                    !categoryName.Equals(subCategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    // Find matching subcategory only if it's different from category
                    subCategory = allSubCategories.FirstOrDefault(sc => 
                        sc.Name.Equals(subCategoryName, StringComparison.OrdinalIgnoreCase) &&
                        sc.CategoryId == category.Id);
                    
                    if (subCategory == null)
                    {
                        var availableSubCats = allSubCategories.Where(sc => sc.CategoryId == category.Id).Select(sc => sc.Name);
                        errors.Add($"Row {row}: Subcategory '{subCategoryName}' not found for category '{categoryName}'. Available: {string.Join(", ", availableSubCats)}");
                        continue;
                    }
                }

                // Parse price - handle different formats
                if (string.IsNullOrWhiteSpace(priceText))
                {
                    errors.Add($"Row {row}: Missing price");
                    continue;
                }

                // Clean price text - remove currency symbols and whitespace
                var cleanPriceText = priceText.Replace("₹", "").Replace("$", "").Replace(",", "").Trim();
                
                if (!decimal.TryParse(cleanPriceText, out var price))
                {
                    errors.Add($"Row {row}: Invalid price '{priceText}' (cleaned: '{cleanPriceText}')");
                    continue;
                }

                if (price <= 0)
                {
                    errors.Add($"Row {row}: Price must be greater than 0, got {price}");
                    continue;
                }

                // Extract quantity from variant name (if numeric value present)
                int? quantity = ExtractQuantityFromVariant(variantName);

                // Check if this menu item already exists in our processed list
                if (!processedItems.ContainsKey(catalogueName))
                {
                    // Create new menu item
                    var menuItem = new CafeMenuItem
                    {
                        Name = catalogueName,
                        Description = description,
                        Category = categoryName,
                        CategoryId = category.Id,
                        SubCategoryId = subCategory?.Id,
                        Quantity = quantity ?? 0,
                        OnlinePrice = price,
                        ShopSellingPrice = price,
                        MakingPrice = 0,
                        PackagingCharge = 0,
                        Variants = new List<MenuItemVariant>(),
                        CreatedBy = "Admin",
                        CreatedDate = MongoService.GetIstNow(),
                        LastUpdatedBy = "Admin",
                        LastUpdated = MongoService.GetIstNow()
                    };

                    processedItems[catalogueName] = menuItem;
                }
                else
                {
                    // Update description if current row has one and existing doesn't
                    if (!string.IsNullOrWhiteSpace(description) && 
                        string.IsNullOrWhiteSpace(processedItems[catalogueName].Description))
                    {
                        processedItems[catalogueName].Description = description;
                    }
                }

                // Add variant to the menu item (even if variant name is empty, we should track the price)
                if (!string.IsNullOrWhiteSpace(variantName))
                {
                    var variant = new MenuItemVariant
                    {
                        VariantName = variantName,
                        Price = price,
                        Quantity = quantity
                    };

                    processedItems[catalogueName].Variants.Add(variant);
                }
                else if (processedItems[catalogueName].Variants.Count == 0)
                {
                    // If no variant name but this is the first entry, use catalogue name as variant name
                    var variant = new MenuItemVariant
                    {
                        VariantName = catalogueName,
                        Price = price,
                        Quantity = quantity
                    };

                    processedItems[catalogueName].Variants.Add(variant);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Row {row}: {ex.Message}");
                _log.LogError(ex, "Error processing row {Row}", row);
            }
        }

        // Save all menu items to database
        var savedCount = 0;
        if (processedItems.Any())
        {
            savedCount = await _mongo.BulkInsertMenuItemsAsync(processedItems.Values.ToList());
        }

        return new
        {
            success = true,
            totalRows = rowCount - 1,
            processedItems = processedItems.Count,
            savedItems = savedCount,
            errors = errors,
            message = $"Successfully processed {processedItems.Count} menu items with {processedItems.Values.Sum(m => m.Variants.Count)} variants"
        };
    }

    private int? ExtractQuantityFromVariant(string variantName)
    {
        if (string.IsNullOrWhiteSpace(variantName))
            return null;

        // Look for numeric values in the variant name
        var match = Regex.Match(variantName, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var quantity))
        {
            return quantity;
        }

        return null;
    }
}
