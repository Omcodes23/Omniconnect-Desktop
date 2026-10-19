using System.Net;
using System.Text.Json;

namespace OmniConnect.Backend.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller hung up (window closed, navigation, timeout). There is
            // nobody left to answer, so this is normal traffic, not a fault.
            _logger.LogDebug(
                "Request aborted by client: {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);

            // Headers may already be on the wire (e.g. streaming); writing a
            // status at that point would throw a second, misleading error.
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                error = "An internal server error occurred.",
                detail = context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
                    ? ex.Message
                    : null
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
