using System.Text.RegularExpressions;

namespace SmileApi.Application.Validators;

public static class InputSanitizer
{
    private static readonly Regex SafePatientIdPattern = new(@"^[a-zA-Z0-9\-_.]+$", RegexOptions.Compiled);
    private static readonly Regex HtmlTagPattern = new(@"<[^>]*>", RegexOptions.Compiled);

    public static (bool IsValid, string Sanitized, string? Error) SanitizePatientId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (false, string.Empty, "ExternalPatientId is required.");

        var trimmed = input.Trim();
        if (trimmed.Length > 128)
            return (false, string.Empty, "ExternalPatientId must not exceed 128 characters.");
        if (!SafePatientIdPattern.IsMatch(trimmed))
            return (false, string.Empty, "ExternalPatientId contains invalid characters. Only alphanumeric, hyphens, underscores, and dots are allowed.");
        return (true, trimmed, null);
    }

    public static (bool IsValid, string Sanitized, string? Error) SanitizeImageUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (false, string.Empty, "ImageUrl is required.");

        var trimmed = input.Trim();
        if (HtmlTagPattern.IsMatch(trimmed))
            return (false, string.Empty, "ImageUrl contains invalid HTML content.");
        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:text", StringComparison.OrdinalIgnoreCase))
            return (false, string.Empty, "ImageUrl contains a forbidden URI scheme.");

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return (true, trimmed, null);
        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return (true, trimmed, null);

        return (false, string.Empty, "ImageUrl must be a valid HTTP(S) URL or a base64 image data URI.");
    }

    public static string StripDangerousContent(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var cleaned = Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty);
        cleaned = HtmlTagPattern.Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }
}
