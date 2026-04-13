using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for a type being matched by a <see cref="TypeShape"/>. Pairs
///     the matched <see cref="INamedTypeSymbol"/> with a cache-stable breadcrumb
///     (fully-qualified name + declaration location) so the incremental pipeline can
///     discriminate rows without relying on the non-stable symbol reference.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Symbol"/> is carried so in-flight predicates and projections can
///         read from it, but it is <em>not</em> part of equality. Roslyn recomputes
///         <see cref="INamedTypeSymbol"/> instances across compilations — using the
///         symbol reference as a cache key would invalidate every downstream step on
///         every change. Equality uses <see cref="Fqn"/> and <see cref="Origin"/>,
///         which are stable.
///     </para>
/// </remarks>
public readonly struct TypeFocus : IEquatable<TypeFocus>
{
    /// <summary>The matched type symbol. Excluded from equality — do not rely on it for caching.</summary>
    public INamedTypeSymbol Symbol { get; }

    /// <summary>Fully-qualified display name of the matched type (interned).</summary>
    public string Fqn { get; }

    /// <summary>Declaration location — the cache-stable breadcrumb used for equality.</summary>
    public LocationInfo? Origin { get; }

    public TypeFocus(INamedTypeSymbol symbol, string fqn, LocationInfo? origin)
    {
        Symbol = symbol;
        Fqn = fqn;
        Origin = origin;
    }

    public bool Equals(TypeFocus other) =>
        string.Equals(Fqn, other.Fqn, StringComparison.Ordinal)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is TypeFocus other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Fqn is null ? 0 : StringComparer.Ordinal.GetHashCode(Fqn);
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(TypeFocus left, TypeFocus right) => left.Equals(right);

    public static bool operator !=(TypeFocus left, TypeFocus right) => !left.Equals(right);
}
