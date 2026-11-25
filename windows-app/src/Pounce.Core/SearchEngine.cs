namespace Pounce.Core;

/// <summary>Anything that can answer a Query as a stream of Early then Final Results.</summary>
public interface ISearchEngine
{
    /// <summary>
    /// Searches for <paramref name="query"/>: yields the Early Results, then the Final Results, then ends.
    /// Cancelling <paramref name="cancellationToken"/> (or leaving the loop) stops the search at once.
    /// Fails with an <see cref="EngineException"/> for problems the user can fix.
    /// </summary>
    IAsyncEnumerable<SearchPhase> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
}

/// <summary>The kinds of <see cref="EngineException"/>.</summary>
public enum EngineProblem
{
    /// <summary>Nothing answers at the Engine's address.</summary>
    NotRunning,

    /// <summary>The bootstrap secret file can't be read.</summary>
    SecretMissing,

    /// <summary>The Engine refused the bootstrap secret.</summary>
    SecretRefused,

    /// <summary>The Engine answered with an HTTP status the Launcher doesn't expect.</summary>
    Unexpected,
}

/// <summary>Problems the user can fix, with a message that says how.</summary>
public sealed class EngineException : Exception
{
    private EngineException(EngineProblem problem, string message, Exception? inner) : base(message, inner)
    {
        Problem = problem;
    }

    /// <summary>Which problem this is.</summary>
    public EngineProblem Problem { get; }

    /// <summary>The Engine's address, for <see cref="EngineProblem.NotRunning"/>.</summary>
    public Uri? EngineUrl { get; private init; }

    /// <summary>The bootstrap secret file, for <see cref="EngineProblem.SecretMissing"/> and <see cref="EngineProblem.SecretRefused"/>.</summary>
    public string? SecretFile { get; private init; }

    /// <summary>The HTTP status, for <see cref="EngineProblem.Unexpected"/>.</summary>
    public int? Status { get; private init; }

    /// <summary>"Pounce isn't running at …. Start Pounce and try again."</summary>
    public static EngineException NotRunning(Uri url, Exception? inner = null) =>
        new(EngineProblem.NotRunning, $"Pounce isn't running at {url.OriginalString}. Start Pounce and try again.", inner) { EngineUrl = url };

    /// <summary>"Can't read …. Start Pounce once to create it."</summary>
    public static EngineException SecretMissing(string path, Exception? inner = null) =>
        new(EngineProblem.SecretMissing, $"Can't read {path}. Start Pounce once to create it.", inner) { SecretFile = path };

    /// <summary>"Pounce refused the secret in …. Is it from another Pounce install?"</summary>
    public static EngineException SecretRefused(string path) =>
        new(EngineProblem.SecretRefused, $"Pounce refused the secret in {path}. Is it from another Pounce install?", null) { SecretFile = path };

    /// <summary>"Pounce answered with HTTP …."</summary>
    public static EngineException Unexpected(int status) =>
        new(EngineProblem.Unexpected, $"Pounce answered with HTTP {status}.", null) { Status = status };
}
