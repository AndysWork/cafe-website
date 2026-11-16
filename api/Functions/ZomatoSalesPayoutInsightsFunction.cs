using Cafe.Api.Helpers;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class ZomatoSalesPayoutInsightsFunction
{
    private readonly AuthService _auth;
    private readonly MongoService _mongo;
    private readonly ZomatoSalesPayoutInsightService _insightService;
    private readonly ILogger _log;

    public ZomatoSalesPayoutInsightsFunction(
        AuthService auth,
        MongoService mongo,
        ZomatoSalesPayoutInsightService insightService,
        ILoggerFactory loggerFactory)
    {
        _auth = auth;
        _mongo = mongo;
        _insightService = insightService;
        _log = loggerFactory.CreateLogger<ZomatoSalesPayoutInsightsFunction>();
    }

    [Function("ParseZomatoSalesPayoutInsights")]
    public async Task<HttpResponseData> ParseZomatoSalesPayoutInsights(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/zomato-sales-payout/insights")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badOutlet = req.CreateResponse(HttpStatusCode.BadRequest);
                await badOutlet.WriteAsJsonAsync(new { success = false, error = "Outlet context is required. Please select an outlet and try again." });
                return badOutlet;
            }

            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? string.Empty;
            if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
                !contentType.Contains("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "Invalid content type. Expected multipart/form-data." });
                return bad;
            }

            var boundary = ExtractBoundary(contentType);
            if (string.IsNullOrWhiteSpace(boundary))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "Invalid multipart boundary." });
                return bad;
            }

            using var bodyStream = new MemoryStream();
            await req.Body.CopyToAsync(bodyStream);
            bodyStream.Position = 0;
            var parts = ParseMultipartFormData(bodyStream, boundary);

            if (!parts.TryGetValue("orderHistoryFile", out var orderHistoryPart) || orderHistoryPart.Data == null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "Missing required file field: orderHistoryFile (.csv)." });
                return bad;
            }

            if (!parts.TryGetValue("settlementFile", out var settlementPart) || settlementPart.Data == null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "Missing required file field: settlementFile (.xlsx)." });
                return bad;
            }

            if (!HasCsvExtension(orderHistoryPart.FileName))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "orderHistoryFile must be a .csv file." });
                return bad;
            }

            if (!HasExcelExtension(settlementPart.FileName))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "settlementFile must be a .xlsx or .xls file." });
                return bad;
            }

            const int maxFileSizeBytes = 15 * 1024 * 1024;
            if (orderHistoryPart.Data.Length > maxFileSizeBytes || settlementPart.Data.Length > maxFileSizeBytes)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, error = "Each file must be <= 15MB." });
                return bad;
            }

            using var orderHistoryStream = new MemoryStream(orderHistoryPart.Data);
            using var settlementStream = new MemoryStream(settlementPart.Data);
            var insights = await _insightService.GenerateInsightsAsync(orderHistoryStream, settlementStream);
            if (!IstDateHelper.TryParseIsoDate(insights.Period.StartDate, out var periodStartDate) ||
                !IstDateHelper.TryParseIsoDate(insights.Period.EndDate, out var periodEndDate))
            {
                var badPeriod = req.CreateResponse(HttpStatusCode.BadRequest);
                await badPeriod.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "Unable to infer report period from uploaded files. Ensure both reports contain valid order dates."
                });
                return badPeriod;
            }

            var (_, userId, _, _) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            var savedRecord = await _mongo.UpsertZomatoSalesPayoutInsightRecordAsync(
                outletId,
                periodStartDate,
                periodEndDate,
                BsonDocument.Parse(JsonSerializer.Serialize(insights)),
                userId ?? "system");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = insights,
                persisted = new
                {
                    id = savedRecord.Id,
                    outletId = savedRecord.OutletId,
                    periodStartDate = savedRecord.PeriodStartDate.ToString("yyyy-MM-dd"),
                    periodEndDate = savedRecord.PeriodEndDate.ToString("yyyy-MM-dd"),
                    updatedAt = savedRecord.UpdatedAt
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error parsing Zomato sales and payout reports.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, error = "Failed to parse reports." });
            return error;
        }
    }

    [Function("GetZomatoSalesPayoutInsightSnapshots")]
    public async Task<HttpResponseData> GetZomatoSalesPayoutInsightSnapshots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/zomato-sales-payout/insights")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badOutlet = req.CreateResponse(HttpStatusCode.BadRequest);
                await badOutlet.WriteAsJsonAsync(new { success = false, error = "Outlet context is required. Please select an outlet and try again." });
                return badOutlet;
            }

            var records = await _mongo.GetZomatoSalesPayoutInsightRecordsAsync(outletId);
            var jsonSettings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
            var readOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var snapshots = new List<object>();
            foreach (var record in records)
            {
                ZomatoSalesPayoutInsightResult? insights = null;
                try
                {
                    insights = JsonSerializer.Deserialize<ZomatoSalesPayoutInsightResult>(record.InsightsPayload.ToJson(jsonSettings), readOptions);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Skipping unreadable Zomato insight snapshot {Id}.", record.Id);
                }

                if (insights == null) continue;
                snapshots.Add(new
                {
                    id = record.Id,
                    periodStartDate = record.PeriodStartDate.ToString("yyyy-MM-dd"),
                    periodEndDate = record.PeriodEndDate.ToString("yyyy-MM-dd"),
                    updatedAt = record.UpdatedAt,
                    parsedBy = record.ParsedBy,
                    data = insights
                });
            }

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { success = true, data = snapshots });
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error loading saved Zomato sales and payout insights.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, error = "Failed to load saved insights." });
            return error;
        }
    }

    [Function("DeleteZomatoSalesPayoutInsightsByPeriod")]
    public async Task<HttpResponseData> DeleteZomatoSalesPayoutInsightsByPeriod(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "reports/zomato-sales-payout/insights")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badOutlet = req.CreateResponse(HttpStatusCode.BadRequest);
                await badOutlet.WriteAsJsonAsync(new { success = false, error = "Outlet context is required. Please select an outlet and try again." });
                return badOutlet;
            }

            var startDateRaw = req.Query["startDate"];
            var endDateRaw = req.Query["endDate"];
            if (!IstDateHelper.TryParseIsoDate(startDateRaw, out var startDate) ||
                !IstDateHelper.TryParseIsoDate(endDateRaw, out var endDate))
            {
                var badDate = req.CreateResponse(HttpStatusCode.BadRequest);
                await badDate.WriteAsJsonAsync(new { success = false, error = "startDate and endDate are required in yyyy-MM-dd format." });
                return badDate;
            }

            if (endDate.Date < startDate.Date)
            {
                var badRange = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRange.WriteAsJsonAsync(new { success = false, error = "endDate must be greater than or equal to startDate." });
                return badRange;
            }

            var deletedCount = await _mongo.DeleteZomatoSalesPayoutInsightRecordsByPeriodAsync(outletId, startDate, endDate);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { success = true, deletedCount });
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting saved Zomato sales and payout insights by period.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, error = "Failed to delete saved insights." });
            return error;
        }
    }

    private static bool HasCsvExtension(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    private static bool HasExcelExtension(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractBoundary(string contentType)
    {
        var match = Regex.Match(contentType, @"boundary=(?<value>[^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"') : null;
    }

    private static Dictionary<string, MultipartPart> ParseMultipartFormData(Stream stream, string boundary)
    {
        var parts = new Dictionary<string, MultipartPart>(StringComparer.OrdinalIgnoreCase);
        var boundaryBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary);
        stream.Position = 0;

        var buffer = new byte[stream.Length];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        if (bytesRead != buffer.Length)
        {
            throw new InvalidOperationException("Failed to read request body.");
        }

        var boundaryPositions = new List<int>();
        for (var i = 0; i <= buffer.Length - boundaryBytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < boundaryBytes.Length; j++)
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

        for (var i = 0; i < boundaryPositions.Count - 1; i++)
        {
            var start = boundaryPositions[i] + boundaryBytes.Length;
            var end = boundaryPositions[i + 1];
            var section = new byte[end - start];
            Array.Copy(buffer, start, section, 0, section.Length);

            var headerEnd = -1;
            for (var j = 0; j < section.Length - 3; j++)
            {
                if ((section[j] == '\r' && section[j + 1] == '\n' && section[j + 2] == '\r' && section[j + 3] == '\n') ||
                    (section[j] == '\n' && section[j + 1] == '\n'))
                {
                    headerEnd = j;
                    break;
                }
            }

            if (headerEnd < 0)
            {
                continue;
            }

            var headersText = System.Text.Encoding.UTF8.GetString(section, 0, headerEnd);
            var fieldMatch = Regex.Match(headersText, @"name=""([^""]+)""");
            if (!fieldMatch.Success)
            {
                continue;
            }

            var fieldName = fieldMatch.Groups[1].Value;
            var fileNameMatch = Regex.Match(headersText, @"filename=""([^""]*)""");
            var bodyStart = headerEnd + (section[headerEnd] == '\r' ? 4 : 2);
            var bodyLength = section.Length - bodyStart;
            while (bodyLength > 0 && (section[bodyStart + bodyLength - 1] == '\r' || section[bodyStart + bodyLength - 1] == '\n'))
            {
                bodyLength--;
            }

            var body = new byte[bodyLength];
            Array.Copy(section, bodyStart, body, 0, bodyLength);

            if (fileNameMatch.Success && !string.IsNullOrWhiteSpace(fileNameMatch.Groups[1].Value))
            {
                parts[fieldName] = new MultipartPart
                {
                    FileName = fileNameMatch.Groups[1].Value,
                    Data = body
                };
            }
            else
            {
                parts[fieldName] = new MultipartPart
                {
                    Text = System.Text.Encoding.UTF8.GetString(body).Trim()
                };
            }
        }

        return parts;
    }

    private sealed class MultipartPart
    {
        public string? FileName { get; set; }
        public byte[]? Data { get; set; }
        public string? Text { get; set; }
    }
}
