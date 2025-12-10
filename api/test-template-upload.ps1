# Test uploading the downloaded template
Write-Host "Uploading downloaded template..." -ForegroundColor Cyan

$csvPath = "f:\MyProducts\CafeWebsite\cafe-website\test-template.csv"

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

try {
    $response = Invoke-RestMethod -Uri "http://localhost:7071/api/upload/categories" `
        -Method Post `
        -Body $body `
        -ContentType "multipart/form-data; boundary=$boundary"
    
    Write-Host "`nUpload Result:" -ForegroundColor Green
    Write-Host "Success: $($response.success)" -ForegroundColor Cyan
    Write-Host "Categories: $($response.categoriesProcessed)" -ForegroundColor Cyan
    Write-Host "SubCategories: $($response.subCategoriesProcessed)" -ForegroundColor Cyan
    Write-Host "Message: $($response.message)" -ForegroundColor White
    
    if ($response.errors -and $response.errors.Count -gt 0) {
        Write-Host "`nErrors:" -ForegroundColor Red
        $response.errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
} catch {
    Write-Host "`nFailed: $_" -ForegroundColor Red
}
