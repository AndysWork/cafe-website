using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using System.Net;

namespace Cafe.Api.Functions;

public class FileUploadFunction
{
    private readonly MongoService _mongo;
    private readonly FileUploadService _fileUploadService;
    private readonly ILogger _log;

    public FileUploadFunction(MongoService mongo, FileUploadService fileUploadService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _fileUploadService = fileUploadService;
        _log = loggerFactory.CreateLogger<FileUploadFunction>();
    }

    [Function("UploadCategoriesFile")]
    public async Task<HttpResponseData> UploadCategoriesFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload/categories")] HttpRequestData req)
    {
        try
        {
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
        
        var content = System.Text.Encoding.UTF8.GetString(buffer);
        var boundaryDelimiter = "--" + boundary;
        var sections = content.Split(new[] { boundaryDelimiter }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var section in sections)
        {
            if (section.Trim() == "--" || string.IsNullOrWhiteSpace(section))
                continue;

            // Find the double CRLF that separates headers from body
            var headerEndIndex = section.IndexOf("\r\n\r\n");
            if (headerEndIndex < 0)
            {
                headerEndIndex = section.IndexOf("\n\n");
                if (headerEndIndex < 0) continue;
            }

            var headers = section.Substring(0, headerEndIndex);
            var bodyStartIndex = headerEndIndex + (section[headerEndIndex + 1] == '\n' && section[headerEndIndex + 2] == '\n' ? 2 : 4);
            var body = section.Substring(bodyStartIndex).TrimEnd('\r', '\n', '-');

            // Parse Content-Disposition
            var dispositionMatch = System.Text.RegularExpressions.Regex.Match(headers, @"name=""([^""]+)""");
            if (!dispositionMatch.Success) continue;

            var fieldName = dispositionMatch.Groups[1].Value;
            var fileNameMatch = System.Text.RegularExpressions.Regex.Match(headers, @"filename=""([^""]+)""");

            var part = new MultipartPart();

            if (fileNameMatch.Success)
            {
                // This is a file field
                part.FileName = fileNameMatch.Groups[1].Value;
                // For file content, convert back to bytes using UTF8
                part.Data = System.Text.Encoding.UTF8.GetBytes(body);
            }
            else
            {
                // This is a text field
                part.Text = body.Trim();
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
}
