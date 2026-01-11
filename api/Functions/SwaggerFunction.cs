using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using System.Net;

namespace Cafe.Api.Functions;

/// <summary>
/// OpenAPI/Swagger documentation is automatically provided by the Azure Functions OpenAPI extension.
/// Access Swagger UI at: /api/swagger/ui
/// Access OpenAPI JSON at: /api/swagger.json
/// 
/// No custom function needed - the extension handles everything automatically.
/// </summary>
public class SwaggerDocumentation
{
    // The Microsoft.Azure.Functions.Worker.Extensions.OpenApi extension automatically provides:
    // - GET /api/swagger/ui - Swagger UI interface
    // - GET /api/swagger.json - OpenAPI v2 specification
    // - GET /api/openapi/v2.json - OpenAPI v2 specification
    // - GET /api/openapi/v3.json - OpenAPI v3 specification
    
    // No custom functions needed - all endpoints are auto-generated from OpenAPI attributes
}

