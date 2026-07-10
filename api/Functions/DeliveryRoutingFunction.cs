using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class DeliveryRoutingFunction
{
    private readonly DeliveryRoutingService _deliveryRoutingService;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public DeliveryRoutingFunction(DeliveryRoutingService deliveryRoutingService, AuthService auth, ILoggerFactory loggerFactory)
    {
        _deliveryRoutingService = deliveryRoutingService;
        _auth = auth;
        _log = loggerFactory.CreateLogger<DeliveryRoutingFunction>();
    }

    [Function("GetDeliveryRouteQuote")]
    public async Task<HttpResponseData> GetDeliveryRouteQuote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "delivery/route-quote")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var destinationAddress = req.Query["deliveryAddress"]?.Trim();
            if (string.IsNullOrWhiteSpace(destinationAddress))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "deliveryAddress is required" });
                return badRequest;
            }

            var outletId = req.Query["outletId"]?.Trim();
            if (string.IsNullOrWhiteSpace(outletId))
            {
                outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
            }

            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Please select an outlet first" });
                return badRequest;
            }

            var orderId = req.Query["orderId"]?.Trim();
            var quote = await _deliveryRoutingService.BuildRouteQuoteAsync(outletId, destinationAddress);
            if (quote == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Unable to generate route for this outlet/address" });
                return badRequest;
            }

            var link = await _deliveryRoutingService.CreateOrReuseShortLinkAsync(
                outletId,
                destinationAddress,
                quote.MapUrl,
                orderId,
                quote.DistanceKm,
                quote.EtaMinutes);

            quote.ShortCode = link.Code;
            quote.ShortUrl = link.ShortUrl;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(quote);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating delivery route quote");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while generating route quote" });
            return res;
        }
    }

    [Function("ResolveDeliveryRouteShortLink")]
    public async Task<HttpResponseData> ResolveDeliveryRouteShortLink(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "r/{code}")] HttpRequestData req,
        string code)
    {
        try
        {
            var link = await _deliveryRoutingService.ResolveShortCodeAsync(code);
            if (link == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Route link expired or not found" });
                return notFound;
            }

            var redirect = req.CreateResponse(HttpStatusCode.Redirect);
            redirect.Headers.Add("Location", link.FullMapUrl);
            return redirect;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resolving route short link for code {Code}", code);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while resolving route link" });
            return res;
        }
    }
}