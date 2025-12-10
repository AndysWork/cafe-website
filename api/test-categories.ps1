# Test script for MenuCategory and MenuSubCategory CRUD operations
$baseUrl = "http://localhost:7071/api"

Write-Host "`n=== Testing MenuCategory CRUD ===" -ForegroundColor Cyan

# 1. Create Categories
Write-Host "`n1. Creating categories..." -ForegroundColor Yellow
$categories = @(
    @{
        Name = "Coffee"
        Description = "Hot and cold coffee beverages"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    },
    @{
        Name = "Pastries"
        Description = "Fresh baked pastries and desserts"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    },
    @{
        Name = "Sandwiches"
        Description = "Delicious sandwiches and wraps"
        DisplayOrder = 3
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    }
)

$createdCategories = @()
foreach ($cat in $categories) {
    $body = $cat | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Post -Body $body -ContentType "application/json"
    $createdCategories += $result
    Write-Host "Created: $($result.Name) (ID: $($result.Id))" -ForegroundColor Green
}

# 2. Get all categories
Write-Host "`n2. Getting all categories..." -ForegroundColor Yellow
$allCategories = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Get
Write-Host "Total categories: $($allCategories.Count)" -ForegroundColor Green
$allCategories | ForEach-Object { Write-Host "  - $($_.Name) (Order: $($_.DisplayOrder))" }

# 3. Get single category
Write-Host "`n3. Getting single category..." -ForegroundColor Yellow
$firstCategoryId = $createdCategories[0].Id
$singleCategory = Invoke-RestMethod -Uri "$baseUrl/categories/$firstCategoryId" -Method Get
Write-Host "Retrieved: $($singleCategory.Name)" -ForegroundColor Green

# 4. Update category
Write-Host "`n4. Updating category..." -ForegroundColor Yellow
$singleCategory.Description = "Premium coffee beverages - hot and cold"
$singleCategory.LastUpdatedBy = "Admin"
$singleCategory.LastUpdated = (Get-Date).ToString("o")
$body = $singleCategory | ConvertTo-Json
$updated = Invoke-RestMethod -Uri "$baseUrl/categories/$firstCategoryId" -Method Put -Body $body -ContentType "application/json"
Write-Host "Updated description: $($updated.Description)" -ForegroundColor Green

Write-Host "`n=== Testing MenuSubCategory CRUD ===" -ForegroundColor Cyan

# 5. Create SubCategories
Write-Host "`n5. Creating subcategories..." -ForegroundColor Yellow
$coffeeId = $createdCategories[0].Id
$pastriesId = $createdCategories[1].Id

$subCategories = @(
    @{
        CategoryId = $coffeeId
        Name = "Hot Coffee"
        Description = "Traditional hot coffee drinks"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    },
    @{
        CategoryId = $coffeeId
        Name = "Iced Coffee"
        Description = "Refreshing cold coffee drinks"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    },
    @{
        CategoryId = $pastriesId
        Name = "Muffins"
        Description = "Fresh baked muffins"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    },
    @{
        CategoryId = $pastriesId
        Name = "Croissants"
        Description = "Buttery French croissants"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
        CreatedDate = (Get-Date).ToString("o")
    }
)

$createdSubCategories = @()
foreach ($subCat in $subCategories) {
    $body = $subCat | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Post -Body $body -ContentType "application/json"
    $createdSubCategories += $result
    Write-Host "Created: $($result.Name) (ID: $($result.Id))" -ForegroundColor Green
}

# 6. Get all subcategories
Write-Host "`n6. Getting all subcategories..." -ForegroundColor Yellow
$allSubCategories = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Get
Write-Host "Total subcategories: $($allSubCategories.Count)" -ForegroundColor Green
$allSubCategories | ForEach-Object { Write-Host "  - $($_.Name) (Order: $($_.DisplayOrder))" }

# 7. Get subcategories by category
Write-Host "`n7. Getting subcategories for Coffee category..." -ForegroundColor Yellow
$coffeeSubCats = Invoke-RestMethod -Uri "$baseUrl/categories/$coffeeId/subcategories" -Method Get
Write-Host "Coffee subcategories: $($coffeeSubCats.Count)" -ForegroundColor Green
$coffeeSubCats | ForEach-Object { Write-Host "  - $($_.Name)" }

# 8. Get single subcategory
Write-Host "`n8. Getting single subcategory..." -ForegroundColor Yellow
$firstSubCategoryId = $createdSubCategories[0].Id
$singleSubCategory = Invoke-RestMethod -Uri "$baseUrl/subcategories/$firstSubCategoryId" -Method Get
Write-Host "Retrieved: $($singleSubCategory.Name)" -ForegroundColor Green

# 9. Update subcategory
Write-Host "`n9. Updating subcategory..." -ForegroundColor Yellow
$singleSubCategory.Description = "Espresso-based hot coffee beverages"
$singleSubCategory.LastUpdatedBy = "Admin"
$singleSubCategory.LastUpdated = (Get-Date).ToString("o")
$body = $singleSubCategory | ConvertTo-Json
$updated = Invoke-RestMethod -Uri "$baseUrl/subcategories/$firstSubCategoryId" -Method Put -Body $body -ContentType "application/json"
Write-Host "Updated description: $($updated.Description)" -ForegroundColor Green

# 10. Delete one subcategory (optional test)
Write-Host "`n10. Testing delete subcategory..." -ForegroundColor Yellow
$lastSubCategoryId = $createdSubCategories[-1].Id
Invoke-RestMethod -Uri "$baseUrl/subcategories/$lastSubCategoryId" -Method Delete
Write-Host "Deleted subcategory: $($createdSubCategories[-1].Name)" -ForegroundColor Green

# Verify deletion
$allSubCategories = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Get
Write-Host "Remaining subcategories: $($allSubCategories.Count)" -ForegroundColor Green

Write-Host "`n=== All Tests Completed ===" -ForegroundColor Cyan
Write-Host "`nSummary:" -ForegroundColor Yellow
Write-Host "Categories created: $($createdCategories.Count)" -ForegroundColor White
Write-Host "SubCategories created: $($createdSubCategories.Count)" -ForegroundColor White
Write-Host "SubCategories remaining: $($allSubCategories.Count)" -ForegroundColor White
