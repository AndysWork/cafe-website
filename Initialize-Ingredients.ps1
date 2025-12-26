# PowerShell script to initialize ingredients in MongoDB
# Run this script from the cafe-website root directory

$ErrorActionPreference = "Stop"

Write-Host "Initializing Maa Tara Cafe Ingredients Database..." -ForegroundColor Cyan
Write-Host "Ensure Azure Functions is running on http://localhost:7071" -ForegroundColor Yellow
Write-Host ""

# Ingredients data (62 items)
$ingredients = @(
    @{ name = "Tea Leaves (Dust)"; category = "beverages"; marketPrice = 400; unit = "kg"; autoUpdate = $false },
    @{ name = "Tea Leaves (Premium)"; category = "beverages"; marketPrice = 600; unit = "kg"; autoUpdate = $false },
    @{ name = "Coffee Powder"; category = "beverages"; marketPrice = 500; unit = "kg"; autoUpdate = $false },
    @{ name = "Milk (Full Cream)"; category = "dairy"; marketPrice = 60; unit = "ltr"; autoUpdate = $true },
    @{ name = "Sugar"; category = "others"; marketPrice = 45; unit = "kg"; autoUpdate = $true },
    @{ name = "Ginger"; category = "vegetables"; marketPrice = 120; unit = "kg"; autoUpdate = $true },
    @{ name = "Cardamom"; category = "spices"; marketPrice = 1500; unit = "kg"; autoUpdate = $false },
    @{ name = "Tea Masala"; category = "spices"; marketPrice = 400; unit = "kg"; autoUpdate = $false },
    @{ name = "Burger Buns"; category = "grains"; marketPrice = 8; unit = "pc"; autoUpdate = $false },
    @{ name = "Chicken Patty"; category = "meat"; marketPrice = 35; unit = "pc"; autoUpdate = $false },
    @{ name = "Veg Patty"; category = "vegetables"; marketPrice = 20; unit = "pc"; autoUpdate = $false },
    @{ name = "Cheese Slices"; category = "dairy"; marketPrice = 200; unit = "kg"; autoUpdate = $false },
    @{ name = "Lettuce"; category = "vegetables"; marketPrice = 60; unit = "kg"; autoUpdate = $true },
    @{ name = "Tomato"; category = "vegetables"; marketPrice = 50; unit = "kg"; autoUpdate = $true },
    @{ name = "Onion (Sliced)"; category = "vegetables"; marketPrice = 40; unit = "kg"; autoUpdate = $true },
    @{ name = "Mayonnaise"; category = "others"; marketPrice = 180; unit = "kg"; autoUpdate = $false },
    @{ name = "Ketchup"; category = "others"; marketPrice = 150; unit = "kg"; autoUpdate = $false },
    @{ name = "Momos Flour (Maida)"; category = "grains"; marketPrice = 50; unit = "kg"; autoUpdate = $true },
    @{ name = "Chicken Mince"; category = "meat"; marketPrice = 280; unit = "kg"; autoUpdate = $true },
    @{ name = "Cabbage"; category = "vegetables"; marketPrice = 30; unit = "kg"; autoUpdate = $true },
    @{ name = "Carrot"; category = "vegetables"; marketPrice = 45; unit = "kg"; autoUpdate = $true },
    @{ name = "Spring Onion"; category = "vegetables"; marketPrice = 80; unit = "kg"; autoUpdate = $false },
    @{ name = "Soy Sauce"; category = "others"; marketPrice = 200; unit = "ltr"; autoUpdate = $false },
    @{ name = "Vinegar"; category = "others"; marketPrice = 100; unit = "ltr"; autoUpdate = $false },
    @{ name = "Garlic"; category = "vegetables"; marketPrice = 100; unit = "kg"; autoUpdate = $true },
    @{ name = "Green Chilli"; category = "vegetables"; marketPrice = 80; unit = "kg"; autoUpdate = $true },
    @{ name = "Schezwan Sauce"; category = "others"; marketPrice = 250; unit = "kg"; autoUpdate = $false },
    @{ name = "Bread Slices"; category = "grains"; marketPrice = 40; unit = "pc"; autoUpdate = $false },
    @{ name = "Butter"; category = "dairy"; marketPrice = 450; unit = "kg"; autoUpdate = $false },
    @{ name = "Capsicum"; category = "vegetables"; marketPrice = 60; unit = "kg"; autoUpdate = $true },
    @{ name = "Cucumber"; category = "vegetables"; marketPrice = 35; unit = "kg"; autoUpdate = $true },
    @{ name = "Paneer"; category = "dairy"; marketPrice = 300; unit = "kg"; autoUpdate = $true },
    @{ name = "Salt"; category = "spices"; marketPrice = 20; unit = "kg"; autoUpdate = $false },
    @{ name = "Black Pepper Powder"; category = "spices"; marketPrice = 600; unit = "kg"; autoUpdate = $false },
    @{ name = "Red Chilli Powder"; category = "spices"; marketPrice = 250; unit = "kg"; autoUpdate = $false },
    @{ name = "Turmeric Powder"; category = "spices"; marketPrice = 200; unit = "kg"; autoUpdate = $false },
    @{ name = "Coriander Powder"; category = "spices"; marketPrice = 180; unit = "kg"; autoUpdate = $false },
    @{ name = "Cumin Seeds"; category = "spices"; marketPrice = 400; unit = "kg"; autoUpdate = $false },
    @{ name = "Garam Masala"; category = "spices"; marketPrice = 500; unit = "kg"; autoUpdate = $false },
    @{ name = "Refined Oil"; category = "oils"; marketPrice = 150; unit = "ltr"; autoUpdate = $true },
    @{ name = "Mustard Oil"; category = "oils"; marketPrice = 180; unit = "ltr"; autoUpdate = $false },
    @{ name = "Ghee"; category = "dairy"; marketPrice = 500; unit = "kg"; autoUpdate = $false },
    @{ name = "Mineral Water Bottle"; category = "beverages"; marketPrice = 20; unit = "ltr"; autoUpdate = $false },
    @{ name = "Cold Drink (Campa Cola)"; category = "beverages"; marketPrice = 15; unit = "pc"; autoUpdate = $false },
    @{ name = "Packaged Juice"; category = "beverages"; marketPrice = 30; unit = "pc"; autoUpdate = $false },
    @{ name = "Biscuits (Parle-G)"; category = "others"; marketPrice = 10; unit = "pc"; autoUpdate = $false },
    @{ name = "Chips Packet"; category = "others"; marketPrice = 10; unit = "pc"; autoUpdate = $false },
    @{ name = "Samosa (Ready)"; category = "others"; marketPrice = 8; unit = "pc"; autoUpdate = $false },
    @{ name = "Potato"; category = "vegetables"; marketPrice = 30; unit = "kg"; autoUpdate = $true },
    @{ name = "Coriander Leaves"; category = "vegetables"; marketPrice = 40; unit = "kg"; autoUpdate = $false },
    @{ name = "Mint Leaves"; category = "vegetables"; marketPrice = 60; unit = "kg"; autoUpdate = $false },
    @{ name = "Lemon"; category = "vegetables"; marketPrice = 80; unit = "kg"; autoUpdate = $true },
    @{ name = "Paper Cups (100ml)"; category = "others"; marketPrice = 2; unit = "pc"; autoUpdate = $false },
    @{ name = "Paper Cups (200ml)"; category = "others"; marketPrice = 3; unit = "pc"; autoUpdate = $false },
    @{ name = "Disposable Plates"; category = "others"; marketPrice = 3; unit = "pc"; autoUpdate = $false },
    @{ name = "Food Packaging Box"; category = "others"; marketPrice = 8; unit = "pc"; autoUpdate = $false },
    @{ name = "Tissue Paper"; category = "others"; marketPrice = 50; unit = "pc"; autoUpdate = $false },
    @{ name = "Plastic Straws"; category = "others"; marketPrice = 0.5; unit = "pc"; autoUpdate = $false },
    @{ name = "Eggs"; category = "meat"; marketPrice = 6; unit = "pc"; autoUpdate = $true },
    @{ name = "Basmati Rice"; category = "grains"; marketPrice = 100; unit = "kg"; autoUpdate = $true },
    @{ name = "Atta (Wheat Flour)"; category = "grains"; marketPrice = 40; unit = "kg"; autoUpdate = $true }
)

