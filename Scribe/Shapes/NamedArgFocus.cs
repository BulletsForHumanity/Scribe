using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Equatable identity for a named argument on an attribute application — the
///     <c>Bar = 42</c> in <c>[Foo(Bar = 42)]</c>. Produced by the
///     <c>NamedArg&lt;T&gt;(name)</c> lens.
/// </summary>
/// <typeparam name="T">The CLR type of the unwrapped argument value. Must be <c>IEquatable</c>-safe for cache correctness.</typeparam>
/// <remarks>
///     Identity breadcrumb is <c>(OwnerFqn, Name, Value, Origin)</c>. The raw
///     <see cref="TypedConstant"/> is carried for in-flight use but excluded from equality.
/// </remarks>
public readonly struct NamedArgFocus<T> : IEquatable<NamedArgFocus<T>>
{
    /// <summary>The raw typed-constant from the attribute application. Excluded from equality.</summary>
    public TypedConstant TypedConstant { get; }

    /// <summary>The coerced argument value.</summary>
    public T? Value { get; }

    /// <summary>The argument name as written at the attribute site.</summary>
    public string Name { get; }

    /// <summary>Owner breadcrumb: attribute class FQN plus the FQN of the symbol the attribute is applied to.</summary>
    public string OwnerFqn { get; }

    /// <summary>Source span of the <c>name = value</c> argument — the cache-stable breadcrumb.</summary>
    public LocationInfo? Origin { get; }

    public NamedArgFocus(
        TypedConstant typedConstant,
        T? value,
        string name,
        string ownerFqn,
        LocationInfo? origin)
    {
        TypedConstant = typedConstant;
        Value = value;
        Name = name;
        OwnerFqn = ownerFqn;
        Origin = origin;
    }

    public bool Equals(NamedArgFocus<T> other) =>
        EqualityComparer<T?>.Default.Equals(Value, other.Value)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(OwnerFqn, other.OwnerFqn, StringComparison.Ordinal)
        && Nullable.Equals(Origin, other.Origin);

    public override bool Equals(object? obj) => obj is NamedArgFocus<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Value is null ? 0 : EqualityComparer<T?>.Default.GetHashCode(Value);
            hash = (hash * 397) ^ (Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name));
            hash = (hash * 397) ^ (OwnerFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerFqn));
            hash = (hash * 397) ^ (Origin?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(NamedArgFocus<T> left, NamedArgFocus<T> right) => left.Equals(right);

    public static bool operator !=(NamedArgFocus<T> left, NamedArgFocus<T> right) => !left.Equals(right);
}
