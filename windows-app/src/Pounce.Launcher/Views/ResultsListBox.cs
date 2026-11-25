using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Pounce.Launcher.Views;

/// <summary>
/// The list of result rows. A plain <see cref="ListBox"/>, except that screen readers do not see the hidden pooled
/// rows (<see cref="RowModel.IsVisible"/>): WPF lists an automation peer for every item, whatever its container's
/// visibility, so without this Narrator could walk onto rows of an earlier search.
/// </summary>
public sealed class ResultsListBox : ListBox
{
    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new ResultsListBoxAutomationPeer(this);

    private sealed class ResultsListBoxAutomationPeer(ListBox owner) : ListBoxAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore()
        {
            List<AutomationPeer> children = base.GetChildrenCore();
            children?.RemoveAll(peer => peer is ItemAutomationPeer { Item: RowModel { IsVisible: false } });
            return children!;
        }
    }
}
