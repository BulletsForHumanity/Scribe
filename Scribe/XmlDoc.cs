namespace Scribe;

/// <summary>
///     Shared XML documentation extraction and normalisation used by generators that
///     need to read and re-emit <c>&lt;summary&gt;</c>, <c>&lt;param&gt;</c>, and
///     <c>&lt;see cref&gt;</c> content from Roslyn's raw doc-comment strings.
/// </summary>
public static class XmlDoc
{
    /// <summary>
    ///     Extracts the plain text content of the <c>&lt;summary&gt;</c> element
    ///     from a raw Roslyn XML doc string. Returns an empty string when absent.
    /// </summary>
    public static string ExtractSummary(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return "";
        }

        var start = rawXml.IndexOf("<summary>", StringComparison.Ordinal);
        var end = rawXml.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
        {
            return "";
        }

        var inner = rawXml.Substring(start + "<summary>".Length, end - start - "<summary>".Length);
        return NormalizeDocText(inner);
    }

    /// <summary>
    ///     Extracts all <c>&lt;param name="..."&gt;</c> entries from a raw XML doc string,
    ///     returning a dictionary keyed by parameter name.
    /// </summary>
    public static Dictionary<string, string> ExtractParams(string rawXml)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return result;
        }

        var search = "<param name=\"";
        var pos = 0;
        while (true)
        {
            var nameStart = rawXml.IndexOf(search, pos, StringComparison.Ordinal);
            if (nameStart < 0)
            {
                break;
            }

            var nameValueStart = nameStart + search.Length;
            var nameEnd = rawXml.IndexOf('"', nameValueStart);
            if (nameEnd < 0)
            {
                break;
            }

            var paramName = rawXml.Substring(nameValueStart, nameEnd - nameValueStart);

            var contentStart = rawXml.IndexOf('>', nameEnd);
            if (contentStart < 0)
            {
                break;
            }

            contentStart++; // skip '>'
            var contentEnd = rawXml.IndexOf("</param>", contentStart, StringComparison.Ordinal);
            if (contentEnd < 0)
            {
                break;
            }

            var content = rawXml.Substring(contentStart, contentEnd - contentStart);
            result[paramName] = NormalizeDocText(content);
            pos = contentEnd + "</param>".Length;
        }

        return result;
    }

    /// <summary>
    ///     Trims leading/trailing whitespace and collapses interior line-level
    ///     indentation from Roslyn's raw XML doc string.
    ///     Rewrites <c>cref</c> attribute values in <c>&lt;see&gt;</c> tags by stripping
    ///     the Roslyn member-kind prefix (e.g. <c>F:Foo.Bar</c> → <c>Foo.Bar</c>) so
    ///     the tag remains navigable in the generated file.
    /// </summary>
    public static string NormalizeDocText(string raw)
    {
        raw = RewriteSeeCrefValues(raw);

        var lines = raw.Split('\n');
        var trimmed = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length > 0)
            {
                trimmed.Add(t);
            }
        }

        return string.Join(" ", trimmed).Trim();
    }

    /// <summary>
    ///     Scans every <c>cref="..."</c> attribute in the text and strips the leading
    ///     Roslyn member-kind prefix (T:, F:, P:, M:, E:, N:) so the value becomes a
    ///     plain C# fully-qualified name that Roslyn can resolve in the generated file.
    ///     The <c>&lt;see&gt;</c> tag itself is left intact.
    /// </summary>
    public static string RewriteSeeCrefValues(string text)
    {
        const string crefKey = "cref=\"";
        var result = "";
        var pos = 0;
        while (true)
        {
            var ci = text.IndexOf(crefKey, pos, StringComparison.Ordinal);
            if (ci < 0)
            {
                result += text.Substring(pos);
                break;
            }

            // Copy everything up to and including 'cref="'
            result += text.Substring(pos, ci - pos + crefKey.Length);
            var valStart = ci + crefKey.Length;
            var valEnd = text.IndexOf('"', valStart);
            if (valEnd < 0)
            {
                result += text.Substring(valStart);
                break;
            }

            var crefValue = text.Substring(valStart, valEnd - valStart);
            // Strip member-kind prefix (T:, F:, P:, M:, E:, N:)
            if (crefValue.Length > 2 && crefValue[1] == ':')
            {
                crefValue = crefValue.Substring(2);
            }

            result += crefValue;
            pos = valEnd; // closing '"' picked up in next iteration
        }

        return result;
    }

    /// <summary>
    ///     XML-escapes characters that are meaningful in XML attribute values and text content.
    /// </summary>
    public static string XmlEscape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    ///     Strips the <c>global::</c> prefix from a fully-qualified name, if present.
    /// </summary>
    public static string StripGlobalPrefix(string s) =>
        s.StartsWith("global::", StringComparison.Ordinal) ? s.Substring(8) : s;

    /// <summary>
    ///     Formats an <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/> as a fully-qualified
    ///     name, optionally stripping the <c>global::</c> prefix.
    /// </summary>
    public static string FormatFqn(Microsoft.CodeAnalysis.INamedTypeSymbol type, bool stripGlobal)
    {
        var fqn = type.ToDisplayString(
            Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat
        );
        return stripGlobal ? StripGlobalPrefix(fqn) : fqn;
    }
}
