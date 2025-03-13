namespace URLScan.Models;

public class UrlValidationResult(
    string url,
    string status,
    string? details = "",
    string category = "Uncategorized",
    Dictionary<string, string>? metaTags = null)
{
    public string Url { get; } = url      ?? throw new ArgumentNullException(nameof(url));
    public string Status { get; } = status   ?? throw new ArgumentNullException(nameof(status));
    public string Details { get; } = details  ?? string.Empty;
    public string Category { get; } = category;

    public Dictionary<string, string> MetaTags { get; } = metaTags ?? new Dictionary<string, string>();
}