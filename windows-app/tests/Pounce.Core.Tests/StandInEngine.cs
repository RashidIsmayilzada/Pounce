using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pounce.Core.Tests;

/// <summary>
/// A tiny HTTP/1.1 server on a real 127.0.0.1 socket that behaves like the Engine: <c>POST /auth/token</c>, then
/// <c>GET /search</c> as server-sent events. Connections are kept alive, as Spring's are.
/// </summary>
internal sealed class StandInEngine : IAsyncDisposable
{
    public const string Secret = "the-bootstrap-secret";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly HashSet<string> _validTokens = [];
    private readonly List<string> _searches = [];
    private readonly List<Task> _connections = [];
    private readonly TempFolder _folder = new();
    private Task _accepting = Task.CompletedTask;
    private int _issued;
    private int _tokenRequests;

    private StandInEngine()
    {
    }

    public Uri Url { get; private set; } = null!;

    public string EarlyJson { get; set; } = "[]";
    public string FinalJson { get; set; } = "[]";

    /// <summary>When set, every authorised search is answered with this status instead of results.</summary>
    public int? SearchStatus { get; set; }

    /// <summary>When set, every search token is refused, even a fresh one.</summary>
    public bool RefuseSearches { get; set; }

    /// <summary>When set, a search sends its Early Results and then keeps the stream open until the client leaves.</summary>
    public bool HoldAfterEarly { get; set; }

    /// <summary>When set, requests are read but never answered.</summary>
    public bool NeverAnswer { get; set; }

