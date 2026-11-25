using System.IO;
using Microsoft.Win32;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// "Launch at login" as the value <c>Pounce</c> under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>,
/// holding the quoted path of this <c>Pounce.exe</c>: per user, no administrator rights, and listed (and switchable)
/// in Task Manager's Startup apps.
/// </summary>
public sealed class LoginItem : ILoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where Task Manager and Settings record that the user switched a Run entry off (undocumented).</summary>
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string executable;

    /// <summary>The sign-in entry for the running <c>Pounce.exe</c>.</summary>
    public LoginItem()
    {
        executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pounce.exe");
    }

    /// <summary>
    /// True when the Run entry starts this very <c>Pounce.exe</c> (a moved or second copy shows unchecked, and
    /// checking it again repairs the entry) and the user has not switched it off in Task Manager. False when the
    /// registry cannot be read.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (!LoginCommand.PointsAt(run?.GetValue(LoginCommand.ValueName) as string, executable))
                {
                    return false;
                }
                using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
                return !LoginCommand.IsSwitchedOff(approved?.GetValue(LoginCommand.ValueName) as byte[]);
            }
            catch (Exception e)
            {
                Log.Warn("Could not read the sign-in entry", e);
                return false;
            }
        }
    }

    /// <summary>
    /// Writes or removes the Run entry. Enabling also clears a "switched off" mark left by Task Manager, since the
    /// user has just asked for Pounce at sign-in. Throws when the registry cannot be written; the caller shows that.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            if (enabled)
            {
                run.SetValue(LoginCommand.ValueName, LoginCommand.For(executable), RegistryValueKind.String);
            }
            else
            {
                run.DeleteValue(LoginCommand.ValueName, throwOnMissingValue: false);
            }
        }
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            approved?.DeleteValue(LoginCommand.ValueName, throwOnMissingValue: false);
        }
        catch (Exception e)
        {
            Log.Warn("Could not clear the Startup apps mark", e);
        }
        Log.Info(enabled ? $"Launch at login on: {LoginCommand.For(executable)}" : "Launch at login off");
    }
}
