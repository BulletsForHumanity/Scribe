using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for an attribute usage on a symbol (type, member, or parameter).
///     Produced by the <c>Attributes(fqn)</c> lens.
/// </summary>
/// <remarks>
///     <para>
///         The identity breadcrumb is <c>(AttributeFqn, OwnerFqn, Index, Origin)</c>. Two
///         attributes of the same class on the same owner are distinguished by their
///         positional <see cref="Index"/> — <c>[Foo][Foo]</c> on a single type yields
///         two distinct foci.
///     </para>
///     <para>
///         <see cref="Data"/> is carried for in-flight predicate evaluation but excluded
///         from equality: Roslyn's <c>AttributeData</c> is not cache-stable across
///         compilations.
///     </para>
/// </remarks>
public readonly struct AttributeFocus : IEquatable<AttributeFocus>
{
    /// <summary>The attribute data. Excluded from equality — do not use for caching.</summary>
    public AttributeData Data { get; }

    /// <summary>Fully-qualified name of the attribute class (interned).</summary>
    public string AttributeFqn { get; }

    /// <summary>Fully-qualified name of the symbol the attribute is applied to.</summary>
    public string OwnerFqn { get; }

    /// <summary>
    ///     Zero-based index of this attribute within the owner's attribute list, used to
    ///     disambiguate repeated attributes of the same class on the same owner.
    /// </summary>
    public int Index { get; }

    /// <summary>Source span of the attribute application — the cache-stable breadcrumb.</summary>
    public LocationInfo? Origin { get; }

    public AttributeFocus(
        AttributeData data,
        string attributeFqn,
        string ownerFqn,
        int index,
        LocationInfo? origin)
    {
        Data = data;
        AttributeFqn = attributeFqn;
        OwnerFqn = ownerFqn;
        Index = index;
        Origin = origin;
    }

    public bool Equals(AttributeFocus other) =>
        string.Equals(AttributeFqn, other.AttributeFqn, StringComparison.Ordinal)
        && string.Equals(OwnerFqn, other.OwnerFqn, StringComparison.Ordinal)
        && Index == other.Index
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is AttributeFocus other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = AttributeFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(AttributeFqn);
            hash = (hash * 397) ^ (OwnerFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerFqn));
            hash = (hash * 397) ^ Index;
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(AttributeFocus left, AttributeFocus right) => left.Equals(right);

    public static bool operator !=(AttributeFocus left, AttributeFocus right) => !left.Equals(right);
}
