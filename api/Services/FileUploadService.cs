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

                    // Add or get category
                    if (!categories.ContainsKey(categoryName))
                    {
                        categories[categoryName] = new MenuCategory
                        {
                            Name = categoryName
                        };
                    }

                    // Add subcategory if provided
                    if (!string.IsNullOrEmpty(subCategoryName))
                    {
                        subCategories.Add(new MenuSubCategory
                        {
                            CategoryName = categoryName,
                            Name = subCategoryName
                        });
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

            result.Success = true;
            result.Message = $"Successfully imported {result.CategoriesProcessed} categories and {result.SubCategoriesProcessed} subcategories";
        }
        catch (Exception ex)
        {
            result.Errors.Add($"File processing error: {ex.Message}");
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

            foreach (var record in records)
            {
                try
                {
                    if (string.IsNullOrEmpty(record.CategoryName))
                    {
                        result.Errors.Add($"Category name is required");
                        continue;
                    }

                    // Add or get category
                    if (!categories.ContainsKey(record.CategoryName))
                    {
                        categories[record.CategoryName] = new MenuCategory
                        {
                            Name = record.CategoryName
                        };
                    }

                    // Add subcategory if provided
                    if (!string.IsNullOrEmpty(record.SubCategoryName))
                    {
                        subCategories.Add(new MenuSubCategory
                        {
                            CategoryName = record.CategoryName,
                            Name = record.SubCategoryName
                        });
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

            result.Success = true;
            result.Message = $"Successfully imported {result.CategoriesProcessed} categories and {result.SubCategoriesProcessed} subcategories";
        }
        catch (Exception ex)
        {
            result.Errors.Add($"CSV processing error: {ex.Message}");
        }

        return result;
    }
}
