namespace Pounce.Core;

/// <summary>Text tidying for showing a Snippet.</summary>
public static class SnippetText
{
    /// <summary>
    /// Collapses every run of whitespace (spaces, tabs, line breaks) to one space: PDFs pad columns with runs of
    /// spaces. Nothing is trimmed. Returns the same string when there is nothing to change.
    /// </summary>
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int length = CleanLength(text);
        if (length == text.Length && !HasOtherWhitespace(text))
        {
            return text;
        }
        return string.Create(length, text, static (output, source) =>
        {
            int written = 0;
            bool inWhitespace = false;
            foreach (char c in source)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!inWhitespace)
                    {
                        output[written++] = ' ';
                    }
                    inWhitespace = true;
                }
                else
                {
                    output[written++] = c;
                    inWhitespace = false;
                }
            }
        });
    }

    private static int CleanLength(string text)
    {
        int length = 0;
        bool inWhitespace = false;
        foreach (char c in text)
        {
            bool whitespace = char.IsWhiteSpace(c);
            if (!whitespace || !inWhitespace)
            {
                length++;
            }
            inWhitespace = whitespace;
        }
        return length;
    }

    /// <summary>True when the text holds whitespace other than a plain space (a single tab still needs replacing).</summary>
    private static bool HasOtherWhitespace(string text)
    {
        foreach (char c in text)
        {
            if (c != ' ' && char.IsWhiteSpace(c))
            {
                return true;
            }
        }
        return false;
    }
}
