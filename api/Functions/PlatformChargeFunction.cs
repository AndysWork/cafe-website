using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class PlatformChargeFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<PlatformChargeFunction> _logger;

    public PlatformChargeFunction(
        MongoService mongoService,
        AuthService authService,
        ILogger<PlatformChargeFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    // GET: Get all platform charges
    [Function("GetAllPlatformCharges")]
    public async Task<HttpResponseData> GetAllPlatformCharges(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "platform-charges")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var charges = await _mongoService.GetAllPlatformChargesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = charges.Select(c => new PlatformChargeResponse
                {
                    Id = c.Id!,
                    Platform = c.Platform,
                    Month = c.Month,
                    Year = c.Year,
                    Charges = c.Charges,
                    ChargeType = c.ChargeType,
                    Notes = c.Notes,
                    RecordedBy = c.RecordedBy,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetAllPlatformCharges");
            return await CreateErrorResponse(req, "Failed to retrieve platform charges");
        }
    }

    // GET: Get platform charge by month/year/platform
    [Function("GetPlatformChargeByKey")]
    public async Task<HttpResponseData> GetPlatformChargeByKey(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "platform-charges/{platform}/{year}/{month}")] HttpRequestData req,
        string platform, int year, int month)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var charge = await _mongoService.GetPlatformChargeByKeyAsync(platform, year, month);
            
            if (charge == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    message = $"No platform charge found for {platform} {month}/{year}"
                });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = new PlatformChargeResponse
                {
                    Id = charge.Id!,
                    Platform = charge.Platform,
                    Month = charge.Month,
                    Year = charge.Year,
                    Charges = charge.Charges,
                    ChargeType = charge.ChargeType ?? string.Empty,
                    Notes = charge.Notes ?? string.Empty,
                    RecordedBy = charge.RecordedBy,
                    CreatedAt = charge.CreatedAt,
                    UpdatedAt = charge.UpdatedAt
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPlatformChargeByKey");
            return await CreateErrorResponse(req, "Failed to retrieve platform charge");
        }
    }

    // GET: Get charges by platform
    [Function("GetChargesByPlatform")]
    public async Task<HttpResponseData> GetChargesByPlatform(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "platform-charges/platform/{platform}")] HttpRequestData req,
        string platform)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var charges = await _mongoService.GetPlatformChargesByPlatformAsync(platform);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = charges.Select(c => new PlatformChargeResponse
                {
                    Id = c.Id!,
                    Platform = c.Platform,
                    Month = c.Month,
                    Year = c.Year,
                    Charges = c.Charges,
                    ChargeType = c.ChargeType,
                    Notes = c.Notes,
                    RecordedBy = c.RecordedBy,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetChargesByPlatform");
            return await CreateErrorResponse(req, "Failed to retrieve platform charges");
        }
    }

    // POST: Create platform charge
    [Function("CreatePlatformCharge")]
    public async Task<HttpResponseData> CreatePlatformCharge(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "platform-charges")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreatePlatformChargeRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                return await CreateErrorResponse(req, "Invalid request body");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Platform) || 
                request.Month < 1 || request.Month > 12 ||
                request.Year < 2020 || request.Year > 2100)
            {
                return await CreateErrorResponse(req, "Invalid platform, month, or year");
            }

            // Check if charge already exists for this month/year/platform
            var existing = await _mongoService.GetPlatformChargeByKeyAsync(request.Platform, request.Year, request.Month);
            if (existing != null)
            {
                return await CreateErrorResponse(req, $"Platform charge already exists for {request.Platform} {request.Month}/{request.Year}. Use update instead.");
            }

            var charge = new PlatformCharge
            {
                Platform = request.Platform,
                Month = request.Month,
                Year = request.Year,
                Charges = request.Charges,
                ChargeType = request.ChargeType ?? string.Empty,
                Notes = request.Notes ?? string.Empty,
                RecordedBy = username ?? "System",
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            await _mongoService.CreatePlatformChargeAsync(charge);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Platform charge created successfully",
                data = new PlatformChargeResponse
                {
                    Id = charge.Id!,
                    Platform = charge.Platform,
                    Month = charge.Month,
                    Year = charge.Year,
                    Charges = charge.Charges,
                    ChargeType = charge.ChargeType ?? string.Empty,
                    Notes = charge.Notes ?? string.Empty,
                    RecordedBy = charge.RecordedBy,
                    CreatedAt = charge.CreatedAt,
                    UpdatedAt = charge.UpdatedAt
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreatePlatformCharge");
            return await CreateErrorResponse(req, "Failed to create platform charge");
        }
    }

    // PUT: Update platform charge
    [Function("UpdatePlatformCharge")]
    public async Task<HttpResponseData> UpdatePlatformCharge(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "platform-charges/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpdatePlatformChargeRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                return await CreateErrorResponse(req, "Invalid request body");
            }

            var updated = await _mongoService.UpdatePlatformChargeAsync(id, request);

            if (!updated)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Platform charge not found"
                });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Platform charge updated successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdatePlatformCharge");
            return await CreateErrorResponse(req, "Failed to update platform charge");
        }
    }

    // DELETE: Delete platform charge
    [Function("DeletePlatformCharge")]
    public async Task<HttpResponseData> DeletePlatformCharge(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "platform-charges/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var deleted = await _mongoService.DeletePlatformChargeAsync(id);

            if (!deleted)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Platform charge not found"
                });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Platform charge deleted successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeletePlatformCharge");
            return await CreateErrorResponse(req, "Failed to delete platform charge");
        }
    }

    private async Task<HttpResponseData> CreateUnauthorizedResponse(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Unauthorized);
        await response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Unauthorized. Admin access required."
        });
        return response;
    }

    private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new
        {
            success = false,
            message
        });
        return response;
    }
}
