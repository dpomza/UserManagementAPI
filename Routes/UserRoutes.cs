using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis;
using UserManagementAPI.Models;
using UserManagementAPI.Validators;

namespace UserManagementAPI.Routes
{
    public static class UserRoutes
    {
        public static IEndpointRouteBuilder MapUserRoutes(this IEndpointRouteBuilder app, ConnectionMultiplexer redis)
        {
            var db = redis.GetDatabase();

            // GET all users.
            app.MapGet("/users", async () =>
            {
                var server = redis.GetServer(redis.GetEndPoints().First());
                var keys = server.Keys(pattern: "user:*")
                                 .Where(k => !k.ToString().Equals("user:nextId", System.StringComparison.OrdinalIgnoreCase))
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

            // GET a user by id.
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
                if (user is null || string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Email))
                {
                    return Results.BadRequest(new { Error = "Invalid user data. 'Name' and 'Email' are required." });
                }
                if (!EmailValidator.IsValidEmail(user.Email))
                {
                    return Results.BadRequest(new { Error = "Invalid email format." });
                }
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
                if (updatedUser is null || string.IsNullOrWhiteSpace(updatedUser.Name) || string.IsNullOrWhiteSpace(updatedUser.Email))
                {
                    return Results.BadRequest(new { Error = "Invalid user data. 'Name' and 'Email' are required." });
                }
                if (!EmailValidator.IsValidEmail(updatedUser.Email))
                {
                    return Results.BadRequest(new { Error = "Invalid email format." });
                }
                updatedUser.Id = id;
                var userJson = JsonSerializer.Serialize(updatedUser);
                await db.StringSetAsync(key, userJson);
                return Results.NoContent();
            });

            // DELETE a user by id.
            app.MapDelete("/users/{id:int}", async (int id) =>
            {
                var key = $"user:{id}";
                if (!await db.KeyDeleteAsync(key))
                {
                    return Results.NotFound(new { Error = $"User with ID {id} not found." });
                }
                return Results.NoContent();
            });

            // GET search for users by name (partial and case‑insensitive).
            app.MapGet("/users/search", async (string name) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Results.BadRequest(new { Error = "Search term cannot be empty." });
                }
                var server = redis.GetServer(redis.GetEndPoints().First());
                var keys = server.Keys(pattern: "user:*")
                                 .Where(k => !k.ToString().Equals("user:nextId", System.StringComparison.OrdinalIgnoreCase))
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
                            user.Name.Contains(name, System.StringComparison.OrdinalIgnoreCase))
                        {
                            matchingUsers.Add(user);
                        }
                    }
                }
                return Results.Ok(matchingUsers);
            });

            return app;
        }
    }
}