namespace Pounce.Core;

/// <summary>What the panel does when it has lost keyboard focus.</summary>
public enum FocusLossAction
{
    /// <summary>Nothing: the panel is hidden already, or has the focus back.</summary>
    Ignore,

    /// <summary>Hide the panel: the user went to another window.</summary>
    Hide,

    /// <summary>Take the focus back: it was lost to Pounce itself just after the panel was shown.</summary>
    Reactivate,
}

/// <summary>
/// Decides what the panel does when it loses keyboard focus. Spotlight-like: clicking elsewhere or switching windows
/// hides it. One exception: right after the panel is shown, the focus can briefly go to another window of Pounce's own
/// process (the tray icon's hidden window when its menu closes) or to no window at all, while Windows finishes
/// switching. Hiding then would make "Search" in the tray menu look broken, so the panel takes the focus back instead.
/// </summary>
public static class PanelFocusPolicy
{
    /// <summary>How long after showing the panel the exception above applies.</summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The action for a focus loss.</summary>
    /// <param name="shown">True while the panel is meant to be on screen.</param>
    /// <param name="active">True when the panel has the focus back by the time this is decided.</param>
    /// <param name="sinceShown">The time since the panel was last shown.</param>
    /// <param name="foreground">Who has the focus now.</param>
    public static FocusLossAction Decide(bool shown, bool active, TimeSpan sinceShown, ForegroundOwner foreground)
    {
        if (!shown || active)
        {
            return FocusLossAction.Ignore;
        }
        bool transient = foreground is ForegroundOwner.ThisProcess or ForegroundOwner.None;
        return transient && sinceShown < Grace ? FocusLossAction.Reactivate : FocusLossAction.Hide;
    }
}

/// <summary>Which process owns the foreground window.</summary>
public enum ForegroundOwner
{
    /// <summary>No window is in the foreground (Windows is switching, or the screen is locked).</summary>
    None,

    /// <summary>Another window of Pounce's own process.</summary>
    ThisProcess,

    /// <summary>A window of another program.</summary>
    OtherProcess,
}
