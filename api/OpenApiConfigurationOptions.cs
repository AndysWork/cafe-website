using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api;

public class OpenApiConfigurationOptions : DefaultOpenApiConfigurationOptions
{
    public override OpenApiInfo Info { get; set; } = new OpenApiInfo
    {
        Version = "v1",
        Title = "Cafe Management API",
        Description = "Comprehensive API for managing cafe operations including menu, orders, inventory, expenses, and sales",
        Contact = new OpenApiContact
        {
            Name = "Cafe Management System",
            Email = "support@cafemanagement.com"
        }
    };

    public override List<OpenApiServer> Servers { get; set; } = new List<OpenApiServer>
    {
        new OpenApiServer { Url = "http://localhost:7071", Description = "Local Development" },
        new OpenApiServer { Url = "https://cafe-management.azurewebsites.net", Description = "Production" }
    };
}
