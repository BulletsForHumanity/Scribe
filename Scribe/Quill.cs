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
///         <b>When to use:</b> generators with loops, conditionals, or multi-section output
///         (e.g. <c>EssenceWork</c>, <c>CommandEndpointsSigilWork</c>).
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
public sealed class Quill
{
    internal readonly StringBuilder _body = new();
    private readonly HashSet<string> _usings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _typeRefs = new(StringComparer.Ordinal);
    private int _indent;
    private string? _namespace;
    private bool _built;

    // ── Usings (can be called at any point during building) ──────────────

    /// <summary>Add a using directive. Duplicates are ignored.</summary>
    public Quill Using(string ns)
    {
        _usings.Add(ns);
        return this;
    }

    /// <summary>Add multiple using directives. Duplicates are ignored.</summary>
    public Quill Usings(params string[] namespaces)
    {
        foreach (var ns in namespaces)
            _usings.Add(ns);

        return this;
    }

    // ── Type References ─────────────────────────────────────────────────

    /// <summary>
    ///     Register a <c>global::</c> fully-qualified type name for automatic resolution.
    ///     During <see cref="Inscribe"/>, registered references are replaced with their short names
    ///     and appropriate <c>using</c> directives (or aliases for conflicts) are added.
    /// </summary>
    /// <returns>The original FQN (pass-through for use in interpolated strings).</returns>
    public string Ref(string globalFqn)
    {
        _typeRefs.Add(globalFqn);
        return globalFqn;
    }

    /// <summary>
    ///     Register multiple <c>global::</c> fully-qualified type names for automatic resolution.
    ///     Returns a <see cref="RefResult"/> that supports tuple-style deconstruction:
    ///     <code>var (iParsable, iComparable) = q.Refs("global::System.IParsable", "global::System.IComparable");</code>
    /// </summary>
    public RefResult Refs(params string[] globalFqns)
    {
        foreach (var fqn in globalFqns)
            _typeRefs.Add(fqn);

        return new RefResult(globalFqns);
    }

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
            _body.AppendLine();
        else
            _body.Append(' ', _indent * 4).AppendLine(text);

