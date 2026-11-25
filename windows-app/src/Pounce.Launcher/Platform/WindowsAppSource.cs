using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The applications Start lists under "All apps": desktop apps and Store apps alike, read from the shell's Apps folder
/// (<c>shell:AppsFolder</c>, FOLDERID_AppsFolder). Each app's path is <c>shell:AppsFolder\</c> and its parsing name,
/// which the shell launches directly. When the Apps folder cannot be read, or lists nothing, the Start Menu shortcuts
/// are used instead and the reason is logged.
/// </summary>
/// <remarks>
/// <see cref="ListApps"/> is called on a thread-pool thread; the COM work runs on the shared <see cref="StaThread"/>
/// and the caller waits for it, at most <see cref="Timeout"/>.
/// </remarks>
public sealed class WindowsAppSource : IAppSource
{
    /// <summary>How long to wait for the shell thread, which may be busy with an icon, before using the shortcuts.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>A guard against an enumerator that never ends.</summary>
    private const int MaxItems = 20_000;

    private static int firstListingLogged;

    private readonly Lock gate = new();
    private Task<Listing>? reading;

    /// <inheritdoc/>
    public IReadOnlyList<AppEntry> ListApps()
    {
        string problem;
        try
        {
            Task<Listing> task = Read();
            if (!task.Wait(Timeout))
            {
                problem = $"no answer from the shell thread within {Timeout.TotalSeconds:0} s";
            }
            else if (task.Result.Problem is { } failed)
            {
                problem = failed;
            }
            else
            {
                IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps(task.Result.Items);
                if (apps.Count > 0)
                {
                    if (Interlocked.Exchange(ref firstListingLogged, 1) == 0)
                    {
                        Log.Info($"Apps folder: {task.Result.Items.Count} items, {apps.Count} apps, in {task.Result.Elapsed.TotalMilliseconds:0} ms");
                    }
                    return apps;
                }
                problem = $"no apps among its {task.Result.Items.Count} items";
            }
        }
        catch (Exception e)
        {
            problem = "listing failed: " + e.GetBaseException().GetType().Name + " " + ShellNative.Hex(e.GetBaseException().HResult);
        }
        ShellLog.WarnOnce("apps: " + problem, $"Apps folder: {problem}; using the Start Menu shortcuts instead");
        return StartMenuAppSource.ForCurrentUser().ListApps();
    }

    /// <summary>Starts reading the Apps folder on the shell thread, unless a read is still on its way.</summary>
    private Task<Listing> Read()
    {
        lock (gate)
        {
            if (reading is null || reading.IsCompleted)
            {
                // Normal comes before the Background priority of icon loads: the app list jumps the icon queue.
                reading = StaThread.Shared.InvokeAsync(ReadAppsFolder, DispatcherPriority.Normal);
            }
            return reading;
        }
    }

    /// <summary>The Apps folder's items as (display name, parsing name). Runs on the STA thread; never throws.</summary>
    private static Listing ReadAppsFolder()
    {
        var clock = Stopwatch.StartNew();
        IShellItem? folder = null;
        IEnumShellItems? children = null;
        try
        {
            Guid folderId = ShellNative.FolderIdAppsFolder;
            Guid shellItemId = ShellNative.IidIShellItem;
            int hr = ShellNative.SHGetKnownFolderItem(ref folderId, ShellNative.KfFlagDefault, IntPtr.Zero, ref shellItemId, out folder);
            if (hr < 0 || folder is null)
            {
                ShellNative.Release(folder);
                folder = null;
                int hr2 = ShellNative.SHCreateShellItem("shell:AppsFolder", IntPtr.Zero, ref shellItemId, out folder);
                if (hr2 < 0 || folder is null)
                {
                    return Listing.Failed($"cannot open it (SHGetKnownFolderItem {ShellNative.Hex(hr)}, SHCreateItemFromParsingName {ShellNative.Hex(hr2)})");
                }
            }

            Guid enumItems = ShellNative.BhidEnumItems;
            Guid enumId = ShellNative.IidIEnumShellItems;
            hr = folder.BindToHandler(IntPtr.Zero, ref enumItems, ref enumId, out children);
            if (hr < 0 || children is null)
            {
                return Listing.Failed($"cannot enumerate it (BindToHandler {ShellNative.Hex(hr)})");
            }

            var items = new List<(string Name, string ParsingName)>();
            while (items.Count < MaxItems)
            {
                IShellItem? child = null;
                try
                {
                    hr = children.Next(1, out child, out uint fetched);
                    if (hr != ShellNative.SOk || fetched != 1 || child is null)
                    {
                        break;
                    }
                    string? name = DisplayName(child, Sigdn.NormalDisplay);
                    string? parsingName = DisplayName(child, Sigdn.ParentRelativeParsing);
                    if (name is not null && parsingName is not null)
                    {
                        items.Add((name, parsingName));
                    }
                }
                finally
                {
                    ShellNative.Release(child);
                }
            }
            if (hr < 0)
            {
                ShellLog.WarnOnce("apps: Next " + ShellNative.Hex(hr),
                    $"Apps folder: enumeration stopped after {items.Count} items (IEnumShellItems.Next {ShellNative.Hex(hr)})");
            }
            return new Listing(items, null, clock.Elapsed);
        }
        catch (Exception e)
        {
            return Listing.Failed($"enumeration failed: {e.GetType().Name} {ShellNative.Hex(e.HResult)} {e.Message}");
        }
        finally
        {
            ShellNative.Release(children);
            ShellNative.Release(folder);
        }
    }

    /// <summary>One of the item's names; null when the shell has none. Frees the shell's string.</summary>
    private static string? DisplayName(IShellItem item, Sigdn kind)
    {
        IntPtr text = IntPtr.Zero;
        try
        {
            int hr = item.GetDisplayName(kind, out text);
            return hr >= 0 && text != IntPtr.Zero ? Marshal.PtrToStringUni(text) : null;
        }
        finally
        {
            if (text != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(text);
            }
        }
    }

    /// <summary>What reading the Apps folder gave: its items, or why there are none.</summary>
    private sealed record Listing(IReadOnlyList<(string Name, string ParsingName)> Items, string? Problem, TimeSpan Elapsed)
    {
        public static Listing Failed(string problem) => new([], problem, TimeSpan.Zero);
    }
}
