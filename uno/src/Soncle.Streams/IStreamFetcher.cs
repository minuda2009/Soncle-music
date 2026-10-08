// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
namespace Soncle.Streams;

/// <summary>
/// How a byte range of a song is fetched from googlevideo. The real implementation uses
/// <see cref="HttpClient"/>; tests inject a fake googlevideo server, so no test reaches the
/// network. A 403 must surface as <see cref="StreamExpiredException"/> so the downloader can ask
/// for a fresh URL.
/// </summary>
public interface IStreamFetcher
{
    Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken cancellationToken = default);
}

/// <summary>The stream URL has expired (HTTP 403): ask <see cref="IStreamRefresher"/> for a new one.</summary>
public sealed class StreamExpiredException : Exception
{
    public StreamExpiredException() : base("HTTP 403") { }
}

/// <summary>An upstream request failed with a non-recoverable status.</summary>
public sealed class StreamHttpException : Exception
{
    public int Status { get; }
    public StreamHttpException(int status) : base($"HTTP {status}") => Status = status;
}

/// <summary>
/// HttpClient-backed fetcher. One <see cref="HttpClient"/> (one connection pool) is shared by
/// every download; the caller supplies it, configured with the timeouts it wants.
/// </summary>
public sealed class HttpStreamFetcher : IStreamFetcher
{
    private readonly HttpClient _http;

    public HttpStreamFetcher(HttpClient http) => _http = http;

    public async Task<Stream> GetAsync(string url, IReadOnlyDictionary<string, string> headers, long from, long to, CancellationToken cancellationToken = default)
    {
        var sep = url.Contains('?') ? '&' : '?';
        using var request = new HttpRequestMessage(HttpMethod.Get, url + sep + $"range={from}-{to}");
        foreach (var (k, v) in headers) request.Headers.TryAddWithoutValidation(k, v);
        var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode == 403)
        {
            response.Dispose();
            throw new StreamExpiredException();
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new StreamHttpException(status);
        }
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }
}
