using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class CashReconciliationFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public CashReconciliationFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<CashReconciliationFunction>();
    }

    [Function("GetCashReconciliations")]
    public async Task<HttpResponseData> GetCashReconciliations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-reconciliation")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            DateTime? startDate = null;
            DateTime? endDate = null;

            if (DateTime.TryParse(query["startDate"], out var start))
                startDate = start;
            if (DateTime.TryParse(query["endDate"], out var end))
                endDate = end;

            var reconciliations = await _mongo.GetCashReconciliationsAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = reconciliations });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting cash reconciliations");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve cash reconciliations" });
            return errorRes;
        }
    }

    [Function("GetCashReconciliationByDate")]
    public async Task<HttpResponseData> GetCashReconciliationByDate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-reconciliation/date/{date}")] HttpRequestData req,
        string date)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid date format" });
                return badRequest;
            }

            var reconciliation = await _mongo.GetCashReconciliationByDateAsync(parsedDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = reconciliation });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting cash reconciliation by date");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve cash reconciliation" });
            return errorRes;
        }
    }

    [Function("GetDailySalesSummary")]
    public async Task<HttpResponseData> GetDailySalesSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-reconciliation/sales-summary/{date}")] HttpRequestData req,
        string date)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid date format" });
                return badRequest;
            }

            var summary = await _mongo.GetDailySalesSummaryForReconciliationAsync(parsedDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = summary });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting daily sales summary");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve sales summary" });
            return errorRes;
        }
    }

    [Function("CreateCashReconciliation")]
    public async Task<HttpResponseData> CreateCashReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cash-reconciliation")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var request = await req.ReadFromJsonAsync<CreateDailyCashReconciliationRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(request, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Check if reconciliation already exists for this date
            var existing = await _mongo.GetCashReconciliationByDateAsync(request.Date);
            if (existing != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { success = false, error = "Reconciliation already exists for this date" });
                return conflict;
            }

            var reconciliation = new DailyCashReconciliation
            {
                Date = request.Date,
                ExpectedCash = request.ExpectedCash,
                ExpectedCoins = request.ExpectedCoins,
                ExpectedOnline = 0, // User manually tracks actual online only
                CountedCash = request.CountedCash,
                CountedCoins = request.CountedCoins,
                ActualOnline = request.ActualOnline,
                Notes = request.Notes
            };

            var created = await _mongo.CreateCashReconciliationAsync(reconciliation, userId!);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = created });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating cash reconciliation");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to create cash reconciliation" });
            return errorRes;
        }
    }

    [Function("UpdateCashReconciliation")]
    public async Task<HttpResponseData> UpdateCashReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cash-reconciliation/{id}")] HttpRequestData req,
        string id)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var request = await req.ReadFromJsonAsync<UpdateDailyCashReconciliationRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(request, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Get existing reconciliation
            var existing = await _mongo.GetCashReconciliationsAsync();
            var current = existing.FirstOrDefault(r => r.Id == id);
            
            if (current == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Reconciliation not found" });
                return notFound;
            }

            // Update fields
            current.CountedCash = request.CountedCash;
            current.CountedCoins = request.CountedCoins;
            current.ActualOnline = request.ActualOnline;
            current.Notes = request.Notes;
            current.IsReconciled = request.IsReconciled;

            var updated = await _mongo.UpdateCashReconciliationAsync(id, current);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = updated });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating cash reconciliation");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update cash reconciliation" });
            return errorRes;
        }
    }

    [Function("BulkCreateCashReconciliations")]
    public async Task<HttpResponseData> BulkCreateCashReconciliations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cash-reconciliation/bulk")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var request = await req.ReadFromJsonAsync<BulkReconciliationRequest>();
            if (request == null || request.Records == null || request.Records.Count == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request or no records provided" });
                return badRequest;
            }

            var reconciliations = request.Records.Select(r => new DailyCashReconciliation
            {
                Date = r.Date,
                ExpectedCash = r.ExpectedCash,
                ExpectedCoins = r.ExpectedCoins,
                ExpectedOnline = 0, // User manually tracks actual online only
                CountedCash = r.CountedCash,
                CountedCoins = r.CountedCoins,
                ActualOnline = r.ActualOnline,
                Notes = r.Notes
            }).ToList();

            var created = await _mongo.BulkCreateCashReconciliationsAsync(reconciliations, userId!);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = created, count = created.Count });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error bulk creating cash reconciliations");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to create cash reconciliations" });
            return errorRes;
        }
    }

    [Function("DeleteCashReconciliation")]
    public async Task<HttpResponseData> DeleteCashReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cash-reconciliation/{id}")] HttpRequestData req,
        string id)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var deleted = await _mongo.DeleteCashReconciliationAsync(id);
            
            if (!deleted)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Reconciliation not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Reconciliation deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting cash reconciliation");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete cash reconciliation" });
            return errorRes;
        }
    }

    [Function("GetCashReconciliationSummary")]
    public async Task<HttpResponseData> GetCashReconciliationSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-reconciliation/summary")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            
            if (!DateTime.TryParse(query["startDate"], out var startDate) ||
                !DateTime.TryParse(query["endDate"], out var endDate))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Start date and end date are required" });
                return badRequest;
            }

            var summary = await _mongo.GetCashReconciliationSummaryAsync(startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = summary });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting cash reconciliation summary");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve summary" });
            return errorRes;
        }
    }

    [Function("BulkUploadCashReconciliation")]
    public async Task<HttpResponseData> BulkUploadCashReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cash-reconciliation/bulk-upload")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            // Read the file from request body
            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "No file data received" });
                return badRequest;
            }

            // Extract file from multipart
            var fileData = ExtractFileFromMultipart(fileBytes);
            
            if (fileData == null || fileData.Length == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "No valid file found in request" });
                return badRequest;
            }

            // Process the file (CSV or Excel)
            var reconciliations = ParseReconciliationFile(fileData);

            if (reconciliations == null || reconciliations.Count == 0)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "No valid reconciliation data found in file" });
                return badRequest;
            }

            // Save reconciliations
            var result = await _mongo.BulkCreateCashReconciliationsAsync(reconciliations, userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, count = result.Count, data = result });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading bulk reconciliation file");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to upload file: " + ex.Message });
            return errorRes;
        }
    }

    private byte[] ExtractFileFromMultipart(byte[] data)
    {
        try
        {
            // Look for Excel file signature (PK\x03\x04 for .xlsx files)
            var excelSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == excelSignature[0] &&
                    data[i + 1] == excelSignature[1] &&
                    data[i + 2] == excelSignature[2] &&
                    data[i + 3] == excelSignature[3])
                {
                    var excelData = new byte[data.Length - i];
                    Array.Copy(data, i, excelData, 0, excelData.Length);
                    return excelData;
                }
            }

            // If no Excel signature, might be CSV - return entire body
            return data;
        }
        catch
        {
            return data;
        }
    }

    private List<DailyCashReconciliation> ParseReconciliationFile(byte[] fileData)
    {
        var reconciliations = new List<DailyCashReconciliation>();

        try
        {
            // Try to parse as CSV first (check for text content and commas/tabs)
            var csvText = System.Text.Encoding.UTF8.GetString(fileData);
            
            // Clean up line endings and check if it looks like CSV
            csvText = csvText.Replace("\r\n", "\n").Replace("\r", "\n");
            
            if ((csvText.Contains(',') || csvText.Contains('\t')) && (csvText.Contains('\n') || csvText.Split(new[] { ',', '\t' }).Length >= 4))
            {
                _log.LogInformation("Parsing as CSV/TSV file");
                var result = ParseCsvReconciliation(csvText);
                if (result.Count > 0)
                {
                    _log.LogInformation($"Successfully parsed {result.Count} records from CSV");
                    return result;
                }
            }

            // Otherwise try as Excel
            _log.LogInformation("Attempting to parse as Excel file");
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var stream = new MemoryStream(fileData))
            using (var package = new OfficeOpenXml.ExcelPackage(stream))
            {
                if (package.Workbook.Worksheets.Count == 0)
                {
                    _log.LogWarning("Excel file has no worksheets");
                    return reconciliations;
                }

                var worksheet = package.Workbook.Worksheets[0];
                var rowCount = worksheet.Dimension?.Rows ?? 0;
                
                _log.LogInformation($"Excel file has {rowCount} rows");

                // Start from row 2 (skip header)
                for (int row = 2; row <= rowCount; row++)
                {
                    var dateStr = worksheet.Cells[row, 1].Text;
                    if (string.IsNullOrWhiteSpace(dateStr) || dateStr.StartsWith("#")) continue;

                    if (DateTime.TryParse(dateStr, out var date))
                    {
                        reconciliations.Add(new DailyCashReconciliation
                        {
                            Date = date,
                            CountedCash = decimal.TryParse(worksheet.Cells[row, 2].Text, out var cash) ? cash : 0,
                            CountedCoins = decimal.TryParse(worksheet.Cells[row, 3].Text, out var coins) ? coins : 0,
                            ActualOnline = decimal.TryParse(worksheet.Cells[row, 4].Text, out var online) ? online : 0,
                            Notes = worksheet.Cells[row, 5].Text
                        });
                    }
                }
                
                _log.LogInformation($"Successfully parsed {reconciliations.Count} records from Excel");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error parsing reconciliation file");
        }

        return reconciliations;
    }

    private List<DailyCashReconciliation> ParseCsvReconciliation(string csvText)
    {
        var reconciliations = new List<DailyCashReconciliation>();
        
        // Normalize line endings
        csvText = csvText.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = csvText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
            .ToList();

        _log.LogInformation($"CSV has {lines.Count} non-empty lines");

        // Skip header (first line)
        for (int i = 1; i < lines.Count; i++)
        {
            // Try tab-separated first, then comma-separated
            var parts = lines[i].Contains('\t') 
                ? lines[i].Split('\t').Select(p => p.Trim()).ToArray()
                : lines[i].Split(',').Select(p => p.Trim()).ToArray();
                
            _log.LogInformation($"Line {i}: {lines[i]} -> {parts.Length} parts");
            
            if (parts.Length >= 4)
            {
                if (DateTime.TryParse(parts[0], out var date))
                {
                    var reconciliation = new DailyCashReconciliation
                    {
                        Date = date,
                        CountedCash = decimal.TryParse(parts[1], out var cash) ? cash : 0,
                        CountedCoins = decimal.TryParse(parts[2], out var coins) ? coins : 0,
                        ActualOnline = decimal.TryParse(parts[3], out var online) ? online : 0,
                        Notes = parts.Length > 4 ? parts[4] : string.Empty
                    };
                    reconciliations.Add(reconciliation);
                    _log.LogInformation($"Added reconciliation for {date:yyyy-MM-dd}: Cash={cash}, Coins={coins}, Online={online}");
                }
                else
                {
                    _log.LogWarning($"Could not parse date from: {parts[0]}");
                }
            }
            else
            {
                _log.LogWarning($"Line {i} has insufficient columns: {parts.Length}");
            }
        }

        _log.LogInformation($"Parsed {reconciliations.Count} reconciliation records from CSV");
        return reconciliations;
    }
}
