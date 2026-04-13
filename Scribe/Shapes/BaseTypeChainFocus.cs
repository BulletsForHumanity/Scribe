using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for one step of a type's inheritance chain. The
///     <c>BaseTypeChain()</c> lens yields one <see cref="BaseTypeChainFocus"/> per
///     chain element, ordered from the immediate base up to <c>object</c>. Quantifiers
///     (<c>Any</c> / <c>All</c> / <c>None</c>) apply across the per-step stream.
/// </summary>
/// <remarks>
///     Identity breadcrumb is <c>(TypeFqn, Depth, RootOrigin, Origin)</c>.
///     <see cref="RootOrigin"/> disambiguates the same (Fqn, Depth) pair when the chain
///     is navigated from two different starting types in the same compilation.
///     <see cref="Symbol"/> is excluded from equality.
/// </remarks>
public readonly struct BaseTypeChainFocus : IEquatable<BaseTypeChainFocus>
{
    /// <summary>The base type at this chain step. Excluded from equality.</summary>
    public INamedTypeSymbol Symbol { get; }

    /// <summary>Fully-qualified name of the base type at this step (interned).</summary>
    public string TypeFqn { get; }

    /// <summary>Depth above the starting type. <c>0</c> = immediate base, <c>1</c> = grandparent, …</summary>
    public int Depth { get; }

    /// <summary>Source span of the chain's starting type — disambiguates identical chains from different roots.</summary>
    public LocationInfo? RootOrigin { get; }

    /// <summary>
    ///     Source span of this chain step. Resolved from the base-list entry when
    ///     present; otherwise from the base type's own declaration. The cache-stable
    ///     breadcrumb for this focus.
    /// </summary>
    public LocationInfo? Origin { get; }

    public BaseTypeChainFocus(
        INamedTypeSymbol symbol,
        string typeFqn,
        int depth,
        LocationInfo? rootOrigin,
        LocationInfo? origin)
    {
        Symbol = symbol;
        TypeFqn = typeFqn;
        Depth = depth;
        RootOrigin = rootOrigin;
        Origin = origin;
    }

    public bool Equals(BaseTypeChainFocus other) =>
        string.Equals(TypeFqn, other.TypeFqn, StringComparison.Ordinal)
        && Depth == other.Depth
        && Nullable.Equals(RootOrigin, other.RootOrigin)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is BaseTypeChainFocus other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TypeFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(TypeFqn);
            hash = (hash * 397) ^ Depth;
            hash = (hash * 397) ^ (RootOrigin?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(BaseTypeChainFocus left, BaseTypeChainFocus right) => left.Equals(right);

    public static bool operator !=(BaseTypeChainFocus left, BaseTypeChainFocus right) => !left.Equals(right);
}
