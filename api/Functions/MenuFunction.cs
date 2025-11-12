using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using System.Net;

namespace Cafe.Api.Functions;

public class MenuFunction
{
    private readonly MongoService _mongo;
    private readonly ILogger _log;

    public MenuFunction(MongoService mongo, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _log = loggerFactory.CreateLogger<MenuFunction>();
    }

    [Function("GetMenu")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(await _mongo.GetMenuAsync());
        return res;
    }
}
