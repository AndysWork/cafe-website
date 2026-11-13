using System.Reflection;
using Cafe.Api.Functions;
using Microsoft.Azure.Functions.Worker;
using Xunit;

namespace Cafe.Api.Tests;

public class HttpRouteTests
{
    [Fact]
    public void HttpFunctionsDoNotUseReservedAdminRoutes()
    {
        var routes = typeof(OrderFunction).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() != null)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()?.Route)
            .Where(route => route != null);

        Assert.NotEmpty(routes);
        Assert.All(routes, route =>
            Assert.False(
                route!.TrimStart('/').Split('/')[0].Equals("admin", StringComparison.OrdinalIgnoreCase),
                $"HTTP route '{route}' conflicts with Azure Functions host APIs."));
    }
}
