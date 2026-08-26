using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace StockTracker.Api.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // Content-Security-Policy configured for single-page app and external product images
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https: http:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";

        // Strict-Transport-Security when HTTPS or in non-development
        if (context.Request.IsHttps || !_env.IsDevelopment())
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        // Remove server headers before invoking downstream
        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        await _next(context);

        // Safe removal if response has not started
        if (!context.Response.HasStarted)
        {
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
        }
    }
}
