namespace StockTracker.Infrastructure.Payments.Iyzico;

public class IyzicoOptions
{
    public const string SectionName = "Payment:Iyzico";

    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox-api.iyzipay.com";
    public string CallbackUrl { get; set; } = "https://app.stocktracker.local/api/payments/callback/iyzico";
    public string WebhookSecretKey { get; set; } = string.Empty;
}
