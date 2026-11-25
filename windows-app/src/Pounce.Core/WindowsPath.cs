namespace Pounce.Core;

/// <summary>
/// Builds Windows paths on any OS. <see cref="Path.Combine(string, string)"/> would use <c>/</c> when the tests run on
/// Linux; these paths are for Windows, so they always use <c>\</c> and the tests check the real strings.
/// </summary>
internal static class WindowsPath
{
    /// <summary><paramref name="folder"/> followed by <paramref name="parts"/>, separated by <c>\</c>.</summary>
    public static string Join(string folder, params string[] parts) =>
        folder.TrimEnd('\\', '/') + "\\" + string.Join('\\', parts);
}
