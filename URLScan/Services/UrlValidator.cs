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

    public UrlValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<UrlValidationResult?>> ValidateUrlsAsync(List<string> urls)
    {
        var results = new ConcurrentBag<UrlValidationResult?>();

        var distinctUrls = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct()
            .ToList();

        var invalidUrls = distinctUrls
            .Where(u => !Uri.IsWellFormedUriString(u, UriKind.Absolute))
            .ToList();

        foreach (var invalidUrl in invalidUrls) 
            results.Add(new UrlValidationResult(invalidUrl, UrlStatus.Invalid, "Malformed URL"));

        var validUrls = distinctUrls.Except(invalidUrls).ToList();

        await Parallel.ForEachAsync(validUrls, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (url, token) =>
        {
            var result = await ValidateUrlAsync(url);
            results.Add(result);
        });

        var finalResults = results
            .Where(r => r != null)
            .GroupBy(r => r!.Url.ToLowerInvariant().Split('?')[0]) 
            .Select(g => g.First())
            .ToList();

        return finalResults
            .OrderBy(r => r!.Status == UrlStatus.Indexed ? 0 : 1)    
            .ThenBy(r => r!.Status == UrlStatus.Invalid ? 0 : 1)   
            .ThenBy(r => r!.Status == UrlStatus.NotFound ? 1 : 2)      
            .ThenBy(r => r!.Status == UrlStatus.ServerError ? 2 : 3)     
            .ThenBy(r => r!.Status == UrlStatus.NoIndex ? 3 : 4)       
            .ThenBy(r => r!.Status)                                
            .ThenBy(r => r!.Url)                                     
            .ToList();
    }

    private async Task<UrlValidationResult?> ValidateUrlAsync(string url)
    {
        if (_cache.TryGetValue(url, out UrlValidationResult? cachedResult))
            return cachedResult;

        var category = DetermineCategory(url);

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            return CacheResult(url, UrlStatus.Invalid, "Malformed URL", category);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch
        {
            return CacheResult(url, UrlStatus.Invalid, "Connection failed", category);
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
        catch
        {
            return CacheResult(url, UrlStatus.EmptyPage, "Error reading content", category);
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
        if (robotsMeta == null)
            return false;

        return robotsMeta.Any(meta =>
            meta.GetAttributeValue("content", "")
                .Contains("noindex", StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineCategory(string url)
    {
        if (url.Contains("/jobs/"))
            return "Job Listings";
        if (url.Contains("/articles/"))
            return "Articles";
        if (url.Contains("/news/"))
            return "News";

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