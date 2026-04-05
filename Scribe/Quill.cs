using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scribe;

/// <summary>
///     Fluent builder for generating well-formed C# source files.
///     Manages using-directive collection (deduplicated, sorted), file-scoped namespaces,
///     automatic indentation for block scoping, and XML doc generation.
/// </summary>
/// <remarks>
///     <para>
///         Members are raw strings — this is NOT a full AST.
///         The builder manages file structure and indentation; the caller provides content.
///     </para>
///     <para>
///         <b>When to use:</b> generators with loops, conditionals, or multi-section output.
///     </para>
///     <para>
///         <b>When NOT to use:</b> simple generators where a single <c>$$"""..."""</c> raw string literal
///         is already maximally readable.
///     </para>
///     <para>
///         XML documentation (<c>Summary</c>, <c>Remarks</c>, <c>Param</c>, etc.) is added via
///         post-decoration on <see cref="BlockScope"/>:
///         <code>
///         using (b.Block("public static class Foo").Summary("Registers all converters."))
///         {
///             b.Line("// members here");
///         }
///         </code>
///     </para>
/// </remarks>
/// <remarks>
///     The parameterless constructor automatically discovers any
///     <see cref="ScribeHeaderAttribute"/> on the calling assembly via reflection.
///     <c>[MethodImpl(NoInlining)]</c> ensures <c>GetCallingAssembly()</c> returns the
///     correct (caller's) assembly rather than being inlined away.
/// </remarks>
[method: MethodImpl(MethodImplOptions.NoInlining)]
public sealed partial class Quill()
{
    internal readonly StringBuilder _body = new();
    private readonly HashSet<string> _usings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal); // alias → "Ns.Type"
    private readonly Dictionary<string, string> _aliasLookup = new(StringComparer.Ordinal); // "Ns.Type" → alias
    private int _indent;
    private string? _namespace;

    private string? _header = Assembly.GetCallingAssembly()
        .GetCustomAttribute<ScribeHeaderAttribute>()
        ?.Header;

    private bool _built;

    /// <summary>Override the auto-discovered header with an explicit value (or <c>null</c> to clear).</summary>
    public Quill Header(string? header)
    {
        _header = header;
        return this;
    }

    /// <summary>
    ///     Deterministic newline constant for generated source.
    ///     Hardcoded to <c>\n</c> (LF) so that generators produce identical output on every platform.
    ///     The C# compiler accepts both <c>\n</c> and <c>\r\n</c>, so LF-only output is safe everywhere.
    ///     <para>
    ///         <b>Why not <c>Environment.NewLine</c>?</b>
    ///         <list type="number">
    ///             <item>RS1035 bans <c>System.Environment</c> in analyzer/generator assemblies.</item>
    ///             <item><c>Environment.NewLine</c> is platform-dependent, producing non-deterministic builds.</item>
    ///             <item><c>StringBuilder.AppendLine()</c> silently uses <c>Environment.NewLine</c> too — avoid it.</item>
    ///         </list>
    ///         Use <c>sb.Append(line).Append(Quill.NewLine)</c> instead of <c>sb.AppendLine(line)</c>.
    ///     </para>
    /// </summary>
    public const char NewLine = '\n';
    /// <summary>
    ///     Deterministic newline constant for generated source.
    ///     Hardcoded to <c>\n</c> (LF) so that generators produce identical output on every platform.
    ///     The C# compiler accepts both <c>\n</c> and <c>\r\n</c>, so LF-only output is safe everywhere.
    ///     <para>
    ///         <b>Why not <c>Environment.NewLine</c>?</b>
    ///         <list type="number">
    ///             <item>RS1035 bans <c>System.Environment</c> in analyzer/generator assemblies.</item>
    ///             <item><c>Environment.NewLine</c> is platform-dependent, producing non-deterministic builds.</item>
    ///             <item><c>StringBuilder.AppendLine()</c> silently uses <c>Environment.NewLine</c> too — avoid it.</item>
    ///         </list>
    ///         Use <c>sb.Append(line).Append(Quill.NewLine)</c> instead of <c>sb.AppendLine(line)</c>.
    ///     </para>
    /// </summary>
    public const string NewLineString = "\n";

    // ── Namespace ────────────────────────────────────────────────────────

    /// <summary>Set the file-scoped namespace (emitted as <c>namespace X;</c>).</summary>
    public Quill FileNamespace(string ns)
    {
        _namespace = ns;
        return this;
    }

    // ── Content ──────────────────────────────────────────────────────────

    /// <summary>Append an empty line or a single line at the current indentation level.</summary>
    public Quill Line(string text = "")
    {
        if (text.Length is 0)
            _body.Append(NewLine);
        else
            _body.Append(' ', _indent * 4).Append(text).Append(NewLine);

        return this;
    }

    /// <summary>Append multiple individual lines, each at the current indentation level.</summary>
    public Quill Lines(params string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.Length is 0)
                _body.Append(NewLine);
            else
                _body.Append(' ', line.Length is 0 ? 0 : _indent * 4).Append(line).Append(NewLine);
        }

        return this;
    }

    /// <summary>
    ///     Append a multi-line string (typically a raw string literal), stripping
    ///     common leading whitespace and applying the builder's current indentation.
    ///     Leading/trailing blank lines from the raw string are trimmed.
    ///     Chain <see cref="ContentResult.Padded"/> to wrap in blank lines.
    /// </summary>
    /// <example>
    ///     <code>
    ///     b.Lines("""
    ///         var type = context.JsonTypeInfo?.Type;
    ///         if (type is null) return;
    ///         """).Padded();
    ///     </code>
    /// </example>
    public ContentResult Lines(string multiLineText)
    {
        var startPos = _body.Length;
        EmitDedentedLines(multiLineText);
        return new ContentResult(this, startPos);
    }

    /// <summary>
    ///     Emit one line per item in <paramref name="items"/> using <paramref name="selector"/>
    ///     to produce each line. Lines are emitted at the current indentation level.
    ///     Chain <see cref="ContentResult.Padded"/> to wrap in blank lines.
    /// </summary>
    /// <example>
    ///     <code>
    ///     b.LinesFor(smartEnums, se => $"options.Converters.Add(new {se.TypeName}JsonConverter());")
    ///      .Padded();
    ///     </code>
    /// </example>
    public ContentResult LinesFor<T>(IEnumerable<T> items, Func<T, string> selector)
    {
        var startPos = _body.Length;
        foreach (var item in items)
        {
            var text = selector(item);
            if (text.Length is 0)
                _body.Append(NewLine);
            else
                _body.Append(' ', _indent * 4).Append(text).Append(NewLine);
        }

        return new ContentResult(this, startPos);
    }

    /// <summary>
    ///     Append a pre-formatted string verbatim (no automatic indentation).
    ///     Use for content that already has correct indentation (e.g. template output).
    /// </summary>
    public Quill AppendRaw(string text)
    {
        _body.Append(text);
        return this;
    }

    /// <summary>
    ///     Emit a <c>// comment</c> at the current indentation level.
    ///     Ensures blank-line separation from previous content (unless at start of a block).
    /// </summary>
    public Quill Comment(string text)
    {
        EnsureBlankLineSeparation();
        _body.Append(' ', _indent * 4).Append("// ").Append(text).Append(NewLine);
        return this;
    }

    // ── Inscribe ────────────────────────────────────────────────────────

    /// <summary>
    ///     Produces the final source text. Prepends the <c>// &lt;auto-generated/&gt;</c> header,
    ///     sorted usings, and file-scoped namespace before the body content.
    ///     <para>
    ///         As a post-processing step, all <c>global::</c> type references in the body are resolved:
    ///         non-conflicting types get a <c>using</c> directive and are shortened to their simple name;
    ///         conflicting types (same short name, different namespace) receive disambiguated
    ///         <c>using</c> aliases built from their namespace segments.
    ///     </para>
    ///     <para>
    ///         If a <see cref="ScribeHeaderAttribute"/> was discovered (or set via <see cref="Header"/>),
    ///         a decorative page header is rendered with the header text, Scribe attribution,
    ///         and a quill illustration. Otherwise a minimal attribution is used.
    ///         The file always ends with <c>// I HAVE SPOKEN</c>.
    ///     </para>
    ///     Can only be called once.
    /// </summary>
    public string Inscribe()
    {
        if (_built)
            throw new InvalidOperationException("Quill.Inscribe() has already been called.");

        _built = true;

        // Add alias using directives before resolving globals (so they're part of the sorted output).
        foreach (var kvp in _aliases)
            _usings.Add($"{kvp.Key} = {kvp.Value}");

        ResolveGlobalReferences();

        var result = new StringBuilder();
        result.Append("// <auto-generated/>").Append(NewLine);

        if (_header is { Length: > 0 })
            AppendPageHeader(result);
        else
            AppendDefaultHeader(result);

        result.Append("#nullable enable").Append(NewLine);
        result.Append(NewLine);

        if (_usings.Count > 0)
        {
            foreach (var u in _usings.OrderBy(static x => x, StringComparer.Ordinal))
                result.Append("using ").Append(u).Append(';').Append(NewLine);

            result.Append(NewLine);
        }

        if (_namespace is { Length: > 0 })
        {
            result.Append("namespace ").Append(_namespace).Append(';').Append(NewLine);
            result.Append(NewLine);
        }

        TrimTrailingBlankLines();
        result.Append(_body);

        result.Append(NewLine);
        result.Append("// I HAVE SPOKEN").Append(NewLine);
        return result.ToString();
    }

    // ── Header rendering ────────────────────────────────────────────────

    private static void AppendDefaultHeader(StringBuilder sb)
    {
        sb.Append("// I,").Append(NewLine);
        sb.Append("// ~ THE SCRIBE").Append(NewLine);
    }

    private void AppendPageHeader(StringBuilder sb)
    {
        const string url = "https://github.com/BulletsForHumanity/Scribe";
        const int padding = 2;

        // Split header into lines, trimming surrounding blank lines.
        var rawLines = _header!.Split('\n');
        var headerLines = new List<string>();
        var start = 0;
        while (start < rawLines.Length && rawLines[start].TrimEnd('\r').Trim().Length == 0)
            start++;
        var end = rawLines.Length - 1;
        while (end > start && rawLines[end].TrimEnd('\r').Trim().Length == 0)
            end--;
        for (var i = start; i <= end; i++)
            headerLines.Add(rawLines[i].TrimEnd('\r'));

        // Determine inner box width from the longest content line.
        var maxContent = url.Length;
        foreach (var line in headerLines)
            if (line.Length > maxContent) maxContent = line.Length;
        var innerWidth = maxContent + padding * 2;

        var dashes = new string('\u2500', innerWidth);
        var empty = new string(' ', innerWidth);

        string Pad(string text) =>
            "  " + text + new string(' ', innerWidth - padding - text.Length);

        //  ┌──────────────────────────────────────────────────┐
        //  │                                                  │
        //  │  [header lines ...]                              │
        //  │                                                  │
        //  │──────────────────────────────────────────────────│      .
        //  │  IN COLLABORATION WITH                           │     /|
        //  │  ~ THE SCRIBE                                    │    //
        //  │  https://github.com/BulletsForHumanity/Scribe    │   //
        //  │  HEREBY ANNOUNCE                                 │  /'
        //  │                                                  │
        //  └──────────────────────────────────────────────────┘

        // Top border + header section
        sb.Append("//  \u250c").Append(dashes).Append('\u2510').Append(NewLine);
        sb.Append("//  \u2502").Append(empty).Append('\u2502').Append(NewLine);
        foreach (var line in headerLines)
            sb.Append("//  \u2502").Append(Pad(line)).Append('\u2502').Append(NewLine);
        sb.Append("//  \u2502").Append(empty).Append('\u2502').Append(NewLine);

        // Fixed section with quill art
        sb.Append("//  \u2502").Append(dashes).Append("\u2502      .").Append(NewLine);
        sb.Append("//  \u2502").Append(Pad("IN COLLABORATION WITH")).Append("\u2502     /|").Append(NewLine);
        sb.Append("//  \u2502").Append(Pad("~ THE SCRIBE")).Append("\u2502    //").Append(NewLine);
        sb.Append("//  \u2502").Append(Pad(url)).Append("\u2502   //").Append(NewLine);
        sb.Append("//  \u2502").Append(Pad("HEREBY ANNOUNCE")).Append("\u2502  /'").Append(NewLine);
        sb.Append("//  \u2502").Append(empty).Append('\u2502').Append(NewLine);

        // Bottom border
        sb.Append("//  \u2514").Append(dashes).Append('\u2518').Append(NewLine);
    }
}
