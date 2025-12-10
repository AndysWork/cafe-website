# Test script for file upload functionality
$baseUrl = "http://localhost:7072/api"

Write-Host "`n=== Testing File Upload Feature ===" -ForegroundColor Cyan

# Create a test CSV file
$csvContent = @"
CategoryName,CategoryDescription,CategoryDisplayOrder,SubCategoryName,SubCategoryDescription,SubCategoryDisplayOrder
Drinks,All beverages,1,Hot Drinks,Coffee and tea,1
Drinks,All beverages,1,Cold Drinks,Juices and smoothies,2
Snacks,Light snacks,2,Chips,Potato chips and crisps,1
Snacks,Light snacks,2,Nuts,Mixed nuts and seeds,2
Desserts,Sweet treats,3,Cakes,Fresh cakes,1
Desserts,Sweet treats,3,Ice Cream,Various flavors,2
"@

$csvPath = "f:\MyProducts\CafeWebsite\cafe-website\api\test-categories-upload.csv"
Set-Content -Path $csvPath -Value $csvContent
Write-Host "`nCreated test CSV file: $csvPath" -ForegroundColor Green

# Test 1: Upload CSV file
Write-Host "`n1. Uploading CSV file..." -ForegroundColor Yellow
try {
    # Read file content
    $fileContent = [System.IO.File]::ReadAllBytes($csvPath)
    $fileName = [System.IO.Path]::GetFileName($csvPath)
    
    # Create multipart form data
    $boundary = [System.Guid]::NewGuid().ToString()
    $LF = "`r`n"
    
    $bodyLines = (
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
        "Content-Type: text/csv$LF",
        [System.Text.Encoding]::Latin1.GetString($fileContent),
        "--$boundary",
        "Content-Disposition: form-data; name=`"uploadedBy`"$LF",
        "TestAdmin",
        "--$boundary--$LF"
    ) -join $LF
    
    $result = Invoke-RestMethod -Uri "$baseUrl/upload/categories" -Method Post -Body $bodyLines -ContentType "multipart/form-data; boundary=$boundary"
    
    Write-Host "`n  + Upload Result:" -ForegroundColor Green
    Write-Host "    Success: $($result.success)" -ForegroundColor White
    Write-Host "    Message: $($result.message)" -ForegroundColor White
    Write-Host "    Categories Processed: $($result.categoriesProcessed)" -ForegroundColor Cyan
    Write-Host "    SubCategories Processed: $($result.subCategoriesProcessed)" -ForegroundColor Cyan
    
    if ($result.errors -and $result.errors.Count -gt 0) {
        Write-Host "`n  Errors:" -ForegroundColor Red
        $result.errors | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    }
} catch {
    Write-Host "`n  x Upload failed: $_" -ForegroundColor Red
}

# Test 2: Verify uploaded data
Write-Host "`n2. Verifying uploaded categories..." -ForegroundColor Yellow
try {
    $categories = Invoke-RestMethod -Uri "$baseUrl/categories" -Method Get
    Write-Host "  Total categories in DB: $($categories.Count)" -ForegroundColor Cyan
    $categories | ForEach-Object { 
        Write-Host "    - $($_.Name): $($_.Description)" -ForegroundColor White 
    }
} catch {
    Write-Host "  x Error getting categories: $_" -ForegroundColor Red
}

# Test 3: Verify subcategories
Write-Host "`n3. Verifying uploaded subcategories..." -ForegroundColor Yellow
try {
    $subCategories = Invoke-RestMethod -Uri "$baseUrl/subcategories" -Method Get
    Write-Host "  Total subcategories in DB: $($subCategories.Count)" -ForegroundColor Cyan
    $subCategories | ForEach-Object { 
        Write-Host "    - $($_.Name): $($_.Description)" -ForegroundColor White 
    }
} catch {
    Write-Host "  x Error getting subcategories: $_" -ForegroundColor Red
}

# Test 4: Download template
Write-Host "`n4. Testing template download..." -ForegroundColor Yellow
try {
    $templateUrl = "$baseUrl/upload/categories/template?format=csv"
    Write-Host "  CSV Template URL: $templateUrl" -ForegroundColor Cyan
    Write-Host "  You can download it manually from your browser" -ForegroundColor White
} catch {
    Write-Host "  x Error: $_" -ForegroundColor Red
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Green
Write-Host "`nCleanup: Removing test CSV file..." -ForegroundColor Yellow
Remove-Item -Path $csvPath -Force
Write-Host "Done!" -ForegroundColor Green
