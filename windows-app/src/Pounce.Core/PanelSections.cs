namespace Pounce.Core;

/// <summary>The section titles between the rows of the panel, as in Spotlight.</summary>
public static class PanelSections
{
    /// <summary>The title above the application rows.</summary>
    public const string Applications = "Applications";

    /// <summary>The title above the file rows; only shown when application rows come before them.</summary>
    public const string Files = "Files";

    /// <summary>
    /// The title shown above row <paramref name="index"/> of <see cref="SearchModel.Items"/>, or null for none:
    /// "Applications" above the first app, "Files" above the first file when there are apps before it.
    /// </summary>
    /// <param name="index">The row.</param>
    /// <param name="appCount">How many of the rows, at the start, are applications.</param>
    public static string? TitleAbove(int index, int appCount)
    {
        if (appCount <= 0)
        {
            return null;
        }
        if (index == 0)
        {
            return Applications;
        }
        return index == appCount ? Files : null;
    }
}
