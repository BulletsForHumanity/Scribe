using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for a positional constructor argument on an attribute
///     application. Produced by the <c>ConstructorArg&lt;T&gt;(index)</c> lens.
/// </summary>
/// <typeparam name="T">The CLR type of the unwrapped argument value. Must be <c>IEquatable</c>-safe for cache correctness.</typeparam>
/// <remarks>
///     <para>
///         The lens produces a focus only when the argument at the given index can be
///         coerced to <typeparamref name="T"/>. Otherwise the lens yields no focus and
///         sub-predicates silently pass.
///     </para>
///     <para>
///         Identity breadcrumb is <c>(OwnerFqn, Index, Value, Origin)</c>. Constant values
///         hash cheaply; <see cref="TypedConstant"/> is excluded from equality.
///     </para>
/// </remarks>
public readonly struct ConstructorArgFocus<T> : IEquatable<ConstructorArgFocus<T>>
{
    /// <summary>The raw typed-constant from the attribute application. Excluded from equality.</summary>
    public TypedConstant TypedConstant { get; }

    /// <summary>The coerced argument value.</summary>
    public T? Value { get; }

    /// <summary>Zero-based index of the positional argument.</summary>
    public int Index { get; }

    /// <summary>Owner breadcrumb: attribute class FQN plus the FQN of the symbol the attribute is applied to.</summary>
    public string OwnerFqn { get; }

    /// <summary>Source span of the argument expression — the cache-stable breadcrumb.</summary>
    public LocationInfo? Origin { get; }

    public ConstructorArgFocus(
        TypedConstant typedConstant,
        T? value,
        int index,
        string ownerFqn,
        LocationInfo? origin)
    {
        TypedConstant = typedConstant;
        Value = value;
        Index = index;
        OwnerFqn = ownerFqn;
        Origin = origin;
    }

    public bool Equals(ConstructorArgFocus<T> other) =>
        EqualityComparer<T?>.Default.Equals(Value, other.Value)
        && Index == other.Index
        && string.Equals(OwnerFqn, other.OwnerFqn, StringComparison.Ordinal)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is ConstructorArgFocus<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Value is null ? 0 : EqualityComparer<T?>.Default.GetHashCode(Value);
            hash = (hash * 397) ^ Index;
            hash = (hash * 397) ^ (OwnerFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerFqn));
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(ConstructorArgFocus<T> left, ConstructorArgFocus<T> right) => left.Equals(right);

    public static bool operator !=(ConstructorArgFocus<T> left, ConstructorArgFocus<T> right) => !left.Equals(right);
}
