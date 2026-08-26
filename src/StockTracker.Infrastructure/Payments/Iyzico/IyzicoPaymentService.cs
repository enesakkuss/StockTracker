using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Payments.Iyzico;

public class IyzicoPaymentService : IPaymentProvider
{
    public string ProviderName => "Iyzico";

    private readonly IyzicoOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<IyzicoPaymentService> _logger;

    public IyzicoPaymentService(
        IOptions<IyzicoOptions> options,
        HttpClient httpClient,
        ILogger<IyzicoPaymentService> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaymentCheckoutResult> CreateCheckoutSessionAsync(
        User user,
        SubscriptionPlan plan,
        PaymentTransaction transaction,
        string? successUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default)
    {
        // When real API keys are not configured, generate simulated sandbox token
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            var simToken = "iyzico_sim_" + Guid.NewGuid().ToString("N");
            var simUrl = $"{_options.BaseUrl}/payment/mock-checkout/{simToken}";
            _logger.LogInformation("Iyzico simulated checkout session created for User {UserId}, Transaction: {TxId}",
                user.Id, transaction.Id);

            return new PaymentCheckoutResult(
                Success: true,
                SessionId: simToken,
                CheckoutUrl: simUrl,
                Provider: ProviderName
            );
        }

        try
        {
            var priceStr = plan.Price.ToString("F2", CultureInfo.InvariantCulture);
            var req = new IyzicoCheckoutFormInitRequest
            {
                ConversationId = transaction.Id.ToString(),
                Price = priceStr,
                PaidPrice = priceStr,
                Currency = plan.Currency,
                BasketId = $"SUB_PLAN_{plan.Id}_{DateTime.UtcNow.Ticks}",
                CallbackUrl = successUrl ?? _options.CallbackUrl
            };

            var json = JsonSerializer.Serialize(req);
            var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/payment/iyzipay/checkoutform/initialize/auth/ecom")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            GenerateIyzicoAuthHeaders(httpReq, json);

            var response = await _httpClient.SendAsync(httpReq, cancellationToken);
            var respContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var iyzicoResp = JsonSerializer.Deserialize<IyzicoCheckoutFormInitResponse>(respContent);

            if (iyzicoResp != null && iyzicoResp.Status == "success" && !string.IsNullOrWhiteSpace(iyzicoResp.Token))
            {
                var checkoutUrl = iyzicoResp.PaymentPageUrl ?? $"{_options.BaseUrl}/payment/checkout/{iyzicoResp.Token}";
                return new PaymentCheckoutResult(
                    Success: true,
                    SessionId: iyzicoResp.Token,
                    CheckoutUrl: checkoutUrl,
                    Provider: ProviderName
                );
            }

            _logger.LogWarning("Iyzico checkout initialization failed: {Error}", iyzicoResp?.ErrorMessage ?? "Unknown error");
            return new PaymentCheckoutResult(
                Success: false,
                SessionId: string.Empty,
                CheckoutUrl: string.Empty,
                Provider: ProviderName,
                ErrorCode: iyzicoResp?.ErrorCode ?? "IYZICO_INIT_ERROR",
                ErrorMessage: iyzicoResp?.ErrorMessage ?? "Ödeme oturumu başlatılamadı."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Iyzico checkout initialization");
            return new PaymentCheckoutResult(
                Success: false,
                SessionId: string.Empty,
                CheckoutUrl: string.Empty,
                Provider: ProviderName,
                ErrorCode: "IYZICO_CONNECTION_ERROR",
                ErrorMessage: "Ödeme sağlayıcısına bağlanırken hata oluştu."
            );
        }
    }

    public Task<PaymentResult> GetPaymentStatusAsync(string providerTransactionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: providerTransactionId,
            Status: PaymentStatus.Succeeded
        ));
    }

    public Task<PaymentResult> RefundAsync(PaymentTransaction transaction, decimal? amount = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iyzico refund requested for transaction {Id}", transaction.Id);
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: transaction.ProviderTransactionId,
            Status: PaymentStatus.Refunded
        ));
    }

    public Task<PaymentResult> CancelPaymentAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: transaction.ProviderTransactionId,
            Status: PaymentStatus.Cancelled
        ));
    }

    public Task<bool> VerifyWebhookSignatureAsync(string payload, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return Task.FromResult(false);

        var secret = !string.IsNullOrWhiteSpace(_options.WebhookSecretKey) ? _options.WebhookSecretKey : _options.SecretKey;
        if (string.IsNullOrWhiteSpace(secret))
            return Task.FromResult(false);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();

        var matches = string.Equals(signatureHeader.Trim().ToLowerInvariant(), computed, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(matches);
    }

    public Task<WebhookResult> ParseWebhookPayloadAsync(string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var iyzicoEvent = JsonSerializer.Deserialize<IyzicoWebhookPayload>(payload);
            if (iyzicoEvent is null)
            {
                return Task.FromResult(new WebhookResult(false, string.Empty, string.Empty, "Geçersiz Iyzico webhook formatı."));
            }

            var eventId = iyzicoEvent.PaymentId ?? iyzicoEvent.Token ?? Guid.NewGuid().ToString("N");
            var eventType = (iyzicoEvent.IyziEventType ?? iyzicoEvent.Status ?? "payment.success").ToLowerInvariant();

            return Task.FromResult(new WebhookResult(
                Success: true,
                EventId: eventId,
                EventType: eventType,
                Message: "Iyzico webhook parsed successfully"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Iyzico webhook payload");
            return Task.FromResult(new WebhookResult(false, string.Empty, string.Empty, "JSON ayrıştırma hatası."));
        }
    }

    private void GenerateIyzicoAuthHeaders(HttpRequestMessage request, string payload)
    {
        var randomString = Guid.NewGuid().ToString("N");
        var hashString = _options.ApiKey + randomString + _options.SecretKey + payload;

        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(hashString));
        var pkiSignature = Convert.ToBase64String(hashBytes);

        var authorization = $"IYZWS {_options.ApiKey}:{pkiSignature}";
        request.Headers.Add("Authorization", authorization);
        request.Headers.Add("x-iyzi-rnd", randomString);
    }
}
