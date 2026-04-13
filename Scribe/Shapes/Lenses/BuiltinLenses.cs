using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes.Lenses;

/// <summary>
///     Static factory methods for every built-in lens shipped with Scribe. Each factory
///     returns a <see cref="Lens{TSource, TTarget}"/> whose <see cref="Lens{TSource, TTarget}.Navigate"/>
///     enumerates children of the source focus in source order and whose
///     <see cref="Lens{TSource, TTarget}.Smudge"/> resolves the child's declaration span.
/// </summary>
internal static class BuiltinLenses
{
    /// <summary>
    ///     <c>TypeFocus → AttributeFocus</c>. Navigates every attribute application on the
    ///     type whose class FQN equals <paramref name="attributeFqn"/> (open-generic forms
    ///     are matched by the bare name before <c>&lt;</c>). Per-usage index is carried on
    ///     the focus to disambiguate multiple applications of the same attribute class.
    /// </summary>
    public static Lens<TypeFocus, AttributeFocus> Attributes(string attributeFqn)
    {
        if (string.IsNullOrEmpty(attributeFqn))
        {
            throw new ArgumentException("Attribute FQN must not be empty.", nameof(attributeFqn));
        }

        var interned = InternPool.Intern(attributeFqn);
        return new Lens<TypeFocus, AttributeFocus>(
            navigate: parent => NavigateAttributes(parent, interned),
            smudge: focus => focus.Origin);
    }

    private static IEnumerable<AttributeFocus> NavigateAttributes(TypeFocus parent, string attributeFqn)
    {
        var ownerFqn = parent.Fqn;
        var attributes = parent.Symbol.GetAttributes();
        var index = 0;
        foreach (var attribute in attributes)
        {
            var cls = attribute.AttributeClass;
            if (cls is null)
            {
                continue;
            }

            var displayed = cls.ToDisplayString();
            if (!MatchesAttributeFqn(displayed, attributeFqn))
            {
                continue;
            }

            var syntaxRef = attribute.ApplicationSyntaxReference;
            var origin = syntaxRef is null
                ? (LocationInfo?)null
                : LocationInfo.From(Location.Create(syntaxRef.SyntaxTree, syntaxRef.Span));

            yield return new AttributeFocus(
                data: attribute,
                attributeFqn: InternPool.Intern(displayed),
                ownerFqn: ownerFqn,
                index: index,
                origin: origin);

            index++;
        }
    }

    private static bool MatchesAttributeFqn(string displayed, string fqn)
    {
        if (string.Equals(displayed, fqn, StringComparison.Ordinal))
        {
            return true;
        }

        var bracket = displayed.IndexOf('<');
        if (bracket <= 0)
        {
            return false;
        }

        return string.Equals(displayed.Substring(0, bracket), fqn, StringComparison.Ordinal);
    }

    /// <summary>
    ///     <c>TypeFocus → MemberFocus</c>. Navigates every directly-declared member of
    ///     the type in source order, optionally filtered by
    ///     <see cref="Microsoft.CodeAnalysis.SymbolKind"/>. Implicit and synthesized
    ///     members are excluded; inherited members are not visited. Smudge resolves
    ///     the member's declaration span.
    /// </summary>
    public static Lens<TypeFocus, MemberFocus> Members(SymbolKind? kindFilter = null) =>
        new Lens<TypeFocus, MemberFocus>(
            navigate: parent => NavigateMembers(parent, kindFilter),
            smudge: focus => focus.Origin);

    private static IEnumerable<MemberFocus> NavigateMembers(TypeFocus parent, SymbolKind? kindFilter)
    {
        var ownerFqn = parent.Fqn;
        var members = new List<ISymbol>();
        foreach (var member in parent.Symbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            if (member.DeclaringSyntaxReferences.Length == 0)
            {
                continue;
            }

            if (kindFilter is { } kind && member.Kind != kind)
            {
                continue;
            }

            members.Add(member);
        }

        members.Sort(DeclarationSpanComparer.Instance);

        foreach (var member in members)
        {
            var syntaxRef = member.DeclaringSyntaxReferences[0];
            var origin = LocationInfo.From(
                Location.Create(syntaxRef.SyntaxTree, syntaxRef.Span));

            yield return new MemberFocus(
                symbol: member,
                memberFqn: InternPool.Intern(member.ToDisplayString()),
                ownerFqn: ownerFqn,
                kind: member.Kind,
                origin: origin);
        }
    }

