using System.Text.RegularExpressions;

namespace StockTracker.Application.Common;

/// <summary>
/// Robust size/variant matching logic across different store naming conventions.
/// Handles cases like "M" matching "M (US M)", "38" matching "38 (EU 38)", etc.
/// </summary>
public static class VariantMatcher
{
    private static readonly Regex ParenthesisRegex = new(@"\s*\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex PrefixRegex = new(@"\b(EU|US|UK|IT|FR|TR)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsMatch(string selectedVariant, string candidateVariant)
    {
        if (string.IsNullOrWhiteSpace(selectedVariant) || string.IsNullOrWhiteSpace(candidateVariant))
            return false;

        // 1. Direct case-insensitive match
        if (string.Equals(selectedVariant.Trim(), candidateVariant.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Base token comparison (e.g. "M" from "M (US M)")
        var normSelected = Normalize(selectedVariant);
        var normCandidate = Normalize(candidateVariant);

        if (string.Equals(normSelected, normCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3. Prefix/suffix containment with word boundaries
        if (normCandidate.Length > 0 && normSelected.Length > 0)
        {
            if (normCandidate.Equals(normSelected, StringComparison.OrdinalIgnoreCase))
                return true;

            // e.g. "38" matching "EU 38" or "38 EU"
            var cleanCandidate = PrefixRegex.Replace(candidateVariant, "").Trim();
            var cleanSelected = PrefixRegex.Replace(selectedVariant, "").Trim();
            if (string.Equals(cleanSelected, cleanCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string Normalize(string variant)
    {
        if (string.IsNullOrWhiteSpace(variant)) return string.Empty;

        // Remove parenthesis and their content: "M (US M)" -> "M"
        var withoutParens = ParenthesisRegex.Replace(variant, "").Trim();

        // Remove known country prefixes: "EU 38" -> "38"
        var clean = PrefixRegex.Replace(withoutParens, "").Trim();

        return clean.ToUpperInvariant();
    }
}
