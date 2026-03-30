using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class AddressFunction
{
    private readonly IUserRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public AddressFunction(IUserRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<AddressFunction>();
    }

    [Function("GetMyAddresses")]
    public async Task<HttpResponseData> GetMyAddresses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "addresses")] HttpRequestData req)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var addresses = await _mongo.GetUserAddressesAsync(userId!);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(addresses);
        return response;
    }

    [Function("AddAddress")]
    public async Task<HttpResponseData> AddAddress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "addresses")] HttpRequestData req)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var (body, validationError) = await ValidationHelper.ValidateBody<AddDeliveryAddressRequest>(req);
        if (validationError != null) return validationError;

        var address = new DeliveryAddress
        {
            Label = InputSanitizer.Sanitize(body.Label),
            FullAddress = InputSanitizer.Sanitize(body.FullAddress),
            City = body.City != null ? InputSanitizer.Sanitize(body.City) : null,
            PinCode = body.PinCode != null ? InputSanitizer.Sanitize(body.PinCode) : null,
            CollectorName = InputSanitizer.Sanitize(body.CollectorName),
            CollectorPhone = InputSanitizer.SanitizePhoneNumber(body.CollectorPhone),
            IsDefault = body.IsDefault
        };

        var created = await _mongo.AddUserAddressAsync(userId!, address);
        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(created);
        return response;
    }

    [Function("UpdateAddress")]
    public async Task<HttpResponseData> UpdateAddress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "addresses/{addressId}")] HttpRequestData req,
        string addressId)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var (body, validationError) = await ValidationHelper.ValidateBody<UpdateDeliveryAddressRequest>(req);
        if (validationError != null) return validationError;

        // Sanitize inputs
        if (body.Label != null) body.Label = InputSanitizer.Sanitize(body.Label);
        if (body.FullAddress != null) body.FullAddress = InputSanitizer.Sanitize(body.FullAddress);
        if (body.City != null) body.City = InputSanitizer.Sanitize(body.City);
        if (body.PinCode != null) body.PinCode = InputSanitizer.Sanitize(body.PinCode);
        if (body.CollectorName != null) body.CollectorName = InputSanitizer.Sanitize(body.CollectorName);
        if (body.CollectorPhone != null) body.CollectorPhone = InputSanitizer.SanitizePhoneNumber(body.CollectorPhone);

        var updated = await _mongo.UpdateUserAddressAsync(userId!, addressId, body);
        if (!updated)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Address not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "Address updated" });
        return response;
    }

    [Function("DeleteAddress")]
    public async Task<HttpResponseData> DeleteAddress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "addresses/{addressId}")] HttpRequestData req,
        string addressId)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var deleted = await _mongo.DeleteUserAddressAsync(userId!, addressId);
        if (!deleted)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Address not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "Address deleted" });
        return response;
    }
}
