using ConduitApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<TokenService>(sp => new TokenService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IWebHostEnvironment>()
));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<IWebHostEnvironment>(builder.Environment);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new UtcDateTimeConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .WithHeaders("Content-Type", "Authorization")
    .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
    .AllowCredentials()));

var app = builder.Build();
var isDevelopment = builder.Environment.IsDevelopment();

app.Services.GetRequiredService<TokenService>();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreated();
}

app.UseCors();

// Forwarded headers middleware - must be before other middleware that uses IP
// Configure trusted proxies/networks in production
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

// In development, trust all proxies (localhost)
if (isDevelopment)
{
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    forwardedHeadersOptions.ForwardLimit = null;
}
else
{
    // In production, only trust specific proxies (configure via appsettings)
    // forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
    // forwardedHeadersOptions.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
}

app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<CsrfProtectionMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    // Content-Security-Policy
    var csp = "default-src 'self'; " +
               "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
               "style-src 'self' 'unsafe-inline'; " +
               "img-src 'self' data: https:; " +
               "font-src 'self'; " +
               "connect-src 'self' http://localhost:3000 https://localhost:3000; " +
               "frame-ancestors 'none'; " +
               "cookie-preference: 'self';";
    context.Response.Headers.Append("Content-Security-Policy", csp);

    // HSTS (only in production)
    if (!isDevelopment)
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

app.MapConduitApi();

app.Run();
