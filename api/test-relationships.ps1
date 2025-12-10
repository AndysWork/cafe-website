# Test script to demonstrate relationships between Category, SubCategory, and Menu Items
$baseUrl = "http://localhost:7071/api"

Write-Host "`n=== Testing Database Relationships ===" -ForegroundColor Cyan
Write-Host "Category -> SubCategory -> MenuItem" -ForegroundColor Yellow

# Step 1: Create Categories
Write-Host "`n1. Creating Categories..." -ForegroundColor Yellow
$categories = @(
    @{
        Name = "Beverages"
        Description = "Hot and cold drinks"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
    },
    @{
        Name = "Food"
        Description = "Fresh food items"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
    }
)

$createdCategories = @{}
foreach ($cat in $categories) {
    $body = $cat | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Post -Body $body -ContentType "application/json"
    $createdCategories[$result.Name] = $result
    Write-Host "  Created Category: $($result.Name) (ID: $($result.Id))" -ForegroundColor Green
}

# Step 2: Create SubCategories linked to Categories
Write-Host "`n2. Creating SubCategories with Category relationships..." -ForegroundColor Yellow
$beveragesId = $createdCategories["Beverages"].Id
$foodId = $createdCategories["Food"].Id

$subCategories = @(
    @{
        CategoryId = $beveragesId
        Name = "Coffee"
        Description = "Coffee beverages"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
    },
    @{
        CategoryId = $beveragesId
        Name = "Tea"
        Description = "Tea varieties"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
    },
    @{
        CategoryId = $foodId
        Name = "Pastries"
        Description = "Baked goods"
        DisplayOrder = 1
        IsActive = $true
        CreatedBy = "Admin"
    },
    @{
        CategoryId = $foodId
        Name = "Sandwiches"
        Description = "Fresh sandwiches"
        DisplayOrder = 2
        IsActive = $true
        CreatedBy = "Admin"
    }
)

$createdSubCategories = @{}
foreach ($subCat in $subCategories) {
    $body = $subCat | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Post -Body $body -ContentType "application/json"
    $createdSubCategories[$result.Name] = $result
    Write-Host "  Created SubCategory: $($result.Name) -> Category: $($result.CategoryId) (ID: $($result.Id))" -ForegroundColor Green
}

# Step 3: Create Menu Items linked to Category and SubCategory
Write-Host "`n3. Creating Menu Items with Category & SubCategory relationships..." -ForegroundColor Yellow
$coffeeSubCatId = $createdSubCategories["Coffee"].Id
$pastriesSubCatId = $createdSubCategories["Pastries"].Id

$menuItems = @(
    @{
        Name = "Cappuccino"
        Description = "Rich espresso with steamed milk"
        Category = "Coffee"
        CategoryId = $beveragesId
        SubCategoryId = $coffeeSubCatId
        Quantity = 50
        MakingPrice = 2.50
        PackagingCharge = 0.50
        ShopSellingPrice = 4.50
        OnlinePrice = 4.99
        CreatedBy = "Admin"
    },
    @{
        Name = "Latte"
        Description = "Espresso with more milk"
        Category = "Coffee"
        CategoryId = $beveragesId
        SubCategoryId = $coffeeSubCatId
        Quantity = 50
        MakingPrice = 2.75
        PackagingCharge = 0.50
        ShopSellingPrice = 4.75
        OnlinePrice = 5.25
        CreatedBy = "Admin"
    },
    @{
        Name = "Croissant"
        Description = "Buttery French pastry"
        Category = "Pastries"
        CategoryId = $foodId
        SubCategoryId = $pastriesSubCatId
        Quantity = 30
        MakingPrice = 1.20
        PackagingCharge = 0.30
        ShopSellingPrice = 2.50
        OnlinePrice = 2.75
        CreatedBy = "Admin"
    },
    @{
        Name = "Chocolate Croissant"
        Description = "Croissant with chocolate filling"
        Category = "Pastries"
        CategoryId = $foodId
        SubCategoryId = $pastriesSubCatId
        Quantity = 25
        MakingPrice = 1.50
        PackagingCharge = 0.35
        ShopSellingPrice = 3.00
        OnlinePrice = 3.25
        CreatedBy = "Admin"
    }
)

$createdMenuItems = @()
foreach ($item in $menuItems) {
    $body = $item | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/menu" -Method Post -Body $body -ContentType "application/json"
    $createdMenuItems += $result
    Write-Host "  Created MenuItem: $($result.Name) -> SubCategory: $($result.SubCategoryId) -> Category: $($result.CategoryId)" -ForegroundColor Green
}

# Step 4: Verify Relationships
Write-Host "`n4. Verifying Relationships..." -ForegroundColor Yellow

# Get subcategories for Beverages category
Write-Host "`n  a) SubCategories under 'Beverages' category:" -ForegroundColor Cyan
$beverageSubCats = Invoke-RestMethod -Uri "$baseUrl/categories/$beveragesId/subcategories" -Method Get
$beverageSubCats | ForEach-Object { Write-Host "     - $($_.Name)" -ForegroundColor White }

# Get menu items for Beverages category
Write-Host "`n  b) Menu Items under 'Beverages' category:" -ForegroundColor Cyan
$beverageMenuItems = Invoke-RestMethod -Uri "$baseUrl/categories/$beveragesId/menu" -Method Get
$beverageMenuItems | ForEach-Object { Write-Host "     - $($_.Name)" -ForegroundColor White }

# Get menu items for Coffee subcategory
Write-Host "`n  c) Menu Items under 'Coffee' subcategory:" -ForegroundColor Cyan
$coffeeMenuItems = Invoke-RestMethod -Uri "$baseUrl/subcategories/$coffeeSubCatId/menu" -Method Get
$coffeeMenuItems | ForEach-Object { Write-Host "     - $($_.Name)" -ForegroundColor White }

# Get menu items for Pastries subcategory
Write-Host "`n  d) Menu Items under 'Pastries' subcategory:" -ForegroundColor Cyan
$pastriesMenuItems = Invoke-RestMethod -Uri "$baseUrl/subcategories/$pastriesSubCatId/menu" -Method Get
$pastriesMenuItems | ForEach-Object { Write-Host "     - $($_.Name)" -ForegroundColor White }

# Display full hierarchy
Write-Host "`n=== Complete Hierarchy ===" -ForegroundColor Cyan
$allCategories = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Get
foreach ($category in $allCategories) {
    Write-Host "`n$($category.Name) (Category)" -ForegroundColor Yellow
    
    # Get subcategories for this category
    $subCats = Invoke-RestMethod -Uri "$baseUrl/categories/$($category.Id)/subcategories" -Method Get
    foreach ($subCat in $subCats) {
        Write-Host "  +-- $($subCat.Name) (SubCategory)" -ForegroundColor Cyan
        
        # Get menu items for this subcategory
        $items = Invoke-RestMethod -Uri "$baseUrl/subcategories/$($subCat.Id)/menu" -Method Get
        foreach ($item in $items) {
            Write-Host "      +-- $($item.Name) - `$$($item.ShopSellingPrice)" -ForegroundColor White
        }
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Categories: $($allCategories.Count)" -ForegroundColor White
Write-Host "SubCategories: $((Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Get).Count)" -ForegroundColor White
Write-Host "Menu Items: $((Invoke-RestMethod -Uri "$baseUrl/menu" -Method Get).Count)" -ForegroundColor White
Write-Host "`nRelationships verified successfully!" -ForegroundColor Green
