# Test file upload to API
Write-Host "Testing File Upload Feature" -ForegroundColor Cyan

# Create test CSV content
$csvContent = @"
CategoryName,CategoryDescription,CategoryDisplayOrder,SubCategoryName,SubCategoryDescription,SubCategoryDisplayOrder
TestCategory1,Test Category 1 Description,1,TestSub1,Test SubCategory 1,1
TestCategory1,Test Category 1 Description,1,TestSub2,Test SubCategory 2,2
TestCategory2,Test Category 2 Description,2,TestSub3,Test SubCategory 3,1
"@

# Save to file
$csvPath = "$PSScriptRoot\test-upload.csv"
Set-Content -Path $csvPath -Value $csvContent -Encoding UTF8
Write-Host "Created test CSV: $csvPath" -ForegroundColor Green

# Prepare multipart form data
$boundary = [System.Guid]::NewGuid().ToString()
$LF = "`r`n"

# Read file as bytes
$fileBytes = [System.IO.File]::ReadAllBytes($csvPath)
$fileName = [System.IO.Path]::GetFileName($csvPath)

# Build multipart body
$bodyLines = @()
$bodyLines += "--$boundary"
$bodyLines += "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`""
$bodyLines += "Content-Type: text/csv"
$bodyLines += ""
$bodyLines += [System.Text.Encoding]::UTF8.GetString($fileBytes)
$bodyLines += "--$boundary"
$bodyLines += "Content-Disposition: form-data; name=`"uploadedBy`""
$bodyLines += ""
$bodyLines += "PowerShellTest"
$bodyLines += "--$boundary--"

$body = $bodyLines -join $LF

Write-Host "`nUploading file to API..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri "http://localhost:7071/api/upload/categories" `
        -Method Post `
        -Body $body `
        -ContentType "multipart/form-data; boundary=$boundary"
    
    Write-Host "`nUpload Successful!" -ForegroundColor Green
    Write-Host "Categories Processed: $($response.categoriesProcessed)" -ForegroundColor Cyan
    Write-Host "SubCategories Processed: $($response.subCategoriesProcessed)" -ForegroundColor Cyan
    Write-Host "Message: $($response.message)" -ForegroundColor White
    
    if ($response.errors -and $response.errors.Count -gt 0) {
        Write-Host "`nErrors:" -ForegroundColor Red
        $response.errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    
    # Verify in database
    Write-Host "`nVerifying data in database..." -ForegroundColor Yellow
    $categories = Invoke-RestMethod -Uri "http://localhost:7071/api/categories" -Method Get
    Write-Host "Total categories in DB: $($categories.Count)" -ForegroundColor Cyan
    
    $subcategories = Invoke-RestMethod -Uri "http://localhost:7071/api/subcategories" -Method Get
    Write-Host "Total subcategories in DB: $($subcategories.Count)" -ForegroundColor Cyan
    
} catch {
    Write-Host "`nUpload Failed!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response: $responseBody" -ForegroundColor Yellow
    }
}

# Cleanup
Write-Host "`nCleaning up test file..." -ForegroundColor Yellow
Remove-Item -Path $csvPath -Force
Write-Host "Done!" -ForegroundColor Green
