using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Leaf-predicate extensions on <see cref="FocusShape{TypeArgFocus}"/>. v1 subset
///     per the gap analysis B.4 catalogue — covers the "does this generic type argument
///     satisfy a structural relationship?" checks. For broader type-shape validation on
///     the argument, use <see cref="TypeArgFocusShapeExtensions.AsTypeShape"/>.
/// </summary>
public static class TypeArgFocusLeafPredicates
{
    /// <summary>
    ///     Require the type argument to implement the interface named by
    ///     <paramref name="metadataName"/>. Metadata name matches either the fully
    ///     qualified display string (e.g. <c>System.IDisposable</c>) or the arity-stripped
    ///     generic form (e.g. <c>System.Collections.Generic.IEnumerable</c> for
    ///     <c>IEnumerable&lt;T&gt;</c>).
    /// </summary>
    public static FocusShape<TypeArgFocus> MustImplement(
        this FocusShape<TypeArgFocus> shape,
        string metadataName,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        shape.AddCheck(new FocusCheck<TypeArgFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE070"),
            Title: spec?.Title ?? "Type argument must implement required interface",
            MessageFormat: spec?.Message ?? "Type argument '{0}' must implement '{1}'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => Implements(focus.Symbol, interned),
            MessageArgs: focus => EquatableArray.Create(focus.TypeFqn, interned)));
        return shape;
    }

    /// <summary>
    ///     Require the type argument to derive (directly or transitively) from the base
    ///     class named by <paramref name="metadataName"/>.
    /// </summary>
    public static FocusShape<TypeArgFocus> MustDeriveFrom(
        this FocusShape<TypeArgFocus> shape,
        string metadataName,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        shape.AddCheck(new FocusCheck<TypeArgFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE071"),
            Title: spec?.Title ?? "Type argument must derive from required base",
            MessageFormat: spec?.Message ?? "Type argument '{0}' must derive from '{1}'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => DerivesFrom(focus.Symbol, interned),
            MessageArgs: focus => EquatableArray.Create(focus.TypeFqn, interned)));
        return shape;
    }

    private static bool Implements(ITypeSymbol symbol, string metadataName)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (NameMatches(iface, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(ITypeSymbol symbol, string metadataName)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (NameMatches(current, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(INamedTypeSymbol symbol, string metadataName)
    {
        var fqn = symbol.ToDisplayString();
        if (string.Equals(fqn, metadataName, StringComparison.Ordinal))
        {
            return true;
        }

        var bracket = fqn.IndexOf('<');
        var bare = bracket > 0 ? fqn.Substring(0, bracket) : fqn;
        return string.Equals(bare, metadataName, StringComparison.Ordinal);
    }
}
