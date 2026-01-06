using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;

namespace Cafe.Api.Functions;

/// <summary>
/// Provides Swagger/OpenAPI documentation endpoints
/// </summary>
public class SwaggerFunction
{
    private readonly ILogger _log;

    public SwaggerFunction(ILoggerFactory loggerFactory)
    {
        _log = loggerFactory.CreateLogger<SwaggerFunction>();
    }

    /// <summary>
    /// Serves the Swagger UI HTML page
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <returns>HTML page with Swagger UI</returns>
    [Function("SwaggerUI")]
    public async Task<HttpResponseData> SwaggerUI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger")] HttpRequestData req)
    {
        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");

            var html = GetSwaggerUIHtml();
            await response.WriteStringAsync(html);

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error serving Swagger UI");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to load Swagger UI" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Serves the OpenAPI/Swagger JSON specification
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <returns>OpenAPI specification in JSON format</returns>
    [Function("SwaggerJSON")]
    public async Task<HttpResponseData> SwaggerJSON(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger/v1/swagger.json")] HttpRequestData req)
    {
        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");

            var openApiSpec = GetOpenApiSpecification();
            await response.WriteStringAsync(openApiSpec);

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error serving Swagger JSON");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to load Swagger specification" });
            return errorResponse;
        }
    }

    private static string GetSwaggerUIHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cafe Management API - Swagger UI</title>
    <link rel=""stylesheet"" type=""text/css"" href=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui.css"">
    <style>
        html { box-sizing: border-box; overflow: -moz-scrollbars-vertical; overflow-y: scroll; }
        *, *:before, *:after { box-sizing: inherit; }
        body { margin:0; padding:0; }
        .topbar { display: none; }
    </style>
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui-bundle.js""></script>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui-standalone-preset.js""></script>
    <script>
        window.onload = function() {
            const ui = SwaggerUIBundle({
                url: '/api/swagger/v1/swagger.json',
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [
                    SwaggerUIBundle.presets.apis,
                    SwaggerUIStandalonePreset
                ],
                plugins: [
                    SwaggerUIBundle.plugins.DownloadUrl
                ],
                layout: 'StandaloneLayout',
                persistAuthorization: true,
                defaultModelsExpandDepth: 1,
                defaultModelExpandDepth: 1
            });
            window.ui = ui;
        };
    </script>
