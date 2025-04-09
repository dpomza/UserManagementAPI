using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace UserManagementAPI.Middleware
{
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        public AuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { Error = "Missing Authorization header." });
                return;
            }

            var parts = authHeader.ToString().Split(' ');
            if (parts.Length < 2 ||
                !parts[0].Equals("Bearer", System.StringComparison.OrdinalIgnoreCase) ||
                parts[1] != "mysecrettoken")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { Error = "Invalid token." });
                return;
            }

            await _next(context);
        }
    }
}