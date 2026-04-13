using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for a single type argument at a specific position inside a
///     generic attribute, base-list entry, or method signature. Produced by the
///     <c>GenericTypeArg(index)</c> lens.
/// </summary>
/// <remarks>
///     <para>
///         The identity breadcrumb is <c>(TypeFqn, Index, ParentOrigin, Origin)</c>.
///         <see cref="ParentOrigin"/> disambiguates the same (Fqn, Index) pair when the
///         navigation comes from two different parent foci — e.g. the same
///         <c>string</c> argument at index 0 on two different <c>[Foo&lt;string&gt;]</c>
///         attribute usages.
///     </para>
///     <para>
///         <see cref="Symbol"/> is carried for in-flight use and excluded from equality.
///     </para>
/// </remarks>
public readonly struct TypeArgFocus : IEquatable<TypeArgFocus>
{
    /// <summary>The type argument symbol. Excluded from equality — do not use for caching.</summary>
    public ITypeSymbol Symbol { get; }

    /// <summary>Fully-qualified name of the type argument (interned).</summary>
    public string TypeFqn { get; }

    /// <summary>Zero-based position of the argument in its generic argument list.</summary>
    public int Index { get; }

    /// <summary>
    ///     Source span of the parent focus — used to disambiguate when the same
    ///     <c>(TypeFqn, Index)</c> appears under multiple parents in the same compilation.
    /// </summary>
    public LocationInfo? ParentOrigin { get; }

    /// <summary>Source span of the <c>TypeSyntax</c> at the generic position — the cache-stable breadcrumb.</summary>
    public LocationInfo? Origin { get; }

    public TypeArgFocus(
        ITypeSymbol symbol,
        string typeFqn,
        int index,
        LocationInfo? parentOrigin,
        LocationInfo? origin)
    {
        Symbol = symbol;
        TypeFqn = typeFqn;
        Index = index;
        ParentOrigin = parentOrigin;
        Origin = origin;
    }

    public bool Equals(TypeArgFocus other) =>
        string.Equals(TypeFqn, other.TypeFqn, StringComparison.Ordinal)
        && Index == other.Index
        && Nullable.Equals(ParentOrigin, other.ParentOrigin)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is TypeArgFocus other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TypeFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(TypeFqn);
            hash = (hash * 397) ^ Index;
            hash = (hash * 397) ^ (ParentOrigin?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(TypeArgFocus left, TypeArgFocus right) => left.Equals(right);

    public static bool operator !=(TypeArgFocus left, TypeArgFocus right) => !left.Equals(right);
}
