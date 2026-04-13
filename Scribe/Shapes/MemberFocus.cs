using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for a member of a type — field, property, method, or event.
///     Produced by the <c>Members(filter?)</c> lens.
/// </summary>
/// <remarks>
///     Identity breadcrumb is <c>(MemberFqn, OwnerFqn, Origin)</c>. The member kind is
///     carried for filter convenience but not part of equality — the FQN already
///     discriminates members of different kinds.
///     <see cref="Symbol"/> is excluded from equality.
/// </remarks>
public readonly struct MemberFocus : IEquatable<MemberFocus>
{
    /// <summary>The member symbol. Excluded from equality — do not use for caching.</summary>
    public ISymbol Symbol { get; }

    /// <summary>Fully-qualified name of the member (interned).</summary>
    public string MemberFqn { get; }

    /// <summary>Fully-qualified name of the containing type.</summary>
    public string OwnerFqn { get; }

    /// <summary>The member's <see cref="SymbolKind"/> — field, property, method, or event. Carried for filter convenience, not part of equality.</summary>
    public SymbolKind Kind { get; }

    /// <summary>Source span of the member declaration — the cache-stable breadcrumb.</summary>
    public LocationInfo? Origin { get; }

    public MemberFocus(
        ISymbol symbol,
        string memberFqn,
        string ownerFqn,
        SymbolKind kind,
        LocationInfo? origin)
    {
        Symbol = symbol;
        MemberFqn = memberFqn;
        OwnerFqn = ownerFqn;
        Kind = kind;
        Origin = origin;
    }

    public bool Equals(MemberFocus other) =>
        string.Equals(MemberFqn, other.MemberFqn, StringComparison.Ordinal)
        && string.Equals(OwnerFqn, other.OwnerFqn, StringComparison.Ordinal)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is MemberFocus other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = MemberFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(MemberFqn);
            hash = (hash * 397) ^ (OwnerFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerFqn));
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(MemberFocus left, MemberFocus right) => left.Equals(right);

    public static bool operator !=(MemberFocus left, MemberFocus right) => !left.Equals(right);
}
