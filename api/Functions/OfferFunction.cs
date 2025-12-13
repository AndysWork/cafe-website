using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class OfferFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<OfferFunction> _logger;

    public OfferFunction(MongoService mongoService, AuthService authService, ILogger<OfferFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    // GET /api/offers - Get all active offers (public)
    [Function("GetActiveOffers")]
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
            _logger.LogError($"Error getting active offers: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error retrieving offers" });
            return response;
        }
    }

    // GET /api/offers/all - Get all offers (admin)
    [Function("GetAllOffers")]
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
            _logger.LogError($"Error getting all offers: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error retrieving offers" });
            return response;
        }
    }

    // GET /api/offers/{id} - Get offer by ID (admin)
    [Function("GetOfferById")]
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
                await notFoundResponse.WriteAsJsonAsync(new { message = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error retrieving offer" });
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
                await badRequestResponse.WriteAsJsonAsync(new { message = "Invalid offer data" });
                return badRequestResponse;
            }

            // Check if code already exists
            var existingOffer = await _mongoService.GetOfferByCodeAsync(offer.Code);
            if (existingOffer != null)
            {
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteAsJsonAsync(new { message = "Offer code already exists" });
                return conflictResponse;
            }

            var createdOffer = await _mongoService.CreateOfferAsync(offer);
            
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdOffer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error creating offer" });
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
                await badRequestResponse.WriteAsJsonAsync(new { message = "Invalid offer data" });
                return badRequestResponse;
            }

            offer.Id = id;
            var success = await _mongoService.UpdateOfferAsync(id, offer);
            
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { message = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(offer);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error updating offer" });
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
                await notFoundResponse.WriteAsJsonAsync(new { message = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offer deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error deleting offer" });
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
                await badRequestResponse.WriteAsJsonAsync(new { message = "Invalid validation request" });
                return badRequestResponse;
            }

            var validationResult = await _mongoService.ValidateOfferAsync(validationRequest);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(validationResult);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error validating offer" });
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
                await notFoundResponse.WriteAsJsonAsync(new { message = "Offer not found" });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offer applied successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error applying offer: {ex.Message}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { message = "Error applying offer" });
            return response;
        }
    }
}