$totalIngredients = $ingredients.Count
$created = 0
$skipped = 0
$errors = @()

Write-Host "Processing $totalIngredients ingredients..." -ForegroundColor Cyan
Write-Host ""

foreach ($ing in $ingredients) {
    try {
        $body = @{
            name = $ing.name
            category = $ing.category
            marketPrice = $ing.marketPrice
            unit = $ing.unit
            isActive = $true
            priceSource = "manual"
            autoUpdateEnabled = $ing.autoUpdate
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri "http://localhost:7071/api/ingredients" `
            -Method POST `
            -Body $body `
            -ContentType "application/json" `
            -Headers @{"x-admin-role"="true"} `
            -ErrorAction Stop
        
        Write-Host "Created: $($ing.name) - Rs.$($ing.marketPrice)/$($ing.unit)" -ForegroundColor Green
        $created++
    }
    catch {
        if ($_.Exception.Message -like "*already exists*" -or $_.ErrorDetails.Message -like "*already exists*") {
            Write-Host "Skipped: $($ing.name) (already exists)" -ForegroundColor Yellow
            $skipped++
        }
        else {
            Write-Host "Error: $($ing.name) - $($_.Exception.Message)" -ForegroundColor Red
            $errors += "$($ing.name): $($_.Exception.Message)"
        }
    }
    
    Start-Sleep -Milliseconds 100  # Small delay to avoid overwhelming the API
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Initialization Complete!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Total ingredients: $totalIngredients" -ForegroundColor White
Write-Host "Created: $created" -ForegroundColor Green
Write-Host "Skipped (existing): $skipped" -ForegroundColor Yellow
Write-Host "Errors: $($errors.Count)" -ForegroundColor Red

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    foreach ($error in $errors) {
        Write-Host "  - $error" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Navigate to http://localhost:4200/price-calculator to view ingredients" -ForegroundColor White
Write-Host "2. Create recipes linking menu items to ingredients" -ForegroundColor White
Write-Host "3. Use the price tracking features to monitor market prices" -ForegroundColor White
