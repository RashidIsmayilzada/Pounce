namespace Pounce.Core;

/// <summary>Where a Windows path lives, as far as its text tells: see <see cref="VolumePath.Of"/>.</summary>
public enum VolumeKind
{
    /// <summary>A drive letter (<c>C:\…</c>, <c>\\?\C:\…</c>); whether it is fast takes asking Windows.</summary>
    Drive,

    /// <summary>A UNC path (<c>\\server\share\…</c>, <c>\\?\UNC\server\share\…</c>): a network share.</summary>
    Network,

    /// <summary>Anything else: a volume GUID or device path, a relative path, an empty string.</summary>
    Unknown,
}

/// <summary>The volume of a path: its kind, and for a drive letter its root (<c>C:\</c>).</summary>
/// <param name="Kind">What the path's text says about its volume.</param>
/// <param name="DriveRoot">The upper-case drive root, such as <c>D:\</c>, for <see cref="VolumeKind.Drive"/>; null otherwise.</param>
public readonly record struct PathVolume(VolumeKind Kind, string? DriveRoot);

/// <summary>
/// Which files may sit on a slow volume, for the icons. Opening a file on an unreachable server (a UNC path, a mapped
/// drive while off VPN, a redirected Documents share) blocks for the network timeout, tens of seconds; a removable or
/// optical drive may have to spin up. So such files are never touched for their icon: they get their type's icon,
/// which needs no disk access.
/// </summary>
/// <remarks>
/// <para>Text only, no OS call: both <c>\</c> and <c>/</c> separate folders, on every OS.</para>
/// <para>The cache-key rule (see <see cref="IconKey"/>): a file on a slow volume is keyed <see cref="SlowFileIconKey"/>,
/// <c>type:</c> and its extension, even when <see cref="IconKey.ForFile"/> would key it by its path (programs,
/// shortcuts, files without an extension), and its icon is always its type's generic icon. That cannot give a local
/// file a wrong icon: a local file with an individual extension or none is keyed <c>file:</c> and its path, so it never
/// reads <c>type:.exe</c> or <c>type:</c>; a local document is keyed <c>type:.pdf</c> like a slow one, and the icon
/// stored there is that type's icon whichever of them loaded it. A slow file is never keyed <c>file:</c>, so the
/// per-path entries only ever hold a file's own icon.</para>
/// </remarks>
public static class VolumePath
{
    /// <summary>
    /// The volume of <paramref name="path"/>. <c>\\server\share</c> and <c>\\?\UNC\server\share</c> are
    /// <see cref="VolumeKind.Network"/>; <c>X:</c>, <c>\\?\X:</c> and <c>\\.\X:</c> are a <see cref="VolumeKind.Drive"/>
    /// with root <c>X:\</c>; everything else is <see cref="VolumeKind.Unknown"/>.
    /// </summary>
    public static PathVolume Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string rest = path;
        bool prefixed = false;
        if (rest.Length >= 4 && IsSeparator(rest[0]) && IsSeparator(rest[1]) && (rest[2] is '?' or '.') && IsSeparator(rest[3]))
        {
            // \\?\ (no normalization) or \\.\ (device namespace): what follows says which volume.
            rest = rest[4..];
            prefixed = true;
            if (rest.Length >= 4 && rest.StartsWith("UNC", StringComparison.OrdinalIgnoreCase) && IsSeparator(rest[3]))
            {
                return new PathVolume(VolumeKind.Network, null);
            }
        }
        else if (rest.Length >= 2 && IsSeparator(rest[0]) && IsSeparator(rest[1]))
        {
            return new PathVolume(VolumeKind.Network, null);
        }
        if (rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':'
            && (!prefixed || rest.Length == 2 || IsSeparator(rest[2])))
        {
            return new PathVolume(VolumeKind.Drive, char.ToUpperInvariant(rest[0]) + @":\");
        }
        return new PathVolume(VolumeKind.Unknown, null);
    }

    /// <summary>
    /// Whether a drive of this type may be slow to reach: everything but a fixed disk and a RAM disk. Network,
    /// removable and optical drives are slow by nature; no root directory (a disconnected mapped drive, a removed
    /// stick) and unknown leave nothing to read anyway.
    /// </summary>
    public static bool MayBeSlow(DriveType type) => type is not (DriveType.Fixed or DriveType.Ram);

    /// <summary>The icon cache key of a file on a slow volume: <c>type:</c> and its extension (see the remarks).</summary>
    public static string SlowFileIconKey(string path) => "type:" + IconKey.Extension(path);

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
