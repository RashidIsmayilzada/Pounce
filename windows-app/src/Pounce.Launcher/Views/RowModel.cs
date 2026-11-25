using System.ComponentModel;
using Pounce.Core;

namespace Pounce.Launcher.Views;

/// <summary>
/// One row of the panel as the list shows it. Rows are kept and updated in place between searches, so when the
/// Final Results replace the Early Results only the text that differs changes: no row is rebuilt and nothing flashes.
/// Each property announces a change only when its value really changed.
/// </summary>
public sealed class RowModel : INotifyPropertyChanged
{
    private SearchItem? item;
    private string name = "";
    private string location = "";
    private Snippet snippet = Snippet.Empty;
    private string? title;
    private bool isApp;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The application or Result shown; the icon follows it.</summary>
    public SearchItem? Item => item;

    /// <summary>The app's or the file's name.</summary>
    public string Name => name;

    /// <summary>For a file, where it is: the path, relative to the user's profile folder when inside it. Empty for an app.</summary>
    public string Location => location;

    /// <summary>For a file, the matching passage; empty for an app.</summary>
    public Snippet Snippet => snippet;

    /// <summary>True when there is a passage to show.</summary>
    public bool HasSnippet => snippet.Text.Length > 0;

    /// <summary>The section title shown above this row ("Applications", "Files"), or null.</summary>
    public string? Title => title;

    /// <summary>True when there is a section title above this row.</summary>
    public bool HasTitle => title is not null;

    /// <summary>True for an application row, false for a file row.</summary>
    public bool IsApp => isApp;

    /// <summary>What a screen reader says for the row.</summary>
    public string AccessibleName => isApp ? $"{name}, application" : $"{name}, {location}";

    /// <summary>Shows <paramref name="newItem"/> in this row.</summary>
    /// <param name="newItem">The row's application or Result.</param>
    /// <param name="newTitle">The section title above the row, or null.</param>
    /// <param name="userProfile">The user's profile folder, to shorten paths inside it.</param>
    public void Show(SearchItem newItem, string? newTitle, string? userProfile)
    {
        if (Equals(item, newItem) && title == newTitle)
        {
            return;
        }
        Set(ref item, newItem, nameof(Item));
        Set(ref title, newTitle, nameof(Title), nameof(HasTitle));
        Set(ref isApp, newItem is AppItem, nameof(IsApp));
        Set(ref name, newItem.Name, nameof(Name));
        string newLocation = newItem is FileItem ? PathDisplay.Abbreviate(newItem.Path, userProfile) : "";
        Set(ref location, newLocation, nameof(Location));
        Snippet newSnippet = newItem is FileItem file ? file.Result.Snippet : Snippet.Empty;
        Set(ref snippet, newSnippet, nameof(Snippet), nameof(HasSnippet));
        Raise(nameof(AccessibleName));
    }

    /// <summary>For screen readers that fall back to the item's text.</summary>
    public override string ToString() => AccessibleName;

    private void Set<T>(ref T field, T value, params ReadOnlySpan<string> properties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        foreach (string property in properties)
        {
            Raise(property);
        }
    }

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
