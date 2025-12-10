# Simple file upload test
Write-Host "Testing file upload..." -ForegroundColor Cyan

# Create CSV content
$csvContent = @"
CategoryName,CategoryDescription,CategoryDisplayOrder,SubCategoryName,SubCategoryDescription,SubCategoryDisplayOrder
Drinks,All beverages,1,Hot Drinks,Coffee and tea,1
Drinks,All beverages,1,Cold Drinks,Juices and smoothies,2
"@

# Save CSV file
$csvPath = "test-upload.csv"
Set-Content -Path $csvPath -Value $csvContent

# Read file as bytes
$fileBytes = [System.IO.File]::ReadAllBytes($csvPath)
$fileName = "test-upload.csv"

# Create multipart form data
$boundary = [Guid]::NewGuid().ToString()
$bodyLines = @()
$bodyLines += "--$boundary"
$bodyLines += "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`""
$bodyLines += "Content-Type: text/csv"
$bodyLines += ""
$bodyLines += [System.Text.Encoding]::UTF8.GetString($fileBytes)
$bodyLines += "--$boundary"
$bodyLines += "Content-Disposition: form-data; name=`"uploadedBy`""
$bodyLines += ""
$bodyLines += "TestUser"
$bodyLines += "--$boundary--"

$body = $bodyLines -join "`r`n"

try {
    $response = Invoke-WebRequest -Uri "http://localhost:7072/api/upload/categories" `
        -Method Post `
        -Body $body `
        -ContentType "multipart/form-data; boundary=$boundary"
    
    Write-Host "Success!" -ForegroundColor Green
    Write-Host $response.Content
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
}

# Cleanup
Remove-Item $csvPath -Force
