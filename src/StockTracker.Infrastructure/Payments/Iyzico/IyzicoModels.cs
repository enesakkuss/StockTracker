using System.Text.Json.Serialization;

namespace StockTracker.Infrastructure.Payments.Iyzico;

public class IyzicoCheckoutFormInitRequest
{
    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "tr";

    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0.0";

    [JsonPropertyName("paidPrice")]
    public string PaidPrice { get; set; } = "0.0";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "TRY";

    [JsonPropertyName("basketId")]
    public string BasketId { get; set; } = string.Empty;

    [JsonPropertyName("paymentGroup")]
    public string PaymentGroup { get; set; } = "PRODUCT";

    [JsonPropertyName("callbackUrl")]
    public string CallbackUrl { get; set; } = string.Empty;

    [JsonPropertyName("enabledInstallments")]
    public List<int> EnabledInstallments { get; set; } = new() { 1 };
}

public class IyzicoCheckoutFormInitResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("checkoutFormContent")]
    public string? CheckoutFormContent { get; set; }

    [JsonPropertyName("paymentPageUrl")]
    public string? PaymentPageUrl { get; set; }
}

public class IyzicoWebhookPayload
{
    [JsonPropertyName("iyziEventType")]
    public string? IyziEventType { get; set; }

    [JsonPropertyName("iyziEventTime")]
    public long IyziEventTime { get; set; }

    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("paymentConversationId")]
    public string? PaymentConversationId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
