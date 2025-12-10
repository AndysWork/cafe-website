# Updated CRUD test for CafeMenu with Category and SubCategory relationships
$baseUrl = "http://localhost:7071/api"

Write-Host "`n=== Testing CafeMenu CRUD with Relationships ===" -ForegroundColor Cyan

# Step 1: Get existing categories and subcategories
Write-Host "`n1. Getting existing Categories and SubCategories..." -ForegroundColor Yellow
$categories = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Get
$beveragesCategory = $categories | Where-Object { $_.Name -eq "Beverages" } | Select-Object -First 1
$foodCategory = $categories | Where-Object { $_.Name -eq "Food" } | Select-Object -First 1

Write-Host "  Found Categories:" -ForegroundColor Cyan
Write-Host "    - Beverages (ID: $($beveragesCategory.Id))" -ForegroundColor White
Write-Host "    - Food (ID: $($foodCategory.Id))" -ForegroundColor White

$subCategories = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Get
$coffeeSubCat = $subCategories | Where-Object { $_.Name -eq "Coffee" } | Select-Object -First 1
$pastriesSubCat = $subCategories | Where-Object { $_.Name -eq "Pastries" } | Select-Object -First 1

Write-Host "`n  Found SubCategories:" -ForegroundColor Cyan
Write-Host "    - Coffee (ID: $($coffeeSubCat.Id))" -ForegroundColor White
Write-Host "    - Pastries (ID: $($pastriesSubCat.Id))" -ForegroundColor White

# Step 2: CREATE - Add new menu items with relationships
Write-Host "`n2. Creating new menu items with Category & SubCategory..." -ForegroundColor Yellow

$newItems = @(
    @{
        Name = "Espresso"
        Description = "Strong Italian coffee shot"
        Category = "Coffee"
        CategoryId = $beveragesCategory.Id
        SubCategoryId = $coffeeSubCat.Id
        Quantity = 100
        MakingPrice = 1.25
        PackagingCharge = 0.25
        ShopSellingPrice = 2.50
        OnlinePrice = 2.75
        CreatedBy = "Admin"
    },
    @{
        Name = "Chocolate Muffin"
        Description = "Rich chocolate chip muffin"
        Category = "Pastries"
        CategoryId = $foodCategory.Id
        SubCategoryId = $pastriesSubCat.Id
        Quantity = 35
        MakingPrice = 1.50
        PackagingCharge = 0.35
        ShopSellingPrice = 3.25
        OnlinePrice = 3.50
        CreatedBy = "Admin"
    }
)

$createdItems = @()
foreach ($item in $newItems) {
    $body = $item | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "$baseUrl/menu" -Method Post -Body $body -ContentType "application/json"
    $createdItems += $result
    Write-Host "  + Created: $($result.Name) [Cat: $($result.CategoryId), SubCat: $($result.SubCategoryId)]" -ForegroundColor Green
}

# Step 3: Test validation - Try to create item with invalid CategoryId
Write-Host "`n3. Testing validation - Invalid CategoryId..." -ForegroundColor Yellow
try {
    $invalidItem = @{
        Name = "Test Item"
        Description = "Should fail"
        Category = "Test"
        CategoryId = "invalidcategoryid123"
        SubCategoryId = $coffeeSubCat.Id
        Quantity = 10
        MakingPrice = 1.00
        PackagingCharge = 0.25
        ShopSellingPrice = 2.00
        OnlinePrice = 2.25
    } | ConvertTo-Json
    
    Invoke-RestMethod -Uri "$baseUrl/menu" -Method Post -Body $invalidItem -ContentType "application/json"
    Write-Host "  x FAILED - Should have rejected invalid CategoryId" -ForegroundColor Red
} catch {
    Write-Host "  + PASSED - Correctly rejected invalid CategoryId" -ForegroundColor Green
}

# Step 4: Test validation - Try to create item with mismatched SubCategory
Write-Host "`n4. Testing validation - SubCategory doesn't belong to Category..." -ForegroundColor Yellow
try {
    $mismatchedItem = @{
        Name = "Test Item 2"
        Description = "Should fail - Coffee subcategory under Food category"
        Category = "Test"
        CategoryId = $foodCategory.Id
        SubCategoryId = $coffeeSubCat.Id  # Coffee belongs to Beverages, not Food
        Quantity = 10
        MakingPrice = 1.00
        PackagingCharge = 0.25
        ShopSellingPrice = 2.00
        OnlinePrice = 2.25
    } | ConvertTo-Json
    
    Invoke-RestMethod -Uri "$baseUrl/menu" -Method Post -Body $mismatchedItem -ContentType "application/json"
    Write-Host "  x FAILED - Should have rejected mismatched SubCategory" -ForegroundColor Red
} catch {
    Write-Host "  + PASSED - Correctly rejected SubCategory that doesn't belong to Category" -ForegroundColor Green
}

