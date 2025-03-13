namespace URLScan.Models;

public class UrlValidationResult(
    string url,
    string status,
    string? messageDetails = "",
    string tagCategory = "Uncategorized",
    string metaTags = ""
)
{
    public string Url { get; } = EnsureNotNullOrEmpty(url, nameof(url));
    public string Status { get; } = EnsureNotNullOrEmpty(status, nameof(status));
    public string Details { get; } = messageDetails ?? string.Empty;
    public string Category { get; } = tagCategory;
    public string MetaTags { get; } = metaTags;

    private static string EnsureNotNullOrEmpty(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentNullException(paramName) : value;
}