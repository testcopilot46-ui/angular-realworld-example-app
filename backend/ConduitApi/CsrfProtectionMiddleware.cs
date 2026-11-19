namespace ConduitApi;

public class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public CsrfProtectionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only check state-changing methods
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check Origin header for CSRF protection
        var origin = context.Request.Headers.Origin.ToString();
        var referer = context.Request.Headers.Referer.ToString();

        // Get allowed origins from configuration
        var allowedOrigins = _configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" };

        // If no Origin or Referer header, might be same-origin request or API client
        // For API clients, we can be more lenient, but for browsers we should check
        if (string.IsNullOrEmpty(origin) && string.IsNullOrEmpty(referer))
        {
            // No origin info - could be API client, allow but log
            await _next(context);
            return;
        }

        // Check if origin is in allowed list
        var isAllowed = allowedOrigins.Any(allowed =>
            origin.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            referer.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("CSRF protection: Invalid origin");
            return;
        }

        await _next(context);
    }
}
