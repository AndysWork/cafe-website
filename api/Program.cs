using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using System.Reflection;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(builder =>
    {
        // Add security middleware
        builder.UseMiddleware<SecurityHeadersMiddleware>();
        builder.UseMiddleware<RateLimitingMiddleware>();
    })
    .ConfigureServices(s =>
    {
        s.AddSingleton<MongoService>();
        s.AddSingleton<FileUploadService>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<IEmailService, EmailService>();
        s.AddHttpClient();
        s.AddSingleton<MarketPriceService>();
        
        // Configure JSON serialization to use camelCase for Azure Functions Worker
        s.Configure<WorkerOptions>(options =>
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            
            options.Serializer = new Azure.Core.Serialization.JsonObjectSerializer(jsonOptions);
        });

        // Configure Swagger/OpenAPI
        s.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Cafe Management API",
                Version = "v1",
                Description = "API for managing cafe operations including menu items, orders, inventory, expenses, and sales",
                Contact = new OpenApiContact
                {
                    Name = "Cafe Management System",
                    Email = "support@cafemanagement.com"
                }
            });

            // Add JWT authentication to Swagger
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML comments
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }

            c.EnableAnnotations();
        });
    })
    .Build();

host.Run();
