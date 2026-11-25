using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Pounce.Core;

/// <summary>
/// Talks to the Engine over its localhost REST API: exchanges the bootstrap secret for a JWT (kept until the
/// Engine refuses it, then fetched again once), and reads each search as a stream of <c>early</c> then
/// <c>final</c> events.
/// </summary>
/// <remarks>
/// Create one per Engine address and keep it for the life of the app: it holds one <see cref="HttpClient"/> whose
/// connections are reused between requests. Safe to use from any thread, and for overlapping searches. Its
/// continuations never run on the caller's <see cref="SynchronizationContext"/>.
/// </remarks>
public sealed class HttpEngineClient : ISearchEngine, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _searchUrl;
    private readonly Uri _tokenUrl;
    private string? _token;

    /// <summary>A client for the Engine at <paramref name="baseUrl"/>, logging in with the secret in <paramref name="secretFile"/>.</summary>
    public HttpEngineClient(Uri baseUrl, string secretFile)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(secretFile);
        BaseUrl = baseUrl;
        SecretFile = secretFile;
        _searchUrl = Endpoint(baseUrl, "search");
        _tokenUrl = Endpoint(baseUrl, "auth/token");
        // No overall timeout: a search streams for as long as the Engine works on it, and is cancelled by the caller.
        _http = new HttpClient(CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>The Engine's address, such as <c>http://127.0.0.1:7431</c>.</summary>
    public Uri BaseUrl { get; }

    /// <summary>The file holding the bootstrap secret.</summary>
    public string SecretFile { get; }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SearchPhase> SearchAsync(string query, int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Nothing below runs on the caller's thread, so a search never blocks the UI, not even to read the secret file.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        using HttpResponseMessage response = await OpenSearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Stream body = await Guard(response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(body, Encoding.UTF8);

        // The next line is read ahead while the caller holds a phase, so a cancel at any moment aborts a pending read,
        // which closes the connection at once and tells the Engine to stop working on this Query.
        Task<string?>? pending = Guard(reader.ReadLineAsync(reading.Token).AsTask(), reading.Token);
        bool finished = false;
        try
        {
            string eventName = "";
            while (pending is not null)
            {
                string? line = await pending.ConfigureAwait(false);
                pending = null;
                if (line is null)
                {
                    finished = true;
                    yield break;
                }
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventName = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    List<ResultItem> items = JsonSerializer.Deserialize(line.AsSpan("data:".Length), PounceJson.Default.ListResultItem) ?? [];
                    if (eventName == "final")
                    {
                        // Stop here: the rest of the response is only its end, which the connection pool reads, so the
                        // connection is kept for the next request.
                        finished = true;
                        yield return SearchPhase.Final(items);
                        yield break;
                    }
                    pending = Guard(reader.ReadLineAsync(reading.Token).AsTask(), reading.Token);
                    yield return SearchPhase.Early(items);
                    continue;
                }
                pending = Guard(reader.ReadLineAsync(reading.Token).AsTask(), reading.Token);
            }
        }
        finally
        {
            if (!finished)
            {
                await reading.CancelAsync().ConfigureAwait(false);
                if (pending is not null)
                {
                    try
                    {
                        await pending.ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is OperationCanceledException or EngineException)
                    {
                        // Cancelled on purpose.
                    }
                }
                await AbandonAsync(body).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Releases the connections.</summary>
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// The handler behind every request to the Engine: no proxy (on corporate Windows the system proxy or WPAD lookup
    /// can add seconds, even for 127.0.0.1), no cookies, no decompression; connections are kept alive.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Closes the connection of an unfinished answer at once. Disposing it would instead have the connection pool
    /// wait up to 2 seconds for the rest (while the Engine keeps working on a Query nobody wants), so this skips what
    /// has already arrived and cancels the first read that has to wait for the Engine, which aborts the connection.
    /// </summary>
    private static async Task AbandonAsync(Stream body)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (true)
            {
                using var abort = new CancellationTokenSource();
                ValueTask<int> read = body.ReadAsync(buffer, abort.Token);
                if (!read.IsCompleted)
                {
                    await abort.CancelAsync().ConfigureAwait(false);
                }
                if (await read.ConfigureAwait(false) == 0)
                {
                    return; // The answer was complete after all; the connection goes back to the pool.
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Aborted, or already closed: either way the connection is gone.
        }
    }

    /// <summary>The address of <paramref name="path"/> below <paramref name="baseUrl"/>, keeping any path the base has.</summary>
    internal static Uri Endpoint(Uri baseUrl, string path) => new(baseUrl.AbsoluteUri.TrimEnd('/') + "/" + path);

    /// <summary>Sends the search, logging in first if needed and once more if the Engine refuses the token.</summary>
    private async Task<HttpResponseMessage> OpenSearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var url = new Uri($"{_searchUrl.AbsoluteUri}?q={Uri.EscapeDataString(query)}&limit={limit}");
        for (int attempt = 0; ; attempt++)
        {
            string token = await TokenAsync(cancellationToken).ConfigureAwait(false);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await Guard(
                _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                // Forget this token only: an overlapping search may already have fetched a newer one.
                Interlocked.CompareExchange(ref _token, null, token);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Dispose();
                throw EngineException.Unexpected((int)response.StatusCode);
            }
            return response;
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _token) is { } cached)
        {
            return cached;
        }
        string secret;
        try
        {
            secret = (await File.ReadAllTextAsync(SecretFile, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw EngineException.SecretMissing(SecretFile, e);
        }

        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new TokenRequest(secret), PounceJson.Default.TokenRequest));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await Guard(_http.PostAsync(_tokenUrl, content, cancellationToken), cancellationToken).ConfigureAwait(false);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                byte[] json = await Guard(response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                string token = JsonSerializer.Deserialize(json, PounceJson.Default.TokenResponse)!.Token;
                Volatile.Write(ref _token, token);
                return token;
            case HttpStatusCode.Unauthorized:
                throw EngineException.SecretRefused(SecretFile);
            default:
                throw EngineException.Unexpected((int)response.StatusCode);
        }
    }

    /// <summary>Awaits a network operation, turning "nothing answers" into <see cref="EngineProblem.NotRunning"/>.</summary>
    private async Task<T> Guard<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation.ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && IsUnreachable(e))
        {
            throw EngineException.NotRunning(BaseUrl, e);
        }
    }

    /// <summary>Refused or reset connections, an Engine that went away mid-answer, and timeouts.</summary>
    private static bool IsUnreachable(Exception e) => e switch
    {
        HttpRequestException h => h.HttpRequestError is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded,
        IOException => true,
        // A timeout (not a cancel by the caller, which the guard has already excluded).
        OperationCanceledException => true,
        _ => false,
    };
}