    /// <summary>Completes when the client closes a held search stream.</summary>
    public TaskCompletionSource ClientLeft { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int TokenRequests
    {
        get
        {
            lock (_lock)
            {
                return _tokenRequests;
            }
        }
    }

    /// <summary>How many connections clients opened.</summary>
    public int Connections
    {
        get
        {
            lock (_lock)
            {
                return _connections.Count;
            }
        }
    }

    /// <summary>The query strings of the searches, percent-decoded.</summary>
    public IReadOnlyList<string> Searches
    {
        get
        {
            lock (_lock)
            {
                return [.. _searches];
            }
        }
    }

    public static StandInEngine Start()
    {
        var engine = new StandInEngine();
        engine._listener.Start();
        engine.Url = new Uri($"http://127.0.0.1:{((IPEndPoint)engine._listener.LocalEndpoint).Port}");
        engine._accepting = engine.AcceptAsync();
        return engine;
    }

    /// <summary>A port on 127.0.0.1 where nothing listens.</summary>
    public static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Writes a secret file (with a trailing newline, as the Engine writes it) and returns its path.</summary>
    public string SecretFile(string secret) => _folder.File($"{Guid.NewGuid():N}/bootstrap-secret", secret + "\n");

    /// <summary>A client that logs in with the right secret.</summary>
    public HttpEngineClient Client() => new(Url, SecretFile(Secret));

    public void ExpireTokens()
    {
        lock (_lock)
        {
            _validTokens.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        Task[] connections;
        lock (_lock)
        {
            connections = [.. _connections];
        }
        await Task.WhenAll(connections);
        _stop.Dispose();
        _folder.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_lock)
                {
                    _connections.Add(ServeAsync(client));
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var reader = new RequestReader(stream);
                while (await reader.ReadAsync(_stop.Token) is { } request)
                {
                    if (!await RespondAsync(request, stream))
                    {
                        return;
                    }
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The client went away, or the server stopped.
            }
        }
    }

    /// <summary>Answers one request; false closes the connection.</summary>
    private async Task<bool> RespondAsync(Request request, NetworkStream stream)
    {
        if (NeverAnswer)
        {
            await WaitForClientToLeave(stream);
            return false;
        }
        if (request.Path == "/auth/token")
        {
            string? token = null;
            lock (_lock)
            {
                _tokenRequests++;
                if (request.Body.Contains($"\"{Secret}\"", StringComparison.Ordinal))
                {
                    token = $"header.payload{++_issued}.signature";
                    _validTokens.Add(token);
                }
            }
            await (token is null
                ? WriteAsync(stream, 401, "text/plain", "")
                : WriteAsync(stream, 200, "application/json", $$"""{"token":"{{token}}","expiresInSeconds":900}"""));
            return true;
        }
        if (request.Path != "/search")
        {
            await WriteAsync(stream, 404, "text/plain", "");
            return true;
        }
        bool authorised;
        lock (_lock)
        {
            authorised = !RefuseSearches
                && request.Headers.TryGetValue("authorization", out string? auth)
                && auth.StartsWith("Bearer ", StringComparison.Ordinal)
                && _validTokens.Contains(auth["Bearer ".Length..]);
            if (authorised)
            {
                _searches.Add(Uri.UnescapeDataString(request.Query.Replace('+', ' ')));
            }
        }
        if (!authorised)
        {
            await WriteAsync(stream, 401, "text/plain", "");
            return true;
        }
        if (SearchStatus is { } status)
        {
            await WriteAsync(stream, status, "text/plain", "");
            return true;
        }
        if (HoldAfterEarly)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n"));
            byte[] chunk = Encoding.UTF8.GetBytes($"event:early\ndata:{EarlyJson}\n\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{chunk.Length:x}\r\n").Concat(chunk).Concat("\r\n"u8.ToArray()).ToArray());
            await stream.FlushAsync();
            await WaitForClientToLeave(stream);
            ClientLeft.TrySetResult();
            return false;
        }
        await WriteAsync(stream, 200, "text/event-stream", $"event:early\ndata:{EarlyJson}\n\nevent:final\ndata:{FinalJson}\n\n");
        return true;
    }

    /// <summary>Returns once the client closes the connection (or the server stops).</summary>
    private async Task WaitForClientToLeave(NetworkStream stream)
    {
        var buffer = new byte[256];
        try
        {
            while (await stream.ReadAsync(buffer, _stop.Token) > 0)
            {
            }
        }
        catch (IOException)
        {
            // Reset by the client: it left too.
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string type, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {status} X\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head).Concat(bytes).ToArray());
    }

    private sealed record Request(string Path, string Query, Dictionary<string, string> Headers, string Body);

    /// <summary>Reads requests one after another from a kept-alive connection.</summary>
    private sealed class RequestReader(NetworkStream stream)
    {
        private byte[] _buffer = new byte[8192];
        private int _count;

        /// <summary>The next request, or null when the client closed the connection.</summary>
        public async Task<Request?> ReadAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (TryParse(out Request? request))
                {
                    return request;
                }
                if (_count == _buffer.Length)
                {
                    Array.Resize(ref _buffer, _buffer.Length * 2);
                }
                int read = await stream.ReadAsync(_buffer.AsMemory(_count), cancellationToken);
                if (read == 0)
                {
                    return null;
                }
                _count += read;
            }
        }

        private bool TryParse(out Request? request)
        {
            request = null;
            int headEnd = _buffer.AsSpan(0, _count).IndexOf("\r\n\r\n"u8);
            if (headEnd < 0)
            {
                return false;
            }
            string[] lines = Encoding.ASCII.GetString(_buffer, 0, headEnd).Split("\r\n");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    headers[line[..colon]] = line[(colon + 1)..].Trim();
                }
            }
            int length = headers.TryGetValue("content-length", out string? value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            int total = headEnd + 4 + length;
            if (_count < total)
            {
                return false;
            }
            string body = Encoding.UTF8.GetString(_buffer, headEnd + 4, length);
            string target = lines[0].Split(' ')[1];
            int question = target.IndexOf('?', StringComparison.Ordinal);
            request = new Request(
                question < 0 ? target : target[..question],
                question < 0 ? "" : target[(question + 1)..],
                headers,
                body);
            Array.Copy(_buffer, total, _buffer, 0, _count - total);
            _count -= total;
            return true;
        }
    }
}
