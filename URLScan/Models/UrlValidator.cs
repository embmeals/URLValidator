using System.Collections.Concurrent;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using URLScan.Extensions;
using URLScan.Services;

namespace URLScan.Models;

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
    private readonly ILogger<UrlValidator> _logger;
    private readonly MemoryCache _cache;

    public UrlValidator(HttpClient httpClient, ILogger<UrlValidator> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(50);

        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<List<UrlValidationResult?>> ValidateUrlsAsync(List<string> urls)
    {
        var results = new ConcurrentBag<UrlValidationResult?>();
        var distinctUrls = GetDistinctUrls(urls);

        _logger.LogInformation($"Processing {distinctUrls.Count} distinct URLs from {urls.Count} total URLs");

        var invalidUrls = MarkMalformedUrls(distinctUrls, results);

        var validUrls = distinctUrls.Except(invalidUrls).ToList();
        _logger.LogInformation($"Found {validUrls.Count} valid URLs to check");

        await Parallel.ForEachAsync(validUrls, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (url, token) =>
        {
            var result = await ValidateUrlAsync(url);
            results.Add(result);
        });

        MarkUnprocessed(distinctUrls, results);

        var finalResults = results
            .Where(r => r != null)
            .OrderBy(r => r!.Status == UrlStatus.NoIndex ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.Invalid ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.NotFound ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.ServerError ? 0 : 1)
            .ThenBy(r => r!.Status == UrlStatus.Indexed ? 1 : 0)
            .ThenBy(r => r!.Url)
            .ToList();

        _logger.LogInformation($"Returning {finalResults.Count} results");
        return finalResults;
    }

    private async Task<UrlValidationResult?> ValidateUrlAsync(string url)
    {
        if (_cache.TryGetValue(url, out UrlValidationResult? cached))
        {
            _logger.LogDebug($"Cache hit for URL: {url}");
            return cached;
        }

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            return CacheResult(url, UrlStatus.Invalid, "Malformed URL", "Uncategorized");

        var category = DetermineCategory(url);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

        var sendResult = await _httpClient.SafeSendAsync(request);
        if (!sendResult.IsSuccess)
        {
            var errorMsg = sendResult.Error ?? "Unknown error";

            if (errorMsg.Contains("canceled", StringComparison.OrdinalIgnoreCase))
                return CacheResult(url, UrlStatus.ServerError, "Request timed out", category);

            return CacheResult(url, UrlStatus.Invalid, $"Connection failed: {errorMsg}", category);
        }

        var response = sendResult.Value;
        var statusCode = (int)response.StatusCode;
        if (statusCode == 404)
            return CacheResult(url, UrlStatus.NotFound, "Page does not exist", category);
        if (statusCode >= 500)
            return CacheResult(url, UrlStatus.ServerError, $"HTTP {statusCode} - Server issue", category);
        if (!response.IsSuccessStatusCode)
            return CacheResult(url, UrlStatus.Invalid, $"HTTP {statusCode} - Request failed", category);

        if (HasNoIndexHeader(response))
            return CacheResult(url, UrlStatus.NoIndex, "Page is marked 'noindex' via HTTP headers", category);

        var contentResult = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(contentResult))
            return CacheResult(url, UrlStatus.EmptyPage, "No content found", category);

        var html = contentResult;
        if (string.IsNullOrWhiteSpace(html))
            return CacheResult(url, UrlStatus.EmptyPage, "No content found", category);
        
        var allMetaTags = ExtractAllMetaTags(html);
        if (HasNoIndexMetaTag(html))
            return CacheResult(url, UrlStatus.NoIndex, "Page contains 'noindex' meta tag", category);

        return CacheResult(url, UrlStatus.Indexed, "Page is NOT noindexed", category);
    }

    private static List<string> GetDistinctUrls(List<string> urls) =>
        urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<string> MarkMalformedUrls(List<string> distinctUrls, ConcurrentBag<UrlValidationResult?> results)
    {
        var invalidUrls = distinctUrls
            .Where(u => !Uri.IsWellFormedUriString(u, UriKind.Absolute))
            .ToList();

        foreach (var invalidUrl in invalidUrls)
        {
            results.Add(new UrlValidationResult(invalidUrl, UrlStatus.Invalid, "Malformed URL"));
            _logger.LogDebug($"Invalid URL format: {invalidUrl}");
        }

        return invalidUrls;
    }

    private void MarkUnprocessed(List<string> distinctUrls, ConcurrentBag<UrlValidationResult?> results)
    {
        var processedUrls = results
            .Select(r => r?.Url)
            .Where(url => url != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var url in distinctUrls)
        {
            if (!processedUrls.Contains(url))
            {
                _logger.LogWarning($"URL was not processed: {url}");
                results.Add(new UrlValidationResult(url, UrlStatus.Invalid, "URL was not processed"));
            }
        }
    }

private string ExtractAllMetaTags(string html)
{
    var htmlDocument = new HtmlDocument();
    htmlDocument.LoadHtml(html);

    var metaNodes = htmlDocument.DocumentNode.SelectNodes("//meta");
    if (metaNodes == null)
        return string.Empty;

    var metaTags = new List<string>();
    foreach (var metaNode in metaNodes)
    {
        var metaTag = ProcessMetaTag(metaNode);
        if (!string.IsNullOrEmpty(metaTag))
            metaTags.Add(metaTag);
    }

    return string.Join(" | ", metaTags);
}

private string ProcessMetaTag(HtmlNode metaNode)
{
    var name = metaNode.GetAttributeValue("name", string.Empty);
    var property = metaNode.GetAttributeValue("property", string.Empty);
    var content = metaNode.GetAttributeValue("content", string.Empty);

    if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(property) || !string.IsNullOrEmpty(content))
        return $"{name}={content}";

    return string.Empty;
}
    
    private UrlValidationResult CacheResult(
        string url,
        string status,
        string details,
        string category,
        string metaTags = "")
    {
        var result = new UrlValidationResult(url, status, details, category, metaTags);
        _cache.Set(url, result, TimeSpan.FromMinutes(30));
        return result;
    }

    private static bool HasNoIndexHeader(HttpResponseMessage response)
    {
        if (!response.Headers.Contains("X-Robots-Tag"))
            return false;

        return response.Headers
            .GetValues("X-Robots-Tag")
            .Any(value => value.Contains("noindex", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNoIndexMetaTag(string html)
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