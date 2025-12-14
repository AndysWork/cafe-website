using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class AdminFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;

    public AdminFunction(MongoService mongo, AuthService auth)
    {
        _mongo = mongo;
        _auth = auth;
    }

    [Function("ClearCategories")]
    public async Task<HttpResponseData> ClearCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/clear/categories")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        await _mongo.ClearCategoriesAsync();
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { message = "All categories cleared" });
        return res;
    }

    [Function("ClearSubCategories")]
    public async Task<HttpResponseData> ClearSubCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/clear/subcategories")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        await _mongo.ClearSubCategoriesAsync();
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { message = "All subcategories cleared" });
        return res;
    }
}