        return this;
    }

    /// <summary>Append multiple individual lines, each at the current indentation level.</summary>
    public Quill Lines(params string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.Length is 0)
                _body.AppendLine();
            else
                _body.Append(' ', line.Length is 0 ? 0 : _indent * 4).AppendLine(line);
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
                _body.AppendLine();
            else
                _body.Append(' ', _indent * 4).AppendLine(text);
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
        _body.Append(' ', _indent * 4).Append("// ").AppendLine(text);
        return this;
    }

    // ── XML Documentation (standalone — for one-liner members) ─────────

    /// <summary>
    ///     Emit a <c>/// &lt;summary&gt;</c> XML doc block at the current indentation level.
    ///     Use this for standalone one-liner members (operators, fields, properties)
    ///     that don't use <see cref="Block(string)"/>. For members with blocks,
    ///     prefer <see cref="BlockScope.Summary"/> (post-decoration).
    /// </summary>
    public Quill Summary(string text)
    {
        _body.Append(BuildXmlDocString("summary", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;remarks&gt;</c> XML doc block at the current indentation level.
    /// </summary>
    public Quill Remarks(string text)
    {
        _body.Append(BuildXmlDocString("remarks", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;param name="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Param(string name, string text)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <param name=\"")
            .Append(name)
            .Append("\">")
            .Append(text)
            .AppendLine("</param>");
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;inheritdoc /&gt;</c> or <c>/// &lt;inheritdoc cref="..." /&gt;</c>
    ///     at the current indentation level.
    /// </summary>
    public Quill InheritDoc(string? cref = null)
    {
        _body.Append(' ', _indent * 4);
        if (cref is null)
            _body.AppendLine("/// <inheritdoc />");
        else
            _body.Append("/// <inheritdoc cref=\"").Append(cref).AppendLine("\" />");
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;typeparam name="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill TypeParam(string name, string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <typeparam name=\"")
            .Append(name)
            .Append("\">")
            .Append(description)
            .AppendLine("</typeparam>");
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;returns&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Returns(string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <returns>")
            .Append(description)
            .AppendLine("</returns>");
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;example&gt;</c> XML doc block at the current indentation level.
    /// </summary>
    public Quill Example(string text)
    {
        _body.Append(BuildXmlDocString("example", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;exception cref="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Exception(string cref, string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <exception cref=\"")
            .Append(cref)
            .Append("\">")
            .Append(description)
            .AppendLine("</exception>");
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;seealso cref="..." /&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill SeeAlso(string cref)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <seealso cref=\"")
            .Append(cref)
            .AppendLine("\" />");
        return this;
    }

    // ── Attributes ───────────────────────────────────────────────────────

    /// <summary>
    ///     Emit an attribute line: <c>[AttributeName]</c> or <c>[AttributeName(args)]</c>
    ///     at the current indentation level.
    /// </summary>
    public Quill Attribute(string name, string? args = null)
    {
        _body.Append(' ', _indent * 4).Append('[').Append(name);
        if (args is not null)
            _body.Append('(').Append(args).Append(')');
        _body.AppendLine("]");
        return this;
    }

    // ── Shortcuts ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Emit a collection initializer: <c>target = new Type { items... };</c>.
    ///     Handles braces, trailing commas, and indentation automatically.
    ///     Chain <see cref="ContentResult.Padded"/> to wrap in blank lines.
    /// </summary>
    public ContentResult ListInit<T>(
        string target,
        string type,
        IEnumerable<T> items,
        Func<T, string> selector
    )
    {
        var startPos = _body.Length;
        Line($"{target} = {type}");
        Line("{");
        using (Indent())
        {
            foreach (var item in items)
                Line($"{selector(item)},");
        }

        Line("};");
        return new ContentResult(this, startPos);
    }

    // ── Scoping ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Opens a block: writes the header and <c>{</c>, increases indent.
    ///     The header can be a single line or a multi-line raw string literal
    ///     (dedented and indented like <see cref="Lines(string)"/>).
    ///     Returns a <see cref="BlockScope"/> that supports post-decoration with XML docs
    ///     (inserted before the header) and closes the block on dispose.
    ///     Trailing blank lines inside the block are automatically trimmed before <c>}</c>.
    /// </summary>
    public BlockScope Block(string header)
    {
        var headerPos = _body.Length;
        var headerIndent = _indent;

        if (header.Contains("\n"))
            EmitDedentedLines(header);
        else
            _body.Append(' ', _indent * 4).AppendLine(header);

        _body.Append(' ', _indent * 4).AppendLine("{");
        _indent++;
        return new BlockScope(this, headerPos, headerIndent);
    }

    /// <summary>
    ///     Opens a bare <c>{</c> scope block for variable scoping or grouping.
    ///     Unlike <see cref="Block(string)"/>, has no header line and does not support XML docs.
    /// </summary>
    public BlockScope Scope()
    {
        var pos = _body.Length;
        _body.Append(' ', _indent * 4).AppendLine("{");
        _indent++;
        return new BlockScope(this, pos, _indent - 1);
    }

    /// <summary>
    ///     Opens a switch expression block: writes the header and <c>{</c>, increases indent.
    ///     On dispose, writes <c>};</c> (with semicolon, as required by switch expressions).
    ///     <code>
    ///     using (q.SwitchExpr("var ok = segments switch"))
    ///     {
    ///         q.Line("0 => true,");
    ///         q.Line("_ => false,");
    ///     }
    ///     // produces: var ok = segments switch { 0 => true, _ => false, };
    ///     </code>
    /// </summary>
    public SwitchExprScope SwitchExpr(string header)
    {
        if (header.Contains("\n"))
            EmitDedentedLines(header);
        else
            _body.Append(' ', _indent * 4).AppendLine(header);

        _body.Append(' ', _indent * 4).AppendLine("{");
        _indent++;
        return new SwitchExprScope(this);
    }

    /// <summary>
    ///     Opens a <c>case</c> block: writes <c>case {expression}:</c>, increases indent.
    ///     On dispose, emits <c>break;</c>, restores indent, and adds a trailing blank line.
    /// </summary>
    public CaseScope Case(string expression)
    {
        _body.Append(' ', _indent * 4).Append("case ").Append(expression).AppendLine(":");
        _body.Append(' ', _indent * 4).AppendLine("{");
        _indent++;
        return new CaseScope(this);
    }

    /// <summary>
    ///     Opens a <c>#region</c> block with the given name.
    ///     On dispose, emits <c>#endregion</c>.
    /// </summary>
    public RegionScope Region(string name)
    {
        _body.Append(' ', _indent * 4).Append("#region ").AppendLine(name);
        return new RegionScope(this);
    }

    /// <summary>
    ///     Increase indentation and return a disposable that restores it.
    ///     Prefer <see cref="Scope"/> when braces are appropriate.
    /// </summary>
    public IndentScope Indent()
    {
        _indent++;
        return new IndentScope(this);
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
    ///     Can only be called once.
    /// </summary>
    public string Inscribe()
    {
        if (_built)
            throw new InvalidOperationException("Quill.Inscribe() has already been called.");

        _built = true;

        ResolveGlobalReferences();

        var result = new StringBuilder();
        result.AppendLine("// <auto-generated/>");
        result.AppendLine("//                                            .");
        result.AppendLine("//                                           /|");
        result.AppendLine("                                            //");
        result.AppendLine("//  https://github.com/BulletsForHumanity/Scribe   //");
        result.AppendLine("//                                        /'");
        result.AppendLine("//                          ~ The Scribe \u00b7");
        result.AppendLine("#nullable enable");
        result.AppendLine();

        if (_usings.Count > 0)
        {
            foreach (var u in _usings.OrderBy(static x => x, StringComparer.Ordinal))
                result.Append("using ").Append(u).AppendLine(";");

            result.AppendLine();
        }

        if (_namespace is { Length: > 0 })
        {
            result.Append("namespace ").Append(_namespace).AppendLine(";");
            result.AppendLine();
        }

        result.Append(_body);
        return result.ToString();
    }

    // ── Global Reference Resolution ─────────────────────────────────────

    /// <summary>
    ///     Scans the body for registered <c>global::</c> type references, resolves them to short
    ///     names (adding <c>using</c> directives), and disambiguates conflicts with aliases.
    /// </summary>
    private void ResolveGlobalReferences()
    {
        if (_typeRefs.Count == 0)
            return;

        var body = _body.ToString();

        // Build (namespace, typeName) pairs from registered FQNs
        var refs = new Dictionary<string, (string Namespace, string TypeName)>(
            StringComparer.Ordinal
        );
        foreach (var fqn in _typeRefs)
        {
            if (!body.Contains(fqn))
                continue;

            var path = fqn.Substring("global::".Length); // e.g. "System.Guid"
            var lastDot = path.LastIndexOf('.');
            if (lastDot < 0)
                continue;

            refs[fqn] = (path.Substring(0, lastDot), path.Substring(lastDot + 1));
        }

        if (refs.Count == 0)
            return;

        // Group by short type name to detect conflicts
        var byTypeName = new Dictionary<
            string,
            List<KeyValuePair<string, (string Namespace, string TypeName)>>
        >(StringComparer.Ordinal);
        foreach (var kvp in refs)
        {
            if (!byTypeName.TryGetValue(kvp.Value.TypeName, out var list))
            {
                list = new List<KeyValuePair<string, (string Namespace, string TypeName)>>();
                byTypeName[kvp.Value.TypeName] = list;
            }

            list.Add(kvp);
        }

        // Build replacement map
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in byTypeName)
        {
            if (group.Value.Count == 1)
            {
                // No conflict — add using, replace with short name
                var entry = group.Value[0];
                _usings.Add(entry.Value.Namespace);
                replacements[entry.Key] = entry.Value.TypeName;
            }
            else
            {
                // Conflict — create disambiguated aliases
                var aliases = DisambiguateAliases(group.Value);
                foreach (var (globalRef, alias, ns, typeName) in aliases)
                {
                    _usings.Add($"{alias} = {ns}.{typeName}");
                    replacements[globalRef] = alias;
                }
            }
        }

        // Apply replacements (longest match first to avoid partial replacements)
        foreach (var from in replacements.Keys.OrderByDescending(static k => k.Length))
        {
            body = body.Replace(from, replacements[from]);
        }

        _body.Clear();
        _body.Append(body);
    }

    /// <summary>
    ///     Given a set of conflicting types (same short name, different namespaces), produces
    ///     disambiguated alias names by walking up namespace segments until each alias is unique.
    /// </summary>
    private static List<(
        string GlobalRef,
        string Alias,
        string Namespace,
        string TypeName
    )> DisambiguateAliases(List<KeyValuePair<string, (string Namespace, string TypeName)>> entries)
    {
        var typeName = entries[0].Value.TypeName;
        var nsParts = new List<string[]>(entries.Count);
        foreach (var e in entries)
            nsParts.Add(e.Value.Namespace.Split('.'));

        // Walk backwards through namespace segments until aliases are unique
        var depth = 1;
        while (depth <= nsParts.Max(static p => p.Length))
        {
            var candidates = new List<string>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var parts = nsParts[i];
                // Take the last `depth` namespace segments and prepend to type name
                var startIdx = Math.Max(0, parts.Length - depth);
                var prefix = string.Join("", parts, startIdx, parts.Length - startIdx);
                candidates.Add(prefix + typeName);
            }

            if (candidates.Distinct(StringComparer.Ordinal).Count() == candidates.Count)
            {
                // All aliases are unique at this depth
                var result = new List<(string, string, string, string)>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                    result.Add(
                        (
                            entries[i].Key,
                            candidates[i],
                            entries[i].Value.Namespace,
                            entries[i].Value.TypeName
                        )
                    );
                return result;
            }

            depth++;
        }

        // Fallback: use full namespace as prefix (should never happen in practice)
        var fallback = new List<(string, string, string, string)>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var fullPrefix = entries[i].Value.Namespace.Replace(".", "");
            fallback.Add(
                (
                    entries[i].Key,
                    fullPrefix + typeName,
                    entries[i].Value.Namespace,
                    entries[i].Value.TypeName
                )
            );
        }

        return fallback;
    }

    // ── Internal helpers ─────────────────────────────────────────────────

    /// <summary>
    ///     Ensure there is a blank line before the current position in the body,
    ///     unless we are at the start of the body or right after an opening <c>{</c>.
    /// </summary>
    internal void EnsureBlankLineSeparation()
    {
        if (_body.Length is 0)
            return;

        var pos = _body.Length - 1;
        if (pos >= 0 && _body[pos] == '\n')
        {
            var scanFrom = pos - 1;
            if (scanFrom >= 0 && _body[scanFrom] == '\r')
                scanFrom--;

            var lineStart = scanFrom;
            while (lineStart >= 0 && _body[lineStart] != '\n')
                lineStart--;

            lineStart++;

            var lineContent = _body.ToString(lineStart, scanFrom - lineStart + 1).Trim();
            if (lineContent.Length is 0 || lineContent == "{" || lineContent.StartsWith("//"))
                return;
        }

        _body.AppendLine();
    }

    /// <summary>
    ///     Check whether a blank-line separator is needed at a specific position in the body.
    /// </summary>
    internal bool NeedsBlankLineSeparationAt(int pos)
    {
        if (pos is 0)
            return false;

        var idx = pos - 1;
        if (idx < 0 || _body[idx] != '\n')
            return true;

        idx--;
        if (idx >= 0 && _body[idx] == '\r')
            idx--;

        var lineStart = idx;
        while (lineStart >= 0 && _body[lineStart] != '\n')
            lineStart--;

        lineStart++;

        if (lineStart > idx)
            return false;

        var lineContent = _body.ToString(lineStart, idx - lineStart + 1).Trim();
        return lineContent.Length > 0 && lineContent != "{" && !lineContent.StartsWith("//");
    }

    /// <summary>Build an XML doc tag block and return it as a string for insertion.</summary>
    internal string BuildXmlDocString(string tag, string text, int indent)
    {
        var sb = new StringBuilder();
        var lines = DedentRawString(text);
        var prefix = new string(' ', indent * 4) + "/// ";

        if (lines.Length is 1)
        {
            sb.Append(prefix)
                .Append('<')
                .Append(tag)
                .Append('>')
                .Append(lines[0])
                .Append("</")
                .Append(tag)
                .AppendLine(">");
        }
        else
        {
            sb.Append(prefix).Append('<').Append(tag).AppendLine(">");
            foreach (var line in lines)
            {
                if (line.Length is 0)
                    sb.Append(prefix).AppendLine();
                else
                    sb.Append(prefix).Append("    ").AppendLine(line);
            }

            sb.Append(prefix).Append("</").Append(tag).AppendLine(">");
        }

        return sb.ToString();
    }

    /// <summary>Dedent a raw string literal into individual lines, trimming surrounding blanks.</summary>
    internal static string[] DedentRawString(string text)
    {
        var rawLines = text.Split('\n');

        var start = 0;
        while (start < rawLines.Length && rawLines[start].TrimEnd('\r').Trim().Length is 0)
            start++;

        var end = rawLines.Length - 1;
        while (end > start && rawLines[end].TrimEnd('\r').Trim().Length is 0)
            end--;

        if (start > end)
            return [text.Trim()];

        var minIndent = int.MaxValue;
        for (var i = start; i <= end; i++)
        {
            var line = rawLines[i].TrimEnd('\r');
            if (line.Trim().Length is 0)
                continue;

            var leading = 0;
            while (leading < line.Length && line[leading] == ' ')
                leading++;

            if (leading < minIndent)
                minIndent = leading;
        }

        if (minIndent == int.MaxValue)
            minIndent = 0;

        var result = new string[end - start + 1];
        for (var i = start; i <= end; i++)
        {
            var line = rawLines[i].TrimEnd('\r');
            result[i - start] = line.Trim().Length is 0
                ? ""
                : (line.Length > minIndent ? line.Substring(minIndent) : "");
        }

        return result;
    }

    /// <summary>Dedent a raw string literal and emit each line at current indentation.</summary>
    private void EmitDedentedLines(string text)
    {
        foreach (var line in DedentRawString(text))
        {
            if (line.Length is 0)
                _body.AppendLine();
            else
                _body.Append(' ', _indent * 4).AppendLine(line);
        }
    }

    /// <summary>Remove trailing blank lines from the body (used before closing a block).</summary>
    internal void TrimTrailingBlankLines()
    {
        while (_body.Length >= 2)
        {
            if (_body[_body.Length - 1] == '\n' && _body[_body.Length - 2] == '\r')
            {
                if (
                    _body.Length >= 4
                    && _body[_body.Length - 3] == '\n'
                    && _body[_body.Length - 4] == '\r'
                )
                {
                    _body.Length -= 2;
                    continue;
                }

                if (_body.Length >= 3 && _body[_body.Length - 3] == '\n')
                {
                    _body.Length -= 2;
                    continue;
                }

                break;
            }

            if (_body[_body.Length - 1] == '\n')
            {
                if (_body.Length >= 2 && _body[_body.Length - 2] == '\n')
                {
                    _body.Length -= 1;
                    continue;
                }

                break;
            }

            break;
        }
    }

    // ── Result & Scope types ─────────────────────────────────────────────

    /// <summary>
    ///     Returned by content-emitting methods (<see cref="Lines(string)"/>,
    ///     <see cref="LinesFor{T}"/>, <see cref="ListInit{T}"/>).
    ///     Supports <see cref="Padded"/> to wrap emitted content with blank lines.
    ///     Implicitly converts back to <see cref="Quill"/> for fluent chaining.
    /// </summary>
    public readonly struct ContentResult
    {
        private readonly Quill _owner;
        private readonly int _startPos;

        internal ContentResult(Quill owner, int startPos)
        {
            _owner = owner;
            _startPos = startPos;
        }

        /// <summary>
        ///     Wrap the just-emitted content with empty lines before and after.
        ///     Inserts a blank line before the content (unless at start of block or already separated),
        ///     and appends a blank line after. Blocks already handle their own spacing —
        ///     use this for <c>Lines</c>, <c>LinesFor</c>, and <c>ListInit</c> content.
        /// </summary>
        public Quill Padded()
        {
            _owner._body.AppendLine();

            if (_owner.NeedsBlankLineSeparationAt(_startPos))
                _owner._body.Insert(_startPos, "\r\n");

            return _owner;
        }

        public static implicit operator Quill(ContentResult result) => result._owner;
    }

    /// <summary>
    ///     Disposable that closes a <c>{</c> block with <c>}</c> and decreases indent.
    ///     Supports post-decoration with XML documentation via <see cref="Summary"/>,
    ///     <see cref="Remarks"/>, <see cref="Param"/>, <see cref="TypeParam"/>,
    ///     <see cref="Returns"/>, <see cref="InheritDoc"/>, <see cref="Example"/>,
    ///     <see cref="Exception"/>, <see cref="SeeAlso"/>, and <see cref="Attribute"/> — these are inserted before the block header.
    ///     Trailing blank lines inside the block are automatically trimmed before <c>}</c>.
    /// </summary>
    public struct BlockScope : IDisposable
    {
        private readonly Quill _owner;
        private readonly int _headerPos;
        private readonly int _headerIndent;
        private int _insertLen;

        internal BlockScope(Quill owner, int headerPos, int headerIndent)
        {
            _owner = owner;
            _headerPos = headerPos;
            _headerIndent = headerIndent;
            _insertLen = 0;
        }

        /// <summary>Insert a <c>&lt;summary&gt;</c> XML doc before the block header.</summary>
        public BlockScope Summary(string text)
        {
            InsertXmlTag("summary", text);
            return this;
        }

        /// <summary>Insert a <c>&lt;remarks&gt;</c> XML doc before the block header.</summary>
        public BlockScope Remarks(string text)
        {
            InsertXmlTag("remarks", text);
            return this;
        }

        /// <summary>Insert a <c>&lt;param name="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Param(string name, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<param name=\"" + name + "\">" + description + "</param>\r\n");
            return this;
        }

        /// <summary>Insert a <c>&lt;typeparam name="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope TypeParam(string name, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(
                prefix + "<typeparam name=\"" + name + "\">" + description + "</typeparam>\r\n"
            );
            return this;
        }

        /// <summary>Insert a <c>&lt;returns&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Returns(string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<returns>" + description + "</returns>\r\n");
            return this;
        }

        /// <summary>Insert a <c>&lt;inheritdoc /&gt;</c> or <c>&lt;inheritdoc cref="..." /&gt;</c> before the block header.</summary>
        public BlockScope InheritDoc(string? cref = null)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            if (cref is null)
                InsertDoc(prefix + "<inheritdoc />\r\n");
            else
                InsertDoc(prefix + "<inheritdoc cref=\"" + cref + "\" />\r\n");
            return this;
        }

        /// <summary>Insert an <c>&lt;example&gt;</c> XML doc block before the block header.</summary>
        public BlockScope Example(string text)
        {
            InsertXmlTag("example", text);
            return this;
        }

        /// <summary>Insert an <c>&lt;exception cref="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Exception(string cref, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(
                prefix + "<exception cref=\"" + cref + "\">" + description + "</exception>\r\n"
            );
            return this;
        }

        /// <summary>Insert a <c>&lt;seealso cref="..." /&gt;</c> XML doc line before the block header.</summary>
        public BlockScope SeeAlso(string cref)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<seealso cref=\"" + cref + "\" />\r\n");
            return this;
        }

        /// <summary>Insert an attribute (<c>[Name]</c> or <c>[Name(args)]</c>) before the block header, after any XML docs.</summary>
        public BlockScope Attribute(string name, string? args = null)
        {
            var sb = new StringBuilder();
            sb.Append(' ', _headerIndent * 4).Append('[').Append(name);
            if (args is not null)
                sb.Append('(').Append(args).Append(')');
            sb.AppendLine("]");
            InsertDoc(sb.ToString());
            return this;
        }

        private void InsertXmlTag(string tag, string text) =>
            InsertDoc(_owner.BuildXmlDocString(tag, text, _headerIndent));

        private void InsertDoc(string doc)
        {
            if (_insertLen is 0 && _owner.NeedsBlankLineSeparationAt(_headerPos))
            {
                _owner._body.Insert(_headerPos, "\r\n");
                _insertLen += 2;
            }

            _owner._body.Insert(_headerPos + _insertLen, doc);
            _insertLen += doc.Length;
        }

        public void Dispose()
        {
            _owner.TrimTrailingBlankLines();
            _owner._indent--;
            _owner.Line("}");
        }
    }

    /// <summary>
    ///     Disposable that closes a <c>case</c> block: emits <c>break;</c>,
    ///     decreases indent, and adds a trailing blank line.
    /// </summary>
    public readonly struct CaseScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner.Line("break;");
            owner._indent--;
            owner._body.Append(' ', owner._indent * 4).AppendLine("}");
            owner._body.AppendLine();
        }
    }

    /// <summary>
    ///     Disposable that closes a switch expression block with <c>};</c> (semicolon).
    ///     Returned by <see cref="SwitchExpr"/>.
    /// </summary>
    public readonly struct SwitchExprScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner.TrimTrailingBlankLines();
            owner._indent--;
            owner._body.Append(' ', owner._indent * 4).AppendLine("};");
        }
    }

    /// <summary>
    ///     Disposable that decreases indent when disposed.
    ///     Returned by <see cref="Indent()"/>.
    /// </summary>
    public readonly struct IndentScope(Quill owner) : IDisposable
    {
        public void Dispose() => owner._indent = Math.Max(0, owner._indent - 1);
    }

    /// <summary>
    ///     Disposable that closes a <c>#region</c> block with <c>#endregion</c>.
    ///     Returned by <see cref="Region"/>.
    /// </summary>
    public readonly struct RegionScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner._body.Append(' ', owner._indent * 4).AppendLine("#endregion");
        }
    }

    /// <summary>
    ///     Result of <see cref="Refs"/>. Supports tuple-style deconstruction
    ///     so registered FQNs can be captured as local variables:
    ///     <code>var (iParsable, iComparable) = q.Refs("global::System.IParsable", "global::System.IComparable");</code>
    /// </summary>
    public readonly struct RefResult(string[] refs)
    {
        public string this[int index] => refs[index];
        public int Length => refs.Length;

        public void Deconstruct(out string a, out string b)
        {
            a = refs[0];
            b = refs[1];
        }

        public void Deconstruct(out string a, out string b, out string c)
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
        }

        public void Deconstruct(out string a, out string b, out string c, out string d)
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
            d = refs[3];
        }

        public void Deconstruct(
            out string a,
            out string b,
            out string c,
            out string d,
            out string e
        )
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
            d = refs[3];
            e = refs[4];
        }

        public void Deconstruct(
            out string a,
            out string b,
            out string c,
            out string d,
            out string e,
            out string f
        )
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
            d = refs[3];
            e = refs[4];
            f = refs[5];
        }

        public void Deconstruct(
            out string a,
            out string b,
            out string c,
            out string d,
            out string e,
            out string f,
            out string g
        )
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
            d = refs[3];
            e = refs[4];
            f = refs[5];
            g = refs[6];
        }

        public void Deconstruct(
            out string a,
            out string b,
            out string c,
            out string d,
            out string e,
            out string f,
            out string g,
            out string h
        )
        {
            a = refs[0];
            b = refs[1];
            c = refs[2];
            d = refs[3];
            e = refs[4];
            f = refs[5];
            g = refs[6];
            h = refs[7];
        }
    }
}
