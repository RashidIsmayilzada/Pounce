using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>Placeholder: starts nothing yet.</summary>
public sealed class EngineProcessStarter : IProcessStarter
{
    /// <summary>The Engine's output goes to <paramref name="logFile"/>.</summary>
    public EngineProcessStarter(string logFile) => _ = logFile;

    /// <inheritdoc/>
    public IEngineProcess Start(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, Action<int> onExit) =>
        throw new NotSupportedException("The Engine process starter is not written yet.");
}
