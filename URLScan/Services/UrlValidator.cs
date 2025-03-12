using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using HtmlAgilityPack;

namespace URLScan.Services;

public static class UrlStatus
{
    public const string Indexed = "Indexed";
    public const string Invalid = "Invalid URL";
    public const string NotFound = "404 Not Found";
    public const string ServerError = "Server Error";
    public const string NoIndex = "NoIndex Found";
    public const string EmptyPage = "Empty Page";
}

public class UrlValidator : IUrlValidator
{
    private readonly HttpClient _httpClient;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ILogger<UrlValidator> _logger;

    public UrlValidator(HttpClient httpClient, ILogger<UrlValidator> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _logger = logger;
    }

    public async Task<List<UrlValidationResult?>> ValidateUrlsAsync(List<string> urls)
    {
        var results = new ConcurrentBag<UrlValidationResult?>();

        var distinctUrls = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList();

        _logger.LogInformation($"Processing {distinctUrls.Count} distinct URLs from {urls.Count} total URLs");

        var invalidUrls = distinctUrls
            .Where(u => !Uri.IsWellFormedUriString(u, UriKind.Absolute))
            .ToList();

        foreach (var invalidUrl in invalidUrls)
        {
            results.Add(new UrlValidationResult(invalidUrl, UrlStatus.Invalid, "Malformed URL"));
            _logger.LogDebug($"Invalid URL format: {invalidUrl}");
        }

        var validUrls = distinctUrls.Except(invalidUrls).ToList();
        _logger.LogInformation($"Found {validUrls.Count} valid URLs to check");

        await Parallel.ForEachAsync(
            validUrls,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (url, token) =>
            {
                try
                {
                    var result = await ValidateUrlAsync(url);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing URL: {url}");
                    results.Add(new UrlValidationResult(url, UrlStatus.Invalid, $"Processing error: {ex.Message}"));
                }
            });

        var processedUrls = results
            .Select(r => r?.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var url in distinctUrls)
        {
            if (!processedUrls.Contains(url))
            {
                _logger.LogWarning($"URL was not processed: {url}");
                results.Add(new UrlValidationResult(url, UrlStatus.Invalid, "URL was not processed"));
            }
        }

        var finalResults = results
            .Where(r => r != null)
            .ToList();

        _logger.LogInformation($"Returning {finalResults.Count} results");

        return finalResults
            .OrderBy(r => r!.Status == UrlStatus.NoIndex ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.Invalid ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.NotFound ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.ServerError ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.Indexed ? 1 : 0)
            .ThenBy(r => r!.Url)
            .ToList();
    }

    private async Task<UrlValidationResult?> ValidateUrlAsync(string url)
    {
        if (_cache.TryGetValue(url, out UrlValidationResult? cachedResult))
        {
            _logger.LogDebug($"Cache hit for URL: {url}");
            return cachedResult;
        }

        var category = DetermineCategory(url);

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            return CacheResult(url, UrlStatus.Invalid, "Malformed URL", category);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

        HttpResponseMessage response;
        try
        {
            _logger.LogDebug($"Sending HTTP request to: {url}");
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            _logger.LogDebug($"Received response {(int)response.StatusCode} from: {url}");
        }
        catch (TaskCanceledException)
        {
            return CacheResult(url, UrlStatus.ServerError, "Request timed out", category);
        }
        catch (HttpRequestException ex)
        {
            return CacheResult(url, UrlStatus.Invalid, $"Connection failed: {ex.Message}", category);
        }
        catch (Exception ex)
        {
            return CacheResult(url, UrlStatus.Invalid, $"Error: {ex.Message}", category);
        }

        if ((int)response.StatusCode == 404)
            return CacheResult(url, UrlStatus.NotFound, "Page does not exist", category);
        if ((int)response.StatusCode >= 500)
            return CacheResult(url, UrlStatus.ServerError, $"HTTP {(int)response.StatusCode} - Server issue", category);
        if (!response.IsSuccessStatusCode)
            return CacheResult(url, UrlStatus.Invalid, $"HTTP {(int)response.StatusCode} - Request failed", category);

        if (HasNoIndexHeader(response))
            return CacheResult(url, UrlStatus.NoIndex, "Page is marked 'noindex' via HTTP headers", category);

        string html;
        try
        {
            html = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return CacheResult(url, UrlStatus.EmptyPage, $"Error reading content: {ex.Message}", category);
        }

        if (string.IsNullOrWhiteSpace(html))
            return CacheResult(url, UrlStatus.EmptyPage, "No content found", category);

        if (CheckForNoIndexMetaTag(html))
            return CacheResult(url, UrlStatus.NoIndex, "Page contains 'noindex' meta tag", category);

        return CacheResult(url, UrlStatus.Indexed, "Page is NOT noindexed", category);
    }

    private UrlValidationResult CacheResult(string url, string status, string? details, string? category)
    {
        var result = new UrlValidationResult(url, status, details ?? string.Empty, category ?? "Uncategorized");
        _cache.Set(url, result, TimeSpan.FromMinutes(30));
        return result;
    }

    private bool HasNoIndexHeader(HttpResponseMessage response)
    {
        if (!response.Headers.Contains("X-Robots-Tag"))
            return false;

        return response.Headers
            .GetValues("X-Robots-Tag")
            .Any(value => value.Contains("noindex", StringComparison.OrdinalIgnoreCase));
    }

    private bool CheckForNoIndexMetaTag(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var robotsMeta = doc.DocumentNode.SelectNodes("//meta[@name='robots']");
        if (robotsMeta != null && robotsMeta.Any(meta =>
                meta.GetAttributeValue("content", "")
                    .Contains("noindex", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var googlebotMeta = doc.DocumentNode.SelectNodes("//meta[@name='googlebot']");
        if (googlebotMeta != null && googlebotMeta.Any(meta =>
                meta.GetAttributeValue("content", "")
                    .Contains("noindex", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private string DetermineCategory(string url)
    {
        if (url.Contains("/jobs/") || url.Contains("/careers/") || url.Contains("/job-"))
            return "Job Listings";
        if (url.Contains("/articles/") || url.Contains("/blog/") || url.Contains("/post/"))
            return "Articles";
        if (url.Contains("/news/") || url.Contains("/press/"))
            return "News";
        if (url.Contains("/events/") || url.Contains("/webinars/"))
            return "Events";
        if (url.Contains("/about/") || url.Contains("/company/"))
            return "Company";
        if (url.Contains("/products/") || url.Contains("/services/"))
            return "Products";
        if (url.Contains("/support/") || url.Contains("/help/"))
            return "Support";

        return "Uncategorized";
    }
}

public class UrlValidationResult
{
    public UrlValidationResult(string url, string status, string? details = "", string category = "Uncategorized")
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Details = details ?? string.Empty;
        Category = category;
    }

    public string Url { get; }
    public string Status { get; }
    public string Details { get; }
    public string Category { get; }
}