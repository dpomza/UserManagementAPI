using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using UserManagementAPI.Middleware;
using UserManagementAPI.Routes;             // Our routes extension method
using Serilog;                              // For logging
using AspNetCoreRateLimit;                  // For rate limiting

// Setup Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration)
                 .ReadFrom.Services(services)
                 .Enrich.FromLogContext()
                 .WriteTo.Console());

// Register services
builder.Services.AddProblemDetails();

// CORS (adjust allowed origins, headers, methods as needed)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
        policy.WithOrigins("https://trusted-site.com") // update with your allowed origins
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Add response compression (e.g., for JSON responses)
builder.Services.AddResponseCompression();

// Rate Limiting configuration using AspNetCoreRateLimit
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules = new System.Collections.Generic.List<RateLimitRule>
    {
        new RateLimitRule {
            Endpoint = "*",
            Limit = 100,    // e.g., 100 requests
            Period = "1m"   // per 1 minute
        }
    };
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Health Checks (includes checking Redis connectivity)
builder.Services.AddHealthChecks()
    .AddRedis("localhost:6379", name: "Redis");

// Build Redis connection (in production, store the connection string in configuration)
var redis = ConnectionMultiplexer.Connect("localhost:6379");

var app = builder.Build();

// Force HTTPS
app.UseHttpsRedirection();

// Expose Health Check endpoint
app.UseHealthChecks("/health");

// Enable CORS
app.UseCors("AllowSpecificOrigin");

// Global exception handling middleware
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { Error = "An unexpected error occurred. Please try again later." });
    });
});

// Use Rate Limiting middleware (AspNetCoreRateLimit)
app.UseIpRateLimiting();

// Enable response compression
app.UseResponseCompression();

// Add Request Correlation Middleware
app.UseMiddleware<CorrelationIdMiddleware>();

// Custom Logging Middleware (which in this example leverages Serilog)
app.UseMiddleware<LoggingMiddleware>();

// Authentication Middleware (validates the bearer token)
app.UseMiddleware<AuthenticationMiddleware>();

// Map the user routes from our modular routes (requires the Redis connection)
app.MapUserRoutes(redis);

app.Run();

// Expose Program class for integration testing
public partial class Program { }