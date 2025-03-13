using URLScan.Models;

namespace URLScan.Extensions;

public static class HttpClientExtensions
{
    public static async Task<Result<HttpResponseMessage>> SafeSendAsync(
        this HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.SendAsync(request, completionOption, cancellationToken);
            return Result<HttpResponseMessage>.Success(response);
        }
        catch (Exception ex)
        {
            return Result<HttpResponseMessage>.Failure(ex.Message);
        }
    }
}