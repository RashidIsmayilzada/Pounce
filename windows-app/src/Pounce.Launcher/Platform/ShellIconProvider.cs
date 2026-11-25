using System.Windows.Media;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>Placeholder: no icons yet.</summary>
public sealed class ShellIconProvider : IIconProvider
{
    /// <inheritdoc/>
    public ImageSource? Cached(SearchItem item) => null;

    /// <inheritdoc/>
    public Task<ImageSource?> LoadAsync(SearchItem item) => Task.FromResult<ImageSource?>(null);
}
