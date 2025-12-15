using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;

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
