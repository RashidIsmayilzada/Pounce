namespace Pounce.Core;

/// <summary>One Document returned for a Query, as the Engine sends it.</summary>
/// <param name="Path">The Document's full path; also its identity.</param>
/// <param name="Name">The file name, shown as the row's title.</param>
/// <param name="Snippet">The excerpt shown under the name.</param>
public sealed record ResultItem(string Path, string Name, Snippet Snippet)
{
    /// <summary>Identifies the Result: its path.</summary>
    public string Id => Path;
}

/// <summary>A highlighted part of a Snippet: offsets count UTF-16 code units, as Java strings do.</summary>
/// <param name="Start">The first highlighted code unit.</param>
/// <param name="End">The code unit after the last highlighted one.</param>
public readonly record struct Highlight(int Start, int End);

/// <summary>A piece of a Snippet's text, marked as highlighted or not.</summary>
/// <param name="Text">The text of this piece.</param>
/// <param name="Highlighted">True for words that matched the Query.</param>
public readonly record struct SnippetRun(string Text, bool Highlighted);

/// <summary>The excerpt shown under a Result. Highlight offsets count UTF-16 code units, as Java strings do.</summary>
/// <param name="Text">The excerpt.</param>
/// <param name="Highlights">The matched parts of <paramref name="Text"/>, in any order.</param>
public sealed record Snippet(string Text, IReadOnlyList<Highlight> Highlights)
{
    /// <summary>An excerpt with no text and no highlights.</summary>
    public static Snippet Empty { get; } = new("", []);

    /// <summary>The text split into runs, each marked as highlighted or not; safe against out-of-range offsets.</summary>
    /// <remarks>C# strings are UTF-16 too, so the offsets index <see cref="Text"/> directly. Computed on each read.</remarks>
    public IReadOnlyList<SnippetRun> Runs
    {
        get
        {
            var runs = new List<SnippetRun>(2 * Highlights.Count + 1);
            int position = 0;
            foreach (Highlight h in Highlights.OrderBy(h => h.Start))
            {
                int start = Math.Min(Math.Max(h.Start, position), Text.Length);
                int end = Math.Min(Math.Max(h.End, start), Text.Length);
                if (start > position)
                {
                    runs.Add(new SnippetRun(Text[position..start], false));
                }
                if (end > start)
                {
                    runs.Add(new SnippetRun(Text[start..end], true));
                }
                position = end;
            }
            if (position < Text.Length)
            {
                runs.Add(new SnippetRun(Text[position..], false));
            }
            return runs;
        }
    }

    /// <summary>Equal when the text and the highlights (in order) are equal.</summary>
    public bool Equals(Snippet? other) =>
        other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal) && Highlights.SequenceEqual(other.Highlights);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Text, Highlights.Count);
}

/// <summary>One step of a streamed search: Early Results first, then Final Results.</summary>
public sealed record SearchPhase
{
    private SearchPhase(bool isFinal, IReadOnlyList<ResultItem> items)
    {
        IsFinal = isFinal;
        Items = items;
    }

    /// <summary>True for the Final Results; false for the Early Results.</summary>
    public bool IsFinal { get; }

    /// <summary>The Results of this phase, best first.</summary>
    public IReadOnlyList<ResultItem> Items { get; }

    /// <summary>The quick first answer, shown until the Final Results arrive.</summary>
    public static SearchPhase Early(IReadOnlyList<ResultItem> items) => new(false, items);

    /// <summary>The complete answer; nothing follows it.</summary>
    public static SearchPhase Final(IReadOnlyList<ResultItem> items) => new(true, items);

    /// <summary>Equal when both are early or both final, with equal Results in the same order.</summary>
    public bool Equals(SearchPhase? other) => other is not null && IsFinal == other.IsFinal && Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(IsFinal, Items.Count);

    /// <summary>For test failures and logs: the phase and the Result names.</summary>
    public override string ToString() => $"{(IsFinal ? "Final" : "Early")}[{string.Join(", ", Items.Select(i => i.Name))}]";
}
