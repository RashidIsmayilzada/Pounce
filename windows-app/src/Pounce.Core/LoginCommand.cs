namespace Pounce.Core;

/// <summary>
/// The sign-in entry of Pounce: the value <see cref="ValueName"/> under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, whose data is the command Windows runs at sign-in.
/// </summary>
public static class LoginCommand
{
    /// <summary>The name of the value under the <c>Run</c> key.</summary>
    public const string ValueName = "Pounce";

    /// <summary>The command for <paramref name="executable"/>: its full path in double quotes, since it may hold spaces.</summary>
    public static string For(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return "\"" + executable + "\"";
    }

    /// <summary>
    /// True when <paramref name="command"/> starts <paramref name="executable"/>: the same path, ignoring case, the
    /// quotes around it and any arguments after it. A Pounce.exe that was moved, or a second copy, is a different one.
    /// </summary>
    /// <param name="command">The data of the <c>Run</c> value; null when there is none.</param>
    /// <param name="executable">The full path of this <c>Pounce.exe</c>.</param>
    public static bool PointsAt(string? command, string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        string? program = Program(command);
        return program is not null
            && string.Equals(program, executable.Trim().Trim('"').Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The program a <c>Run</c> command starts: the text inside the leading double quotes, or the whole command when
    /// it is not quoted (an unquoted path may hold spaces, so it is not cut at the first one). Null when blank.
    /// </summary>
    internal static string? Program(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }
        string text = command.Trim();
        if (text[0] != '"')
        {
            return text;
        }
        int end = text.IndexOf('"', 1);
        string program = (end < 0 ? text[1..] : text[1..end]).Trim();
        return program.Length == 0 ? null : program;
    }

    /// <summary>
    /// True when the user switched the entry off in Task Manager or Settings > Apps > Startup. Windows records that
    /// as a binary value of the same name under
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run</c>; its first byte is even
    /// (2, 6) for on and odd (3, 7) for off. Undocumented, so anything unexpected counts as on.
    /// </summary>
    /// <param name="startupApproved">The value's data; null when there is no value (on).</param>
    public static bool IsSwitchedOff(byte[]? startupApproved) =>
        startupApproved is { Length: > 0 } && (startupApproved[0] & 1) == 1;
}