</body>
</html>";
    }

    private static string GetOpenApiSpecification()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        
        return $$"""
{
  "openapi": "3.0.1",
  "info": {
    "title": "Cafe Management API",
    "description": "Comprehensive API for managing cafe operations including menu items, orders, inventory, expenses, and sales analytics",
    "contact": {
      "name": "Cafe Management System",
      "email": "support@cafemanagement.com"
    },
    "version": "{{version}}"
  },
  "servers": [
    {
      "url": "/api",
      "description": "API Base URL"
    }
  ],
  "paths": {
    "/auth/login": {
      "post": {
        "tags": ["Authentication"],
        "summary": "Authenticates a user and returns a JWT token",
        "operationId": "Login",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/LoginRequest"
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Successfully authenticated",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/LoginResponse"
                }
              }
            }
          },
          "401": { "description": "Invalid credentials" },
          "403": { "description": "Account deactivated" },
          "429": { "description": "Too many failed login attempts" }
        }
      }
    },
    "/auth/register": {
      "post": {
        "tags": ["Authentication"],
        "summary": "Registers a new user account",
        "operationId": "Register",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/RegisterRequest"
              }
            }
          }
        },
        "responses": {
          "201": { "description": "User successfully registered" },
          "400": { "description": "Invalid data or validation failed" },
          "409": { "description": "Username or email already exists" }
        }
      }
    },
    "/menu": {
      "get": {
        "tags": ["Menu"],
        "summary": "Retrieves all menu items",
        "operationId": "GetMenu",
        "responses": {
          "200": {
            "description": "Successfully retrieved menu items",
            "content": {
              "application/json": {
                "schema": {
                  "type": "array",
                  "items": { "$ref": "#/components/schemas/CafeMenuItem" }
                }
              }
            }
          }
        }
      },
      "post": {
        "tags": ["Menu"],
        "summary": "Creates a new menu item (Admin only)",
        "operationId": "CreateMenuItem",
        "security": [{ "Bearer": [] }],
        "requestBody": {
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/CafeMenuItem" }
            }
          }
        },
        "responses": {
          "201": { "description": "Menu item successfully created" },
          "401": { "description": "User not authenticated" },
          "403": { "description": "User not authorized" }
        }
      }
    },
    "/menu/{id}": {
      "get": {
        "tags": ["Menu"],
        "summary": "Retrieves a specific menu item by ID",
        "operationId": "GetMenuItem",
        "parameters": [
          {
            "name": "id",
            "in": "path",
            "required": true,
            "schema": { "type": "string" }
          }
        ],
        "responses": {
          "200": { "description": "Successfully retrieved menu item" },
          "404": { "description": "Menu item not found" }
        }
      }
    },
    "/categories": {
      "get": {
        "tags": ["Categories"],
        "summary": "Retrieves all menu categories",
        "operationId": "GetCategories",
        "responses": {
          "200": {
            "description": "Successfully retrieved categories",
            "content": {
              "application/json": {
                "schema": {
                  "type": "array",
                  "items": { "$ref": "#/components/schemas/MenuCategory" }
                }
              }
            }
          }
        }
      }
    },
    "/orders": {
      "get": {
        "tags": ["Orders"],
        "summary": "Retrieves all orders (Admin only)",
        "operationId": "GetAllOrders",
        "security": [{ "Bearer": [] }],
        "responses": {
          "200": { "description": "Successfully retrieved all orders" },
          "401": { "description": "User not authenticated" },
          "403": { "description": "User not authorized" }
        }
      },
      "post": {
        "tags": ["Orders"],
        "summary": "Creates a new order for the authenticated user",
        "operationId": "CreateOrder",
        "security": [{ "Bearer": [] }],
        "requestBody": {
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/CreateOrderRequest" }
            }
          }
        },
        "responses": {
          "201": { "description": "Order successfully created" },
          "400": { "description": "Invalid request data" },
          "401": { "description": "User not authenticated" }
        }
      }
    },
    "/orders/my": {
      "get": {
        "tags": ["Orders"],
        "summary": "Retrieves all orders for the authenticated user",
        "operationId": "GetMyOrders",
        "security": [{ "Bearer": [] }],
        "responses": {
          "200": { "description": "Successfully retrieved user's orders" },
          "401": { "description": "User not authenticated" }
        }
      }
    },
    "/orders/{id}": {
      "get": {
        "tags": ["Orders"],
        "summary": "Retrieves a specific order by ID",
        "operationId": "GetOrder",
        "security": [{ "Bearer": [] }],
        "parameters": [
          {
            "name": "id",
            "in": "path",
            "required": true,
            "schema": { "type": "string" }
          }
        ],
        "responses": {
          "200": { "description": "Successfully retrieved order" },
          "401": { "description": "User not authenticated" },
          "403": { "description": "Access denied" },
          "404": { "description": "Order not found" }
        }
      }
    },
    "/orders/{id}/status": {
      "put": {
        "tags": ["Orders"],
        "summary": "Updates the status of an order (Admin only)",
        "operationId": "UpdateOrderStatus",
        "security": [{ "Bearer": [] }],
        "parameters": [
          {
            "name": "id",
            "in": "path",
            "required": true,
            "schema": { "type": "string" }
          }
        ],
        "requestBody": {
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/UpdateOrderStatusRequest" }
            }
          }
        },
        "responses": {
          "200": { "description": "Order status successfully updated" },
          "400": { "description": "Invalid status" },
          "401": { "description": "User not authenticated" },
          "403": { "description": "User not authorized" }
        }
      }
    },
    "/users": {
      "get": {
        "tags": ["Users"],
        "summary": "Retrieves all user accounts (Admin only)",
        "operationId": "GetAllUsers",
        "security": [{ "Bearer": [] }],
        "responses": {
          "200": { "description": "Successfully retrieved users" },
          "401": { "description": "User not authenticated" },
          "403": { "description": "User not authorized" }
        }
      }
    }
  },
  "components": {
    "schemas": {
      "LoginRequest": {
        "type": "object",
        "properties": {
          "username": { "type": "string" },
          "password": { "type": "string", "format": "password" }
        },
        "required": ["username", "password"]
      },
      "LoginResponse": {
        "type": "object",
        "properties": {
          "token": { "type": "string" },
          "username": { "type": "string" },
          "email": { "type": "string" },
          "role": { "type": "string" },
          "firstName": { "type": "string" },
          "lastName": { "type": "string" }
        }
      },
      "RegisterRequest": {
        "type": "object",
        "properties": {
          "username": { "type": "string" },
          "email": { "type": "string", "format": "email" },
          "password": { "type": "string", "format": "password" },
          "firstName": { "type": "string" },
          "lastName": { "type": "string" },
          "phoneNumber": { "type": "string" }
        },
        "required": ["username", "email", "password", "firstName", "lastName"]
      },
      "CafeMenuItem": {
        "type": "object",
        "properties": {
          "id": { "type": "string" },
          "name": { "type": "string" },
          "description": { "type": "string" },
          "categoryId": { "type": "string" },
          "subCategoryId": { "type": "string" },
          "shopPrice": { "type": "number", "format": "decimal" },
          "onlinePrice": { "type": "number", "format": "decimal" },
          "isAvailable": { "type": "boolean" },
          "imageUrl": { "type": "string" }
        }
      },
      "MenuCategory": {
        "type": "object",
        "properties": {
          "id": { "type": "string" },
          "name": { "type": "string" },
          "description": { "type": "string" },
          "displayOrder": { "type": "integer" }
        }
      },
      "CreateOrderRequest": {
        "type": "object",
        "properties": {
          "items": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "menuItemId": { "type": "string" },
                "quantity": { "type": "integer" }
              }
            }
          },
          "deliveryMethod": { "type": "string", "enum": ["dine-in", "takeaway", "delivery"] },
          "deliveryAddress": { "type": "string" },
          "paymentMethod": { "type": "string" },
          "specialInstructions": { "type": "string" }
        },
        "required": ["items", "deliveryMethod"]
      },
      "UpdateOrderStatusRequest": {
        "type": "object",
        "properties": {
          "status": { "type": "string", "enum": ["pending", "confirmed", "preparing", "ready", "delivered", "cancelled"] }
        },
        "required": ["status"]
      }
    },
    "securitySchemes": {
      "Bearer": {
        "type": "http",
        "description": "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        "scheme": "bearer",
        "bearerFormat": "JWT"
      }
    }
  }
}
""";
    }
}
