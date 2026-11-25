namespace ConduitApi;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTime = DateTime.UtcNow;
        var request = context.Request;

        // Log request
        _logger.LogInformation(
            "Request: {Method} {Path} from {RemoteIp}",
            request.Method,
            request.Path,
            context.Connection.RemoteIpAddress
        );

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request failed: {Method} {Path}", request.Method, request.Path);
            throw;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Response: {StatusCode} for {Method} {Path} - Duration: {Duration}ms",
                context.Response.StatusCode,
                request.Method,
                request.Path,
                duration.TotalMilliseconds
            );
        }
    }
}
