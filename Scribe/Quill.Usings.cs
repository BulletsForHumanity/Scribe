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
    ///     Replaces <c>global::Ns.Type</c> references in the body with short names
    ///     when <c>Ns</c> matches a namespace already registered via <see cref="Using"/>
    ///     or <see cref="Usings"/>. Unmatched <c>global::</c> references are left as-is.
    ///     Registered namespaces are tried longest-first (by segment count) so that
    ///     <c>System.Text.Json</c> is preferred over <c>System.Text</c>.
    /// </summary>
    private void ResolveGlobalReferences()
    {
        if (_usings.Count == 0)
            return;

        var body = _body.ToString();
        if (body.IndexOf("global::", StringComparison.Ordinal) < 0)
            return;

        // Sort registered namespaces longest-first by segment count, then by length,
        // so "System.Text.Json.Serialization" is tried before "System.Text.Json".
        var orderedNamespaces = new List<string>(_usings.Count);
        foreach (var ns in _usings)
        {
            // Skip alias entries ("AliasName = Ns.Type") — they are not plain namespaces.
            if (ns.IndexOf('=') >= 0)
                continue;
            orderedNamespaces.Add(ns);
        }

        orderedNamespaces.Sort((a, b) =>
        {
            var aSeg = CountDots(a) + 1;
            var bSeg = CountDots(b) + 1;
            if (bSeg != aSeg)
                return bSeg.CompareTo(aSeg);
            return b.Length.CompareTo(a.Length);
        });

        // For each registered namespace, find and replace "global::<ns>." with ""
        // effectively leaving just the remainder (type name + any member access).
        foreach (var ns in orderedNamespaces)
        {
            var token = "global::" + ns + ".";
            body = body.Replace(token, "");
        }

        _body.Clear();
        _body.Append(body);
    }

    private static int CountDots(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '.')
                count++;
        }

        return count;
    }
}
