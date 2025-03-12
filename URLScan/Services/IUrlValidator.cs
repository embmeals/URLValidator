using URLScan.Models;

namespace URLScan.Services;

public interface IUrlValidator
{
    Task<List<UrlValidationResult?>> ValidateUrlsAsync(List<string> urls);
}