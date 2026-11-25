using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Pounce.Core;

namespace Pounce.Launcher.Views;

/// <summary>
/// Fills a <see cref="TextBlock"/> with a Snippet: <c>views:SnippetView.Snippet="{Binding Snippet}"</c>. The words that
/// matched the Query are bold and in the primary text colour; the rest keeps the TextBlock's own (secondary) colour.
/// Runs of whitespace (PDFs pad columns with them) are shown as one space.
/// </summary>
public static class SnippetView
{
    /// <summary>The Snippet the TextBlock shows.</summary>
    public static readonly DependencyProperty SnippetProperty = DependencyProperty.RegisterAttached(
        "Snippet", typeof(Snippet), typeof(SnippetView), new PropertyMetadata(null, OnSnippetChanged));

    /// <summary>Gets the Snippet <paramref name="text"/> shows.</summary>
    public static Snippet? GetSnippet(TextBlock text) => (Snippet?)text.GetValue(SnippetProperty);

    /// <summary>Sets the Snippet <paramref name="text"/> shows.</summary>
    public static void SetSnippet(TextBlock text, Snippet? value) => text.SetValue(SnippetProperty, value);

    private static void OnSnippetChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBlock text || Equals(e.OldValue, e.NewValue))
        {
            return;
        }
        InlineCollection inlines = text.Inlines;
        inlines.Clear();
        if (e.NewValue is not Snippet snippet)
        {
            return;
        }
        foreach (SnippetRun part in snippet.Runs)
        {
            var run = new Run(SnippetText.Clean(part.Text));
            if (part.Highlighted)
            {
                run.FontWeight = FontWeights.Bold;
                run.SetResourceReference(TextElement.ForegroundProperty, "PrimaryTextBrush");
            }
            inlines.Add(run);
        }
    }
}
