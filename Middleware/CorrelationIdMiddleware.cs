using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace UserManagementAPI.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
                // Using the indexer sets the header without throwing on duplicates.
                context.Request.Headers["X-Correlation-ID"] = correlationId;
            }

            context.Response.OnStarting(() =>
            {
                // Use the indexer to ensure the header is set without duplicates.
                context.Response.Headers["X-Correlation-ID"] = correlationId;
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}