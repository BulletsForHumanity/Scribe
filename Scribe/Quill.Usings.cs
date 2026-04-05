using System.Text;

namespace Scribe;

public sealed partial class Quill
{
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

    // ── Type Aliases ─────────────────────────────────────────────────────

    /// <summary>
    ///     Register a <c>using</c> alias for a type. The alias name is auto-generated
    ///     from the namespace hierarchy (e.g. <c>Foo.Bar.Widget</c> → <c>BarWidget</c>).
    ///     Returns the alias name for use in interpolated strings.
    ///     <para>
    ///         Use this when two namespaces contain the same type name
    ///         and you need both in the same generated file:
    ///         <code>
    ///         var fw = q.Alias("Foo.Bar", "Widget");
    ///         var bw = q.Alias("Baz.Qux", "Widget");
    ///         q.Line($"{fw} a = default;");  // BarWidget a = default;
    ///         q.Line($"{bw} b = default;");  // QuxWidget b = default;
    ///         </code>
    ///     </para>
    ///     Calling with the same namespace + type again returns the previously assigned alias.
    /// </summary>
    /// <param name="ns">The full namespace (e.g. <c>"Foo.Bar"</c>).</param>
    /// <param name="typeName">The short type name (e.g. <c>"Widget"</c>).</param>
    /// <returns>The generated alias name.</returns>
    public string Alias(string ns, string typeName)
    {
        var fqn = ns + "." + typeName;
        if (_aliasLookup.TryGetValue(fqn, out var existing))
            return existing;

        var alias = GenerateAliasName(ns, typeName);
        _aliases[alias] = fqn;
        _aliasLookup[fqn] = alias;
        return alias;
    }

    /// <summary>
    ///     Register a <c>using</c> alias for a type with an explicit alias name.
    ///     Returns the alias name for use in interpolated strings.
    ///     <code>
    ///     var w = q.Alias("Foo.Bar", "Widget", "FooWidget");
    ///     q.Line($"{w} x = default;");  // FooWidget x = default;
    ///     // Inscribe emits: using FooWidget = Foo.Bar.Widget;
    ///     </code>
    /// </summary>
    /// <param name="ns">The full namespace (e.g. <c>"Foo.Bar"</c>).</param>
    /// <param name="typeName">The short type name (e.g. <c>"Widget"</c>).</param>
    /// <param name="aliasName">The explicit alias name to use.</param>
    /// <returns>The alias name (same as <paramref name="aliasName"/>).</returns>
    public string Alias(string ns, string typeName, string aliasName)
    {
        var fqn = ns + "." + typeName;
        _aliases[aliasName] = fqn;
        _aliasLookup[fqn] = aliasName;
        return aliasName;
    }

    /// <summary>Generate a unique alias name by walking up namespace segments.</summary>
    private string GenerateAliasName(string ns, string typeName)
    {
        var parts = ns.Split('.');
        for (var depth = 1; depth <= parts.Length; depth++)
        {
            var startIdx = Math.Max(0, parts.Length - depth);
            var prefix = string.Join("", parts, startIdx, parts.Length - startIdx);
            var candidate = prefix + typeName;
            if (!_aliases.ContainsKey(candidate))
                return candidate;
        }

        // Fallback: full namespace collapsed
        return ns.Replace(".", "") + typeName;
    }

    // ── Global Reference Resolution ─────────────────────────────────────

    /// <summary>
    ///     Scans the body for registered <c>global::</c> type references, resolves them to short
    ///     names (adding <c>using</c> directives), and disambiguates conflicts with aliases.
    /// </summary>
    private void ResolveGlobalReferences()
    {
        var body = _body.ToString();

        // Auto-discover all global:: references in the body
        var refs = new Dictionary<string, (string Namespace, string TypeName)>(
            StringComparer.Ordinal
        );
        var searchFrom = 0;
        while (true)
        {
            var idx = body.IndexOf("global::", searchFrom, StringComparison.Ordinal);
            if (idx < 0)
                break;

            // Extract the full dotted identifier: global::Some.Namespace.TypeName
            // Stops at the first character that is not part of a qualified C# identifier.
            // Member access chains (global::System.StringComparer.Ordinal) will be consumed
            // whole — use Using() + short names for those patterns.
            var start = idx;
            var end = idx + "global::".Length;
            while (end < body.Length && (char.IsLetterOrDigit(body[end]) || body[end] == '.' || body[end] == '_'))
                end++;

            // Trim trailing dot if the identifier ended on one
            while (end > start + "global::".Length && body[end - 1] == '.')
                end--;

            var fqn = body.Substring(start, end - start);

            // If '(' follows immediately, the last segment MAY be a method call — peel it
            // back so the type reference is what remains. But don't peel when the global::
            // is preceded by 'new ' or 'typeof(' — those indicate the whole thing is a type.
            if (end < body.Length && body[end] == '(')
            {
                var isConstructor = false;
                if (start >= 4)
                {
                    var before = body.Substring(Math.Max(0, start - 7), Math.Min(7, start));
                    isConstructor = before.EndsWith("new ", StringComparison.Ordinal)
                                   || before.EndsWith("typeof(", StringComparison.Ordinal);
                }

                if (!isConstructor)
                {
                    var dotPos = fqn.LastIndexOf('.');
                    if (dotPos > "global::".Length)
                    {
                        fqn = fqn.Substring(0, dotPos);
                        end = start + fqn.Length;
                    }
                }
            }

            searchFrom = end;

            if (refs.ContainsKey(fqn))
                continue;

            var path = fqn.Substring("global::".Length);
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
}
