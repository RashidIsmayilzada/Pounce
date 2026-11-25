using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>Placeholder: registers nothing yet.</summary>
public sealed class GlobalHotKey : IGlobalHotKey
{
    /// <summary>Registers <paramref name="preferred"/>, or the first of its fallbacks that is free.</summary>
    public GlobalHotKey(HotKeySpec preferred) => _ = preferred;

    /// <inheritdoc/>
    public HotKeySpec? Registered => null;

    /// <inheritdoc/>
    public event EventHandler? Pressed { add { } remove { } }

    /// <inheritdoc/>
    public void Dispose() { }
}
