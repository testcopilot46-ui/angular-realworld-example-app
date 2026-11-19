using System.Collections.Concurrent;
using System.Linq;

namespace ConduitApi;

public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimitStore;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private readonly int _maxRequests = 100;
    private readonly Timer _cleanupTimer;

    public RateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
        _rateLimitStore = new ConcurrentDictionary<string, RateLimitEntry>();
        // Clean up expired entries every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientId(context);

        if (string.IsNullOrEmpty(clientId))
        {
            await _next(context);
            return;
        }

        var now = DateTime.UtcNow;
        var entry = _rateLimitStore.GetOrAdd(clientId, _ => new RateLimitEntry { Count = 0, WindowStart = now });

        // Reset if window expired
        if (now - entry.WindowStart > _window)
        {
            entry.Count = 0;
            entry.WindowStart = now;
        }

        entry.Count++;

        if (entry.Count > _maxRequests)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        await _next(context);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _rateLimitStore
            .Where(kvp => now - kvp.Value.WindowStart > _window * 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _rateLimitStore.TryRemove(key, out _);
        }
    }

    private static string? GetClientId(HttpContext context)
    {
        // Use client IP as identifier
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp != null)
        {
            return $"ip:{remoteIp}";
        }

        return null;
    }

    private class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime WindowStart { get; set; }
    }
}
