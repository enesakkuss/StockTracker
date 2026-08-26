using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Net;
using StockTracker.Application.Common;

namespace StockTracker.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers["X-Correlation-ID"] = correlationId;
        context.Items["CorrelationId"] = correlationId;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        var (statusCode, errorCode, userMessage) = exception switch
        {
            ArgumentNullException or ArgumentException => (HttpStatusCode.BadRequest, "VALIDATION_ERROR", exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND", exception.Message),
            InvalidOperationException => (HttpStatusCode.Conflict, "CONFLICT", exception.Message),
            NotSupportedException => (HttpStatusCode.UnprocessableEntity, "UNSUPPORTED_OPERATION", exception.Message),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_SERVER_ERROR", "Sunucu tarafında beklenmeyen bir hata meydana geldi.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "[CorrelationId: {CorrelationId}] Unhandled internal exception: {Message}", correlationId, exception.Message);
        }
        else
        {
            _logger.LogWarning("[CorrelationId: {CorrelationId}] Handled exception: {ErrorCode} - {Message}", correlationId, errorCode, exception.Message);
        }

        if (!context.Response.HasStarted)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = (int)statusCode;

            var response = ApiResponse<object>.Fail(errorCode, userMessage, null, correlationId);
            var json = JsonSerializer.Serialize(response, JsonOptions);

            await context.Response.WriteAsync(json);
        }
    }
}
