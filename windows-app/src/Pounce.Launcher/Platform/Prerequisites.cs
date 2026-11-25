using System.IO;

namespace Pounce.Launcher.Platform;

/// <summary>
/// What the bundled Engine needs from Windows itself. ONNX Runtime, which computes the embeddings, loads
/// <c>MSVCP140_1.dll</c> from the Microsoft Visual C++ 2015–2022 Redistributable (x64). The bundled Java runtime
/// carries <c>msvcp140.dll</c>, <c>vcruntime140.dll</c> and <c>vcruntime140_1.dll</c> but not that one, so without the
/// Redistributable the Engine starts and then fails to load the model.
/// </summary>
public static class Prerequisites
{
    /// <summary>
    /// True when <c>%SystemRoot%\System32\msvcp140_1.dll</c> is missing. Pounce is a 64-bit process, so
    /// <c>System32</c> is the real 64-bit folder (no WOW64 redirection). Cheap: one file check.
    /// </summary>
    public static bool RedistributableMissing
    {
        get
        {
            try
            {
                return !File.Exists(Path.Combine(Environment.SystemDirectory, "msvcp140_1.dll"));
            }
            catch (Exception e)
            {
                Log.Warn("Could not check for the Visual C++ runtime", e);
                return false;
            }
        }
    }
}
