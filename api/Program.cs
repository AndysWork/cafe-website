using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using OfficeOpenXml;
using Polly;
using Polly.Extensions.Http;
using Azure.Storage.Blobs;

// Set EPPlus license context once at startup (thread-safe)
ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(builder =>
    {
        // Add security middleware
        builder.UseMiddleware<SecurityHeadersMiddleware>();
        builder.UseMiddleware<RateLimitingMiddleware>();
        builder.UseMiddleware<RequestLoggingMiddleware>();
        builder.UseMiddleware<ApiVersionMiddleware>();
    })
    .ConfigureServices(s =>
    {
        // Application Insights for distributed tracing and telemetry
        s.AddApplicationInsightsTelemetryWorkerService();
        s.ConfigureFunctionsApplicationInsights();
        s.AddSingleton<MongoService>();
        s.AddSingleton(sp => sp.GetRequiredService<MongoService>().Database);
        s.AddSingleton<FileUploadService>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<IEmailService, EmailService>();
        s.AddSingleton<IWhatsAppService, WhatsAppService>();
        s.AddSingleton<MarketPriceService>();
        s.AddSingleton<IRazorpayService, RazorpayService>();
        s.AddSingleton<NotificationService>();
        
        // Azure Blob Storage for file/image uploads
        var blobConnectionString = Environment.GetEnvironmentVariable("Blob__ConnectionString") ?? "UseDevelopmentStorage=true";
        s.AddSingleton(new BlobServiceClient(blobConnectionString));
        s.AddSingleton<BlobStorageService>();
        
        // Resilience policies for external HTTP calls
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        
        // Named HTTP clients with Polly policies
        s.AddHttpClient("WhatsApp")
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);
        
        s.AddHttpClient("Razorpay")
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);
        
        // Default client for other uses
        s.AddHttpClient();
        
        // Async initialization (replaces blocking .Wait() calls)
        s.AddHostedService<MongoInitializationService>();
        
        // In-memory caching for frequently accessed data
        s.AddMemoryCache();
        
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
    })
    .Build();

host.Run();