    /// <summary>
    ///     <c>TypeFocus → BaseTypeChainFocus</c>. Navigates the type's inheritance chain
    ///     from the immediate base up to (but excluding) <see cref="object"/>. Each step
    ///     carries the depth above the starting type. Interfaces are not included —
    ///     use the interface-specific lens (or
    ///     <see cref="TypeShape.Implementing(string)"/>) for interface navigation.
    ///     Smudge resolves to the base type's own declaration span.
    /// </summary>
    public static Lens<TypeFocus, BaseTypeChainFocus> BaseTypeChain() =>
        new Lens<TypeFocus, BaseTypeChainFocus>(
            navigate: NavigateBaseTypeChain,
            smudge: focus => focus.Origin);

    private static IEnumerable<BaseTypeChainFocus> NavigateBaseTypeChain(TypeFocus parent)
    {
        var rootOrigin = parent.Origin;
        var current = parent.Symbol.BaseType;
        var depth = 0;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            var origin = FirstDeclarationOrigin(current);
            yield return new BaseTypeChainFocus(
                symbol: current,
                typeFqn: InternPool.Intern(current.ToDisplayString()),
                depth: depth,
                rootOrigin: rootOrigin,
                origin: origin);

            current = current.BaseType;
            depth++;
        }
    }

    private static LocationInfo? FirstDeclarationOrigin(ISymbol symbol)
    {
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length == 0)
        {
            var locations = symbol.Locations;
            return locations.Length == 0
                ? null
                : LocationInfo.From(locations[0]);
        }

        var syntaxRef = refs[0];
        return LocationInfo.From(Location.Create(syntaxRef.SyntaxTree, syntaxRef.Span));
    }

    /// <summary>
    ///     <c>AttributeFocus → TypeArgFocus</c>. Navigates the type argument at the given
    ///     position in the attribute's generic type argument list — e.g. index <c>0</c>
    ///     of <c>[Foo&lt;string&gt;]</c> yields a focus carrying <c>string</c>. Yields no
    ///     focus when the attribute has no generics or <paramref name="index"/> is out
    ///     of range.
    /// </summary>
    public static Lens<AttributeFocus, TypeArgFocus> GenericTypeArg(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        return new Lens<AttributeFocus, TypeArgFocus>(
            navigate: parent => NavigateGenericTypeArg(parent, index),
            smudge: focus => focus.Origin);
    }

    private static IEnumerable<TypeArgFocus> NavigateGenericTypeArg(AttributeFocus parent, int index)
    {
        var cls = parent.Data.AttributeClass;
        if (cls is null)
        {
            yield break;
        }

        var args = cls.TypeArguments;
        if (index >= args.Length)
        {
            yield break;
        }

        var arg = args[index];
        yield return new TypeArgFocus(
            symbol: arg,
            typeFqn: InternPool.Intern(arg.ToDisplayString()),
            index: index,
            parentOrigin: parent.Origin,
            origin: parent.Origin);
    }

    /// <summary>
    ///     <c>AttributeFocus → ConstructorArgFocus&lt;T&gt;</c>. Navigates the positional
    ///     constructor argument at the given index, coercing its value to
    ///     <typeparamref name="T"/>. Yields no focus when the index is out of range or
    ///     the value cannot be coerced.
    /// </summary>
    public static Lens<AttributeFocus, ConstructorArgFocus<T>> ConstructorArg<T>(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        return new Lens<AttributeFocus, ConstructorArgFocus<T>>(
            navigate: parent => NavigateConstructorArg<T>(parent, index),
            smudge: focus => focus.Origin);
    }

    private static IEnumerable<ConstructorArgFocus<T>> NavigateConstructorArg<T>(AttributeFocus parent, int index)
    {
        var args = parent.Data.ConstructorArguments;
        if (index >= args.Length)
        {
            yield break;
        }

        var tc = args[index];
        if (!TryCoerce<T>(tc, out var value))
        {
            yield break;
        }

        var ownerBreadcrumb = InternPool.Intern(parent.AttributeFqn + ">" + parent.OwnerFqn);
        yield return new ConstructorArgFocus<T>(
            typedConstant: tc,
            value: value,
            index: index,
            ownerFqn: ownerBreadcrumb,
            origin: parent.Origin);
    }

    /// <summary>
    ///     <c>AttributeFocus → NamedArgFocus&lt;T&gt;</c>. Navigates the named argument
    ///     <paramref name="name"/> on the attribute application, coercing its value to
    ///     <typeparamref name="T"/>. Yields no focus when no argument matches the name
    ///     or the value cannot be coerced.
    /// </summary>
    public static Lens<AttributeFocus, NamedArgFocus<T>> NamedArg<T>(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Named argument name must not be empty.", nameof(name));
        }

        return new Lens<AttributeFocus, NamedArgFocus<T>>(
            navigate: parent => NavigateNamedArg<T>(parent, name),
            smudge: focus => focus.Origin);
    }

    private static IEnumerable<NamedArgFocus<T>> NavigateNamedArg<T>(AttributeFocus parent, string name)
    {
        foreach (var kvp in parent.Data.NamedArguments)
        {
            if (!string.Equals(kvp.Key, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryCoerce<T>(kvp.Value, out var value))
            {
                yield break;
            }

            var ownerBreadcrumb = InternPool.Intern(parent.AttributeFqn + ">" + parent.OwnerFqn);
            yield return new NamedArgFocus<T>(
                typedConstant: kvp.Value,
                value: value,
                name: name,
                ownerFqn: ownerBreadcrumb,
                origin: parent.Origin);
            yield break;
        }
    }

    /// <summary>
    ///     <c>TypeArgFocus → TypeFocus</c>. Re-enters the type-shape world rooted at
    ///     the type argument, enabling reuse of type-level lenses under a generic
    ///     argument. Yields no focus when the argument's symbol is not an
    ///     <see cref="INamedTypeSymbol"/> (e.g. type parameters, arrays, pointers).
    /// </summary>
    public static Lens<TypeArgFocus, TypeFocus> TypeArgAsTypeShape() =>
        new Lens<TypeArgFocus, TypeFocus>(
            navigate: NavigateTypeArgAsTypeShape,
            smudge: focus => focus.Origin);

    private static IEnumerable<TypeFocus> NavigateTypeArgAsTypeShape(TypeArgFocus parent)
    {
        if (parent.Symbol is not INamedTypeSymbol named)
        {
            yield break;
        }

        yield return new TypeFocus(
            symbol: named,
            fqn: InternPool.Intern(named.ToDisplayString()),
            origin: FirstDeclarationOrigin(named) ?? parent.Origin);
    }

    /// <summary>
    ///     <c>BaseTypeChainFocus → TypeFocus</c>. Re-enters the type-shape world rooted
    ///     at a base-chain step, enabling type-level lenses to run on the base type.
    /// </summary>
    public static Lens<BaseTypeChainFocus, TypeFocus> BaseTypeChainAsTypeShape() =>
        new Lens<BaseTypeChainFocus, TypeFocus>(
            navigate: NavigateBaseTypeChainAsTypeShape,
            smudge: focus => focus.Origin);

    private static IEnumerable<TypeFocus> NavigateBaseTypeChainAsTypeShape(BaseTypeChainFocus parent)
    {
        yield return new TypeFocus(
            symbol: parent.Symbol,
            fqn: parent.TypeFqn,
            origin: parent.Origin);
    }

    private static bool TryCoerce<T>(TypedConstant tc, out T? value)
    {
        if (tc.IsNull)
        {
            value = default;
            return !typeof(T).IsValueType
                || Nullable.GetUnderlyingType(typeof(T)) is not null;
        }

        if (tc.Value is T coerced)
        {
            value = coerced;
            return true;
        }

        // ITypeSymbol-valued constants (e.g. typeof(X)) carry ITypeSymbol values; if
        // the caller asked for T = string we unwrap to its display form.
        if (typeof(T) == typeof(string) && tc.Value is ITypeSymbol ts)
        {
            value = (T)(object)ts.ToDisplayString();
            return true;
        }

        value = default;
        return false;
    }

    private sealed class DeclarationSpanComparer : IComparer<ISymbol>
    {
        public static readonly DeclarationSpanComparer Instance = new();

        public int Compare(ISymbol? x, ISymbol? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var xRef = x.DeclaringSyntaxReferences[0];
            var yRef = y.DeclaringSyntaxReferences[0];
            var fileCmp = string.CompareOrdinal(xRef.SyntaxTree.FilePath, yRef.SyntaxTree.FilePath);
            return fileCmp != 0 ? fileCmp : xRef.Span.Start.CompareTo(yRef.Span.Start);
        }
    }
}
