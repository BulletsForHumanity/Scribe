namespace Scribe;

/// <summary>Shared naming utilities for source generation.</summary>
public static class Naming
{
    /// <summary>Converts PascalCase to kebab-case (e.g. ManifestIndex → manifest-index).</summary>
    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>Splits PascalCase into Title Words (e.g. ManifestDailyLog → "Manifest Daily Log").</summary>
    public static string ToTitleWords(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    /// <summary>Converts kebab-case to PascalCase (e.g. daily-log → DailyLog).</summary>
    public static string ToPascalCase(string kebab)
    {
        if (string.IsNullOrEmpty(kebab))
            return kebab;
        var sb = new System.Text.StringBuilder(kebab.Length);
        var capitalizeNext = true;
        foreach (var ch in kebab)
        {
            if (ch == '-')
            {
                capitalizeNext = true;
                continue;
            }
            sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
            capitalizeNext = false;
        }
        return sb.ToString();
    }

    /// <summary>
    ///     Extracts the trimmed text content from the <c>&lt;summary&gt;</c> element
    ///     of a Roslyn <c>GetDocumentationCommentXml()</c> return value.
    ///     Returns an empty string if no summary is found.
    /// </summary>
    public static string ExtractXmlSummary(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return "";
        }

        var start = xml!.IndexOf("<summary>", StringComparison.Ordinal);
        var end = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
        {
            return "";
        }

        var raw = xml.Substring(start + 9, end - start - 9); // 9 = "<summary>".Length
        // Collapse whitespace — XML doc comments often contain indented newlines.
        return raw.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("    ", " ").Trim();
    }

    /// <summary>
    ///     Escapes a string for safe embedding inside a regular (non-verbatim) C# string literal.
    ///     Handles backslashes, double-quotes, and newlines.
    /// </summary>
    public static string EscapeStringLiteral(string s)
    {
        return s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");
    }
}