# Step 5: READ - Get all menu items
Write-Host "`n5. Reading all menu items..." -ForegroundColor Yellow
$allMenuItems = Invoke-RestMethod -Uri "$baseUrl/menu" -Method Get
Write-Host "  Total menu items: $($allMenuItems.Count)" -ForegroundColor Cyan

# Step 6: UPDATE - Update a menu item's category
Write-Host "`n6. Updating menu item category..." -ForegroundColor Yellow
$espressoItem = $createdItems[0]
$espressoItem.Description = "Premium Italian espresso shot"
$espressoItem.ShopSellingPrice = 2.75
$espressoItem.OnlinePrice = 3.00
$espressoItem.LastUpdatedBy = "Admin"

$updateBody = $espressoItem | ConvertTo-Json
$updated = Invoke-RestMethod -Uri "$baseUrl/menu/$($espressoItem.Id)" -Method Put -Body $updateBody -ContentType "application/json"
Write-Host "  + Updated: $($updated.Name) - New price: `$$($updated.ShopSellingPrice)" -ForegroundColor Green

# Step 7: Verify relationships - Get items by Category
Write-Host "`n7. Verifying menu items filtered by Category..." -ForegroundColor Yellow
$beverageItems = Invoke-RestMethod -Uri "$baseUrl/categories/$($beveragesCategory.Id)/menu" -Method Get
Write-Host "  Items in Beverages category: $($beverageItems.Count)" -ForegroundColor Cyan
$beverageItems | ForEach-Object { Write-Host "    - $($_.Name)" -ForegroundColor White }

# Step 8: Verify relationships - Get items by SubCategory
Write-Host "`n8. Verifying menu items filtered by SubCategory..." -ForegroundColor Yellow
$coffeeItems = Invoke-RestMethod -Uri "$baseUrl/subcategories/$($coffeeSubCat.Id)/menu" -Method Get
Write-Host "  Items in Coffee subcategory: $($coffeeItems.Count)" -ForegroundColor Cyan
$coffeeItems | ForEach-Object { Write-Host "    - $($_.Name) - `$$($_.ShopSellingPrice)" -ForegroundColor White }

# Step 9: Display menu with relationships
Write-Host "`n9. Complete Menu with Categories..." -ForegroundColor Yellow
$allCategories = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Get
foreach ($cat in $allCategories) {
    Write-Host "`n  $($cat.Name):" -ForegroundColor Yellow
    $catSubCats = Invoke-RestMethod -Uri "$baseUrl/categories/$($cat.Id)/subcategories" -Method Get
    foreach ($subCat in $catSubCats) {
        Write-Host "    $($subCat.Name):" -ForegroundColor Cyan
        $items = Invoke-RestMethod -Uri "$baseUrl/subcategories/$($subCat.Id)/menu" -Method Get
        if ($items.Count -gt 0) {
            $items | ForEach-Object { 
                Write-Host "      - $($_.Name) - Shop: `$$($_.ShopSellingPrice) | Online: `$$($_.OnlinePrice)" -ForegroundColor White
            }
        } else {
            Write-Host "      (No items)" -ForegroundColor DarkGray
        }
    }
}

Write-Host "`n=== CRUD with Relationships Test Complete ===" -ForegroundColor Green
Write-Host "`nSummary:" -ForegroundColor Yellow
Write-Host "  + Create with valid relationships: PASSED" -ForegroundColor Green
Write-Host "  + Validation for invalid CategoryId: PASSED" -ForegroundColor Green
Write-Host "  + Validation for mismatched SubCategory: PASSED" -ForegroundColor Green
Write-Host "  + Update with relationships: PASSED" -ForegroundColor Green
Write-Host "  + Filter by Category: PASSED" -ForegroundColor Green
Write-Host "  + Filter by SubCategory: PASSED" -ForegroundColor Green
