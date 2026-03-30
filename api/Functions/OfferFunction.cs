using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class OfferFunction
{
    private readonly IOfferRepository _mongoService;
    private readonly AuthService _authService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<OfferFunction> _logger;

    public OfferFunction(IOfferRepository mongoService, AuthService authService, IWhatsAppService whatsAppService, ILogger<OfferFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    // GET /api/offers - Get all active offers (public)
    [Function("GetActiveOffers")]
    [OpenApiOperation(operationId: "GetActiveOffers", tags: new[] { "Offers" }, Summary = "Get active offers", Description = "Retrieves all active offers (public endpoint)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Offer>), Description = "Successfully retrieved active offers")]
    public async Task<HttpResponseData> GetActiveOffers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers")] HttpRequestData req)
    {
        _logger.LogInformation("Getting active offers");

        try
        {
            var offers = await _mongoService.GetActiveOffersAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offers);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active offers");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error retrieving offers" });
            return response;
        }
    }

    // GET /api/offers/all - Get all offers (admin)
    [Function("GetAllOffers")]
    [OpenApiOperation(operationId: "GetAllOffers", tags: new[] { "Offers" }, Summary = "Get all offers", Description = "Retrieves all offers including inactive ones (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Offer>), Description = "Successfully retrieved all offers")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetAllOffers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers/all")] HttpRequestData req)
    {
        _logger.LogInformation("Getting all offers (admin)");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var offers = await _mongoService.GetAllOffersAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offers);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all offers");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error retrieving offers" });
            return response;
        }
    }

    // GET /api/offers/{id} - Get offer by ID (admin)
    [Function("GetOfferById")]
    [OpenApiOperation(operationId: "GetOfferById", tags: new[] { "Offers" }, Summary = "Get offer by ID", Description = "Retrieves a specific offer by its ID (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Offer ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Offer), Description = "Successfully retrieved offer")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Offer not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetOfferById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offers/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"Getting offer by ID: {id}");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var offer = await _mongoService.GetOfferByIdAsync(id);
            
            if (offer == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error retrieving offer" });
            return response;
        }
    }

    // POST /api/offers - Create new offer (admin)
    [Function("CreateOffer")]
    public async Task<HttpResponseData> CreateOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers")] HttpRequestData req)
    {
        _logger.LogInformation("Creating new offer");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var offer = await JsonSerializer.DeserializeAsync<Offer>(req.Body);
            if (offer == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid offer data" });
                return badRequestResponse;
            }

            // Check if code already exists
            var existingOffer = await _mongoService.GetOfferByCodeAsync(offer.Code);
            if (existingOffer != null)
            {
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteAsJsonAsync(new { error = "Offer code already exists" });
                return conflictResponse;
            }

            var createdOffer = await _mongoService.CreateOfferAsync(offer);
            
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdOffer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error creating offer" });
            return response;
        }
    }

    // PUT /api/offers/{id} - Update offer (admin)
    [Function("UpdateOffer")]
    public async Task<HttpResponseData> UpdateOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "offers/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"Updating offer: {id}");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var offer = await JsonSerializer.DeserializeAsync<Offer>(req.Body);
            if (offer == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid offer data" });
                return badRequestResponse;
            }

            offer.Id = id;
            var success = await _mongoService.UpdateOfferAsync(id, offer);
            
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error updating offer" });
            return response;
        }
    }

    // DELETE /api/offers/{id} - Delete offer (admin)
    [Function("DeleteOffer")]
    public async Task<HttpResponseData> DeleteOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "offers/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"Deleting offer: {id}");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongoService.DeleteOfferAsync(id);
            
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offer deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error deleting offer" });
            return response;
        }
    }

    // POST /api/offers/validate - Validate offer code (authenticated users)
    [Function("ValidateOffer")]
    public async Task<HttpResponseData> ValidateOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/validate")] HttpRequestData req)
    {
        _logger.LogInformation("Validating offer code");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var validationRequest = await JsonSerializer.DeserializeAsync<OfferValidationRequest>(req.Body);
            if (validationRequest == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid validation request" });
                return badRequestResponse;
            }

            var validationResult = await _mongoService.ValidateOfferAsync(validationRequest);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(validationResult);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error validating offer" });
            return response;
        }
    }

    // POST /api/offers/{id}/apply - Apply offer and increment usage count (authenticated users)
    [Function("ApplyOffer")]
    public async Task<HttpResponseData> ApplyOffer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offers/{id}/apply")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"Applying offer: {id}");

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongoService.IncrementOfferUsageAsync(id);
            
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offer applied successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying offer");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Error applying offer" });
            return response;
        }
    }
}
