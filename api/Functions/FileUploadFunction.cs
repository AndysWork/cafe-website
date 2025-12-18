using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class FileUploadFunction
{
    private readonly MongoService _mongo;
    private readonly FileUploadService _fileUploadService;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public FileUploadFunction(MongoService mongo, FileUploadService fileUploadService, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _fileUploadService = fileUploadService;
        _auth = auth;
        _log = loggerFactory.CreateLogger<FileUploadFunction>();
    }

    [Function("UploadCategoriesFile")]
    public async Task<HttpResponseData> UploadCategoriesFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload/categories")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Read the content type header
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            
            if (!contentType.Contains("multipart/form-data"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data" });
                return badRequest;
            }

            // Read the body as stream
            using var bodyStream = new MemoryStream();
            await req.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;

            var boundary = contentType.Split("boundary=")[1];
            var parts = ParseMultipartFormData(bodyStream, boundary);

            if (!parts.ContainsKey("file") || parts["file"].Data == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "No file uploaded" });
                return badRequest;
            }

            var fileData = parts["file"];
            var fileName = fileData.FileName?.ToLower() ?? "";
            var uploadedBy = parts.ContainsKey("uploadedBy") && parts["uploadedBy"].Text != null
                ? parts["uploadedBy"].Text
                : "Unknown";

            FileUploadService.UploadResult result;

            using (var stream = new MemoryStream(fileData.Data!))
            {
                if (fileName.EndsWith(".xlsx") || fileName.EndsWith(".xls"))
                {
                    result = await _fileUploadService.ProcessExcelFile(stream, _mongo, uploadedBy);
                }
                else if (fileName.EndsWith(".csv"))
                {
                    result = await _fileUploadService.ProcessCsvFile(stream, _mongo, uploadedBy);
                }
                else
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid file format. Only .xlsx, .xls, and .csv files are supported" });
                    return badRequest;
                }
            }

            if (result.Success)
            {
                var res = req.CreateResponse(HttpStatusCode.OK);
                await res.WriteAsJsonAsync(result);
                return res;
            }
            else
            {
                var res = req.CreateResponse(HttpStatusCode.BadRequest);
                await res.WriteAsJsonAsync(result);
                return res;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading categories file");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    private class MultipartPart
    {
        public string? FileName { get; set; }
        public byte[]? Data { get; set; }
        public string? Text { get; set; }
    }

    private Dictionary<string, MultipartPart> ParseMultipartFormData(Stream stream, string boundary)
    {
        var parts = new Dictionary<string, MultipartPart>();
        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        
        stream.Position = 0;
        var buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        
        // Find all boundary positions
        var boundaryPositions = new List<int>();
        for (int i = 0; i <= buffer.Length - boundaryBytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < boundaryBytes.Length; j++)
            {
                if (buffer[i + j] != boundaryBytes[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                boundaryPositions.Add(i);
            }
        }

        // Process each section between boundaries
        for (int i = 0; i < boundaryPositions.Count - 1; i++)
        {
            var start = boundaryPositions[i] + boundaryBytes.Length;
            var end = boundaryPositions[i + 1];
            var sectionBytes = new byte[end - start];
            Array.Copy(buffer, start, sectionBytes, 0, sectionBytes.Length);

            // Find header end (double CRLF or double LF)
            int headerEnd = -1;
            for (int j = 0; j < sectionBytes.Length - 3; j++)
            {
                if ((sectionBytes[j] == '\r' && sectionBytes[j + 1] == '\n' && 
                     sectionBytes[j + 2] == '\r' && sectionBytes[j + 3] == '\n') ||
                    (sectionBytes[j] == '\n' && sectionBytes[j + 1] == '\n'))
                {
                    headerEnd = j;
                    break;
                }
            }

            if (headerEnd < 0) continue;

            var headerBytes = new byte[headerEnd];
            Array.Copy(sectionBytes, 0, headerBytes, 0, headerEnd);
            var headers = System.Text.Encoding.UTF8.GetString(headerBytes);

            // Parse headers
            var dispositionMatch = System.Text.RegularExpressions.Regex.Match(headers, @"name=""([^""]+)""");
            if (!dispositionMatch.Success) continue;

            var fieldName = dispositionMatch.Groups[1].Value;
            var fileNameMatch = System.Text.RegularExpressions.Regex.Match(headers, @"filename=""([^""]+)""");

            var part = new MultipartPart();

            // Calculate body start
            int bodyStart = headerEnd + (sectionBytes[headerEnd] == '\r' ? 4 : 2);
            int bodyLength = sectionBytes.Length - bodyStart;
            
            // Trim trailing CRLF or LF
            while (bodyLength > 0 && (sectionBytes[bodyStart + bodyLength - 1] == '\n' || 
                                       sectionBytes[bodyStart + bodyLength - 1] == '\r'))
            {
                bodyLength--;
            }

            if (fileNameMatch.Success)
            {
                // This is a file - keep as binary
                part.FileName = fileNameMatch.Groups[1].Value;
                part.Data = new byte[bodyLength];
                Array.Copy(sectionBytes, bodyStart, part.Data, 0, bodyLength);
            }
            else
            {
                // This is text - decode to string
                var textBytes = new byte[bodyLength];
                Array.Copy(sectionBytes, bodyStart, textBytes, 0, bodyLength);
                part.Text = System.Text.Encoding.UTF8.GetString(textBytes).Trim();
            }

            parts[fieldName] = part;
        }

        return parts;
    }

    [Function("DownloadCategoriesTemplate")]
    public async Task<HttpResponseData> DownloadCategoriesTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "upload/categories/template")] HttpRequestData req)
    {
        try
        {
            var format = req.Query["format"] ?? "csv";

            if (format.ToLower() == "csv")
            {
                var csvContent = "CategoryName,SubCategoryName\n" +
                                "Beverages,Coffee\n" +
                                "Beverages,Tea\n" +
                                "Food,Pastries\n" +
                                "Food,Sandwiches";

                var res = req.CreateResponse(HttpStatusCode.OK);
                res.Headers.Add("Content-Type", "text/csv");
                res.Headers.Add("Content-Disposition", "attachment; filename=categories_template.csv");
                await res.WriteStringAsync(csvContent);
                return res;
            }
            else
            {
                // For Excel, create a simple template
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using var package = new OfficeOpenXml.ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Categories");

                // Headers
                worksheet.Cells[1, 1].Value = "CategoryName";
                worksheet.Cells[1, 2].Value = "SubCategoryName";

                // Sample data
                worksheet.Cells[2, 1].Value = "Beverages";
                worksheet.Cells[2, 2].Value = "Coffee";

                worksheet.Cells[3, 1].Value = "Beverages";
                worksheet.Cells[3, 2].Value = "Tea";

                worksheet.Cells[4, 1].Value = "Food";
                worksheet.Cells[4, 2].Value = "Pastries";

                worksheet.Cells[5, 1].Value = "Food";
                worksheet.Cells[5, 2].Value = "Sandwiches";

                // Auto-fit columns
                worksheet.Cells.AutoFitColumns();

                var excelBytes = package.GetAsByteArray();
                var res = req.CreateResponse(HttpStatusCode.OK);
                res.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                res.Headers.Add("Content-Disposition", "attachment; filename=categories_template.xlsx");
                await res.WriteBytesAsync(excelBytes);
                return res;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating template");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // POST /api/upload/online-sales - Upload Zomato/Swiggy Excel
    [Function("UploadOnlineSales")]
    public async Task<HttpResponseData> UploadOnlineSales(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload/online-sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? "";
            
            if (!contentType.Contains("multipart/form-data") || !contentType.Contains("boundary="))
            {
                var res = req.CreateResponse(HttpStatusCode.BadRequest);
                await res.WriteAsJsonAsync(new { error = "Invalid content type. Expected multipart/form-data" });
                return res;
            }

            var boundary = contentType.Split("boundary=")[1];
            
            using var bodyStream = new MemoryStream();
            await req.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;

            var parts = ParseMultipartFormData(bodyStream, boundary);

            if (!parts.ContainsKey("file") || parts["file"].Data == null)
            {
                var res = req.CreateResponse(HttpStatusCode.BadRequest);
                await res.WriteAsJsonAsync(new { error = "No file uploaded" });
                return res;
            }

            if (!parts.ContainsKey("platform") || string.IsNullOrEmpty(parts["platform"].Text))
            {
                var res = req.CreateResponse(HttpStatusCode.BadRequest);
                await res.WriteAsJsonAsync(new { error = "Platform (Zomato/Swiggy) is required" });
                return res;
            }

            var platform = parts["platform"].Text!.Trim();
            if (platform != "Zomato" && platform != "Swiggy")
            {
                var res = req.CreateResponse(HttpStatusCode.BadRequest);
                await res.WriteAsJsonAsync(new { error = "Platform must be either 'Zomato' or 'Swiggy'" });
                return res;
            }

            var fileData = parts["file"].Data!;
            using var ms = new MemoryStream(fileData);

            var fileUploadService = new FileUploadService();
            var onlineResult = await fileUploadService.ProcessOnlineSaleExcel(ms, platform, _mongo, userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = onlineResult.Success,
                message = onlineResult.Message,
                salesProcessed = onlineResult.SalesProcessed,
                totalRowsInFile = onlineResult.SalesProcessed + onlineResult.Errors.Count,
                errors = onlineResult.Errors,
                hasErrors = onlineResult.Errors.Any()
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error uploading online sales file");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}

