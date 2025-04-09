using System;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure logging (console logging for demo purposes)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Configure the Redis connection.
// In production, move the connection string (here "localhost:6379") to configuration.
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var db = redis.GetDatabase();

var app = builder.Build();

// Register middleware in the desired order:
// 1) Error-handling middleware.
// 2) Authentication middleware.
// 3) Logging middleware.
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// ----------------- Endpoints ----------------------

// GET all users using SCAN to avoid a blocking KEYS call.
app.MapGet("/users", async () =>
{
    try
    {
        var server = redis.GetServer(redis.GetEndPoints().First());
        // Using SCAN internally (by specifying a pageSize).
        var keys = server.Keys(pattern: "user:*", pageSize: 1000);
        var usersList = new List<User>();

        foreach (var key in keys)
        {
            if (key.ToString().Equals("user:nextId", StringComparison.OrdinalIgnoreCase))
                continue;

            var userData = await db.StringGetAsync(key);
            if (userData.HasValue)
            {
                var user = JsonSerializer.Deserialize<User>(userData.ToString());
                if (user != null)
                {
                    usersList.Add(user);
                }
            }
        }
        return Results.Ok(usersList);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error retrieving all users: {ex.Message}");
    }
});

// GET user by id.
app.MapGet("/users/{id:int}", async (int id) =>
{
    try
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
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error retrieving user by ID: {ex.Message}");
    }
});

// POST create a new user.
app.MapPost("/users", async (HttpRequest request) =>
{
    try
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
        // Use Redis atomic incrementation to generate a new user ID.
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
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error creating user: {ex.Message}");
    }
});

// PUT update an existing user.
app.MapPut("/users/{id:int}", async (int id, HttpRequest request) =>
{
    try
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
        // Maintain the original id.
        updatedUser.Id = id;
        var userJson = JsonSerializer.Serialize(updatedUser);
        await db.StringSetAsync(key, userJson);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error updating user: {ex.Message}");
    }
});

// DELETE user by id.
app.MapDelete("/users/{id:int}", async (int id) =>
{
    try
    {
        var key = $"user:{id}";
        if (!await db.KeyDeleteAsync(key))
        {
            return Results.NotFound(new { Error = $"User with ID {id} not found." });
        }
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error deleting user: {ex.Message}");
    }
});

// GET search for users by name with a case‑insensitive partial match.
app.MapGet("/users/search", async (string name) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new { Error = "Search term cannot be empty." });
        }
        var server = redis.GetServer(redis.GetEndPoints().First());
        var keys = server.Keys(pattern: "user:*", pageSize: 1000);
        var matchingUsers = new List<User>();

        foreach (var key in keys)
        {
            if (key.ToString().Equals("user:nextId", StringComparison.OrdinalIgnoreCase))
                continue;

            var userData = await db.StringGetAsync(key);
            if (userData.HasValue)
            {
                var user = JsonSerializer.Deserialize<User>(userData.ToString());
                if (!string.IsNullOrWhiteSpace(user?.Name) &&
                    user!.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    matchingUsers.Add(user);
                }
            }
        }
        return Results.Ok(matchingUsers);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error searching users: {ex.Message}");
    }
});

app.Run();

// ----------------- Helper Methods and Types ------------------

bool IsValidEmail(string email)
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

public record User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
}

// ----------------- Middleware Classes ------------------

// 1. Error Handling Middleware (registered first)
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception caught in ErrorHandlingMiddleware.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var errorResponse = JsonSerializer.Serialize(new { Error = "An unexpected error occurred." });
            await context.Response.WriteAsync(errorResponse);
        }
    }
}

// 2. Authentication Middleware (registered next)
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

        // For demonstration, require a token: "Bearer mysecrettoken"
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

// 3. Logging Middleware (registered last)
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
        // Log incoming request details.
        _logger.LogInformation("Incoming Request: {Method} {Path}", context.Request.Method, context.Request.Path);

        await _next(context);

        // Log outgoing response details.
        _logger.LogInformation("Outgoing Response: {StatusCode}", context.Response.StatusCode);
    }
}

// Expose the Program class for testing.
public partial class Program { }