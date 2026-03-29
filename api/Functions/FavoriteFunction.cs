using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class FavoriteFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public FavoriteFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<FavoriteFunction>();
    }

    [Function("GetMyFavorites")]
    public async Task<HttpResponseData> GetMyFavorites(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "favorites")] HttpRequestData req)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var favoriteIds = await _mongo.GetUserFavoritesAsync(userId!);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(favoriteIds);
        return response;
    }

    [Function("ToggleFavorite")]
    public async Task<HttpResponseData> ToggleFavorite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "favorites/{menuItemId}")] HttpRequestData req,
        string menuItemId)
    {
        var (isValid, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (!isValid) return errorResponse!;

        var isNowFavorite = await _mongo.ToggleFavoriteAsync(userId!, menuItemId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { isFavorite = isNowFavorite, menuItemId });
        return response;
    }
}
