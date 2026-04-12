using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Attributes;

/// <summary>
///     Typed, cache-safe single-attribute reader. Replaces the manual
///     <c>ConstructorArguments[i].Value is string</c> pattern that every Roslyn
///     source generator reinvents, with a small declarative projection surface.
/// </summary>
/// <remarks>
///     <para>
///         Usage — ad-hoc read for a transient stage:
///     </para>
///     <code>
///         var reader = AttributeSchema.For(symbol, "My.Ns.FooAttribute");
///         if (reader.Exists)
///         {
///             var name = reader.Ctor&lt;string&gt;(0);
///             var max  = reader.Named&lt;int&gt;("Max", 100);
///         }
///     </code>
///     <para>
///         Usage — cache-safe projection into a user model, with collected diagnostics:
///     </para>
///     <code>
///         var result = AttributeSchema.Read(symbol, "My.Ns.FooAttribute", (ref AttributeReader r) =>
///             new MyModel(
///                 Name:   r.Ctor&lt;string&gt;(0),
///                 Format: r.Named&lt;string&gt;("Format", "default")));
///         // result.Exists, result.Model, result.Errors, result.Location
///     </code>
/// </remarks>
public static class AttributeSchema
{
    /// <summary>
    ///     Return a <see cref="AttributeReader"/> scoped to the first attribute on
    ///     <paramref name="symbol"/> whose class FQN equals <paramref name="attributeFqn"/>.
    ///     If no match is found, the returned reader's <see cref="AttributeReader.Exists"/>
    ///     is <see langword="false"/> and all typed reads return <c>default</c>.
    /// </summary>
    /// <param name="symbol">The symbol to inspect.</param>
    /// <param name="attributeFqn">
    ///     Fully-qualified type name of the attribute, with or without the <c>Attribute</c>
    ///     suffix — both forms are accepted. Matched via the attribute class's display string.
    /// </param>
    public static AttributeReader For(ISymbol symbol, string attributeFqn)
    {
        var attribute = FindAttribute(symbol, attributeFqn);
        return new AttributeReader(attribute);
    }

    /// <summary>
    ///     Project the first matching attribute into a cache-safe model. Collects any typed-read
    ///     mismatches into <see cref="AttributeReadResult{TModel}.Errors"/>.
    /// </summary>
    /// <typeparam name="TModel">The projection target. Constrain to <see cref="IEquatable{T}"/> so the result can flow through the incremental cache.</typeparam>
    public static AttributeReadResult<TModel> Read<TModel>(
        ISymbol symbol,
        string attributeFqn,
        AttributeProjection<TModel> project)
        where TModel : IEquatable<TModel>
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        var attribute = FindAttribute(symbol, attributeFqn);
        var reader = new AttributeReader(attribute);
        var model = project(ref reader);

        return new AttributeReadResult<TModel>(
            Model: model,
            Exists: reader.Exists,
            Location: reader.Location,
            Errors: reader.DrainErrors());
    }

    /// <summary>Convenience — <see langword="true"/> if <paramref name="symbol"/> carries the named attribute.</summary>
    public static bool Has(ISymbol symbol, string attributeFqn) =>
        FindAttribute(symbol, attributeFqn) is not null;

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeFqn)
    {
        if (symbol is null || string.IsNullOrEmpty(attributeFqn))
        {
            return null;
        }

        var normalized = Normalize(attributeFqn);

        foreach (var attribute in symbol.GetAttributes())
        {
            var fqn = attribute.AttributeClass?.ToDisplayString();
            if (fqn is null)
            {
                continue;
            }

            // Generic attributes like [Foo<int>] produce a display string with type args;
            // strip them for the match so callers can pass the bare name.
            var fqnBase = StripTypeArgs(fqn);

            if (string.Equals(fqn, attributeFqn, StringComparison.Ordinal)
                || string.Equals(fqnBase, attributeFqn, StringComparison.Ordinal)
                || string.Equals(Normalize(fqn), normalized, StringComparison.Ordinal)
                || string.Equals(Normalize(fqnBase), normalized, StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    // Strip a trailing "Attribute" suffix so both "Foo" and "FooAttribute" match the class "FooAttribute".
    private static string Normalize(string fqn)
    {
        const string Suffix = "Attribute";
        if (fqn.Length > Suffix.Length && fqn.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return fqn.Substring(0, fqn.Length - Suffix.Length);
        }

        return fqn;
    }

    private static string StripTypeArgs(string fqn)
    {
        var bracket = fqn.IndexOf('<');
        return bracket > 0 ? fqn.Substring(0, bracket) : fqn;
    }
}

/// <summary>Projection delegate for <see cref="AttributeSchema.Read{TModel}"/>.</summary>
public delegate TModel AttributeProjection<out TModel>(ref AttributeReader reader);

/// <summary>
///     Cache-safe result of <see cref="AttributeSchema.Read{TModel}"/>.
///     All fields are pipeline-safe.
/// </summary>
public readonly record struct AttributeReadResult<TModel>(
    TModel Model,
    bool Exists,
    LocationInfo? Location,
    EquatableArray<DiagnosticInfo> Errors)
    where TModel : IEquatable<TModel>;
