using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Cafe.Api.Services;
using System.Net;

namespace Cafe.Api.Functions;

public class AdminFunction
{
    private readonly MongoService _mongo;

    public AdminFunction(MongoService mongo)
    {
        _mongo = mongo;
    }

    [Function("ClearCategories")]
    public async Task<HttpResponseData> ClearCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/clear/categories")] HttpRequestData req)
    {
        await _mongo.ClearCategoriesAsync();
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { message = "All categories cleared" });
        return res;
    }

    [Function("ClearSubCategories")]
    public async Task<HttpResponseData> ClearSubCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/clear/subcategories")] HttpRequestData req)
    {
        await _mongo.ClearSubCategoriesAsync();
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { message = "All subcategories cleared" });
        return res;
    }
}
