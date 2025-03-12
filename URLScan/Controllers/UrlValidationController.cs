using Microsoft.AspNetCore.Mvc;
using URLScan.Services;

namespace URLScan.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UrlValidationController : ControllerBase
{
    private readonly IUrlValidator _urlValidator;
    private readonly ILogger<UrlValidationController> _logger;

    public UrlValidationController(IUrlValidator urlValidator, ILogger<UrlValidationController> logger)
    {
        _urlValidator = urlValidator;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateUrls([FromBody] List<string> urls)
    {
        if (urls == null || urls.Count == 0)
        {
            _logger.LogWarning("API called with an empty URL list.");
            return BadRequest("No URLs provided");
        }

        _logger.LogInformation($"Received {urls.Count} URLs for validation.");

        try
        {
            var results = await _urlValidator.ValidateUrlsAsync(urls);
            _logger.LogInformation($"Validation completed: {results.Count} results returned.");
            return Ok(results);
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Validation request timed out.");
            return StatusCode(504, "The request took too long and was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error: {ex.Message}");
            return StatusCode(500, "An unexpected error occurred.");
        }
    }
}