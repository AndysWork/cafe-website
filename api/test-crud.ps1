# CRUD Testing Script for Cafe Menu API
$baseUrl = "http://localhost:7071/api/menu"

Write-Host "`n=== Testing CRUD Operations for Cafe Menu ===" -ForegroundColor Cyan

# Wait a moment to ensure server is ready
Start-Sleep -Seconds 2

# Test 1: GET all items (should be empty initially)
Write-Host "`n1. GET all menu items:" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri $baseUrl -Method Get
    Write-Host "Response: $($response | ConvertTo-Json)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 2: POST - Create a new menu item
Write-Host "`n2. POST - Create new menu item (Cappuccino):" -ForegroundColor Yellow
$newItem = @{
    name = "Cappuccino"
    description = "Rich espresso with steamed milk and foam"
    category = "Coffee"
    quantity = 50
    makingPrice = 2.50
    packagingCharge = 0.50
    shopSellingPrice = 4.50
    onlinePrice = 4.99
    createdBy = "TestUser"
    lastUpdatedBy = "TestUser"
} | ConvertTo-Json

try {
    $created = Invoke-RestMethod -Uri $baseUrl -Method Post -Body $newItem -ContentType "application/json"
    $itemId = $created.id
    Write-Host "Created item ID: $itemId" -ForegroundColor Green
    Write-Host "Response: $($created | ConvertTo-Json)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 3: POST - Create another menu item
Write-Host "`n3. POST - Create new menu item (Croissant):" -ForegroundColor Yellow
$newItem2 = @{
    name = "Croissant"
    description = "Buttery, flaky French pastry"
    category = "Pastries"
    quantity = 30
    makingPrice = 1.50
    packagingCharge = 0.30
    shopSellingPrice = 3.00
    onlinePrice = 3.25
    createdBy = "TestUser"
    lastUpdatedBy = "TestUser"
} | ConvertTo-Json

try {
    $created2 = Invoke-RestMethod -Uri $baseUrl -Method Post -Body $newItem2 -ContentType "application/json"
    $itemId2 = $created2.id
    Write-Host "Created item ID: $itemId2" -ForegroundColor Green
    Write-Host "Response: $($created2 | ConvertTo-Json)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 4: GET all items (should show 2 items)
Write-Host "`n4. GET all menu items (should show 2 items):" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri $baseUrl -Method Get
    Write-Host "Total items: $($response.Count)" -ForegroundColor Green
    $response | ForEach-Object { Write-Host "  - $($_.name) ($($_.category))" -ForegroundColor Cyan }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 5: GET single item by ID
Write-Host "`n5. GET single menu item by ID:" -ForegroundColor Yellow
try {
    $item = Invoke-RestMethod -Uri "$baseUrl/$itemId" -Method Get
    Write-Host "Retrieved: $($item.name)" -ForegroundColor Green
    Write-Host "Response: $($item | ConvertTo-Json)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 6: PUT - Update menu item
Write-Host "`n6. PUT - Update menu item (increase price):" -ForegroundColor Yellow
$updateItem = @{
    id = $itemId
    name = "Cappuccino Deluxe"
    description = "Premium espresso with steamed milk and foam"
    category = "Coffee"
    quantity = 50
    makingPrice = 2.80
    packagingCharge = 0.50
    shopSellingPrice = 5.00
    onlinePrice = 5.49
    createdBy = "TestUser"
    lastUpdatedBy = "UpdateUser"
} | ConvertTo-Json

try {
    $updated = Invoke-RestMethod -Uri "$baseUrl/$itemId" -Method Put -Body $updateItem -ContentType "application/json"
    Write-Host "Updated item: $($updated.name)" -ForegroundColor Green
    Write-Host "New price: $($updated.shopSellingPrice)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 7: GET updated item
Write-Host "`n7. GET updated menu item:" -ForegroundColor Yellow
try {
    $item = Invoke-RestMethod -Uri "$baseUrl/$itemId" -Method Get
    Write-Host "Name: $($item.name)" -ForegroundColor Green
    Write-Host "Price: $($item.shopSellingPrice)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 8: DELETE menu item
Write-Host "`n8. DELETE menu item:" -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/$itemId" -Method Delete
    Write-Host "Item deleted successfully" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 9: GET all items (should show 1 item remaining)
Write-Host "`n9. GET all menu items (should show 1 item):" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri $baseUrl -Method Get
    Write-Host "Total items: $($response.Count)" -ForegroundColor Green
    $response | ForEach-Object { Write-Host "  - $($_.name) ($($_.category))" -ForegroundColor Cyan }
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 10: DELETE the second item
Write-Host "`n10. DELETE second menu item:" -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/$itemId2" -Method Delete
    Write-Host "Item deleted successfully" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

# Test 11: GET all items (should be empty)
Write-Host "`n11. GET all menu items (should be empty):" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri $baseUrl -Method Get
    Write-Host "Total items: $($response.Count)" -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
}

Write-Host "`n=== CRUD Testing Complete ===" -ForegroundColor Cyan
