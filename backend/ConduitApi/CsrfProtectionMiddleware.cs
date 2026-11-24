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

        // For state-changing requests, require Origin or Referer header
        var origin = context.Request.Headers.Origin.ToString();
        var referer = context.Request.Headers.Referer.ToString();

        // Get allowed origins from configuration
        var allowedOrigins = _configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:4200" };

        // If no Origin or Referer header, the request is likely a non-browser API client.
        // However, a request that relies on the ambient jwt cookie (no Authorization header)
        // could still be a forged cross-site form post, so require a custom header that
        // browsers cannot set without a CORS preflight.
        if (string.IsNullOrEmpty(origin) && string.IsNullOrEmpty(referer))
        {
            var usesCookieAuth = !string.IsNullOrEmpty(context.Request.Cookies["jwt"])
                && string.IsNullOrEmpty(context.Request.Headers.Authorization.ToString());

            if (usesCookieAuth &&
                !context.Request.Headers["X-Requested-With"].ToString()
                    .Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("CSRF protection: missing X-Requested-With header");
                return;
            }

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
