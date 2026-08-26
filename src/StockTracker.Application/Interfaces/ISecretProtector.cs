namespace StockTracker.Application.Interfaces;

/// <summary>
/// Abstraction for protecting and unprotecting sensitive secrets (such as Telegram Bot Tokens).
/// Keeps encryption/protection logic decoupled from domain and application layers.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Protects/encrypts a plain text secret.
    /// </summary>
    string Protect(string plainText);

    /// <summary>
    /// Unprotects/decrypts a protected secret.
    /// </summary>
    string Unprotect(string protectedText);
}
