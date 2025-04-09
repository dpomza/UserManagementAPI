using System;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

// Create the web application builder.
var builder = WebApplication.CreateBuilder(args);

// Optionally add Problem Details support.
builder.Services.AddProblemDetails();

// Configure logging: clear default providers and add console logging.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Configure the Redis connection.
// In production, move the connection string ("localhost:6379") into configuration.
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var db = redis.GetDatabase();

var app = builder.Build();

// Global exception handling middleware.
// This will catch unhandled exceptions and return a consistent JSON error response.
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "An unexpected error occurred. Please try again later."
        });
    });
});

// Register custom middleware.
// Logging middleware logs requests and responses.
app.UseMiddleware<LoggingMiddleware>();

// Authentication middleware validates the Authorization header.
app.UseMiddleware<AuthenticationMiddleware>();

// Helper method to validate an email address.
static bool IsValidEmail(string email)
{
    try
    {
        var addr = new MailAddress(email);
        return addr.Address == email;
    }
    catch
    {
        return false;
    }
}

// ----------------- CRUD Endpoints ------------------

// GET all users.
app.MapGet("/users", async () =>
{
    var server = redis.GetServer(redis.GetEndPoints().First());
    var keys = server.Keys(pattern: "user:*")
                     .Where(k => !k.ToString().Equals("user:nextId", StringComparison.OrdinalIgnoreCase))
                     .ToArray();

    var usersList = new List<User>();
    foreach (var key in keys)
    {
        var userData = await db.StringGetAsync(key);
        if (userData.HasValue)
        {
            var user = JsonSerializer.Deserialize<User>(userData.ToString());
            if (user is not null)
            {
                usersList.Add(user);
            }
        }
    }
    return Results.Ok(usersList);
});

// GET user by id.
app.MapGet("/users/{id:int}", async (int id) =>
{
    var key = $"user:{id}";
    if (!await db.KeyExistsAsync(key))
    {
        return Results.NotFound(new { Error = $"User with ID {id} not found." });
    }
    var userData = await db.StringGetAsync(key);
    if (!userData.HasValue)
    {
        return Results.NotFound(new { Error = $"User with ID {id} not found." });
    }
    var user = JsonSerializer.Deserialize<User>(userData.ToString());
    return Results.Ok(user);
});

// POST create a new user.
app.MapPost("/users", async (HttpRequest request) =>
{
    var user = await request.ReadFromJsonAsync<User>();
    if (user is null ||
        string.IsNullOrWhiteSpace(user.Name) ||
        string.IsNullOrWhiteSpace(user.Email))
    {
        return Results.BadRequest(new { Error = "Invalid user data. 'Name' and 'Email' are required and cannot be empty." });
    }
    if (!IsValidEmail(user.Email))
    {
        return Results.BadRequest(new { Error = "Invalid email format." });
    }
    // Atomically generate a new user ID using Redis.
    var newId = (int)await db.StringIncrementAsync("user:nextId");
    user.Id = newId;

    var key = $"user:{user.Id}";
    var userJson = JsonSerializer.Serialize(user);
    var setResult = await db.StringSetAsync(key, userJson);
    if (!setResult)
    {
        return Results.Problem("User could not be added due to an internal issue.");
    }
    return Results.Created($"/users/{user.Id}", user);
});

// PUT update an existing user.
app.MapPut("/users/{id:int}", async (int id, HttpRequest request) =>
{
    var key = $"user:{id}";
    if (!await db.KeyExistsAsync(key))
    {
        return Results.NotFound(new { Error = $"User with ID {id} not found." });
    }
    var updatedUser = await request.ReadFromJsonAsync<User>();
    if (updatedUser is null ||
        string.IsNullOrWhiteSpace(updatedUser.Name) ||
        string.IsNullOrWhiteSpace(updatedUser.Email))
    {
        return Results.BadRequest(new { Error = "Invalid user data. 'Name' and 'Email' are required and cannot be empty." });
    }
    if (!IsValidEmail(updatedUser.Email))
    {
        return Results.BadRequest(new { Error = "Invalid email format." });
    }
    // Preserve the original ID.
    updatedUser.Id = id;
    var userJson = JsonSerializer.Serialize(updatedUser);
    await db.StringSetAsync(key, userJson);
    return Results.NoContent();
});

// DELETE user by id.
app.MapDelete("/users/{id:int}", async (int id) =>
{
    var key = $"user:{id}";
    if (!await db.KeyDeleteAsync(key))
    {
        return Results.NotFound(new { Error = $"User with ID {id} not found." });
    }
    return Results.NoContent();
});

// GET search for users by name (case‑insensitive partial match).
app.MapGet("/users/search", async (string name) =>
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.BadRequest(new { Error = "Search term cannot be empty." });
    }
    var server = redis.GetServer(redis.GetEndPoints().First());
    var keys = server.Keys(pattern: "user:*")
                     .Where(k => !k.ToString().Equals("user:nextId", StringComparison.OrdinalIgnoreCase))
                     .ToArray();

    var matchingUsers = new List<User>();
    foreach (var key in keys)
    {
        var userData = await db.StringGetAsync(key);
        if (userData.HasValue)
        {
            var user = JsonSerializer.Deserialize<User>(userData.ToString());
            if (user is not null &&
                !string.IsNullOrWhiteSpace(user.Name) &&
                user.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                matchingUsers.Add(user);
            }
        }
    }
    return Results.Ok(matchingUsers);
});

app.Run();

// --------------- Helper Types and Middleware ---------------

// Record type for the User model.
public record User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
}

// Custom Logging Middleware.
public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingMiddleware> _logger;
    public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogInformation("Incoming Request: {Method} {Path}", context.Request.Method, context.Request.Path);
        await _next(context);
        _logger.LogInformation("Outgoing Response: {StatusCode}", context.Response.StatusCode);
    }
}

// Custom Authentication Middleware.
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    public AuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    public async Task InvokeAsync(HttpContext context)
    {
        // Check if the Authorization header is present.
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { Error = "Missing Authorization header." });
            return;
        }
        // Expect the token to be "Bearer mysecrettoken".
        var parts = authHeader.ToString().Split(' ');
        if (parts.Length < 2 ||
            !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            parts[1] != "mysecrettoken")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { Error = "Invalid token." });
            return;
        }
        await _next(context);
    }
}

// Expose the Program class for integration testing (using WebApplicationFactory).
public partial class Program { }