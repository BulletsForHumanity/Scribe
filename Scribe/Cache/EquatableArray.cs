using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Scribe.Cache;

/// <summary>
///     An <see cref="ImmutableArray{T}"/> that participates in structural, element-wise
///     value equality — the form required by the Roslyn incremental-generator cache.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ImmutableArray{T}"/> is a struct wrapping a <c>T[]</c>; its built-in
///         <see cref="object.Equals(object)"/> compares the underlying array <em>reference</em>,
///         not its contents. Any value that flows through an
///         <c>IncrementalValuesProvider&lt;T&gt;</c> must compare by value, otherwise the
///         cache is invalidated on every compilation and the generator runs on every keystroke.
///     </para>
///     <para>
///         <see cref="EquatableArray{T}"/> wraps <see cref="ImmutableArray{T}"/>, computes a
///         deterministic element-wise hash once at construction time (cached in a field so
///         <see cref="GetHashCode"/> is a constant-time field read), and overrides
///         <see cref="Equals(EquatableArray{T})"/> to compare element-by-element via
///         <see cref="System.MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>.
///     </para>
///     <para>
///         <c>default(EquatableArray&lt;T&gt;)</c> is normalised to an empty array (same hash,
///         equal to <see cref="Empty"/>) — callers never need to null-check before use.
///     </para>
/// </remarks>
/// <typeparam name="T">Element type. Must implement <see cref="IEquatable{T}"/>.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    /// <summary>Canonical empty instance. Equal to <c>default(EquatableArray&lt;T&gt;)</c>.</summary>
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _array;
    private readonly int _hash;

    /// <summary>Wrap an existing <see cref="ImmutableArray{T}"/>.</summary>
    /// <param name="array">Source array. <c>default</c> and empty are treated identically.</param>
    public EquatableArray(ImmutableArray<T> array)
    {
        if (array.IsDefaultOrEmpty)
        {
            _array = ImmutableArray<T>.Empty;
            _hash = EmptyHash;
        }
        else
        {
            _array = array;
            _hash = ComputeHash(array);
        }
    }

    /// <summary>Number of elements.</summary>
    public int Count => _array.IsDefault ? 0 : _array.Length;

    /// <summary><see langword="true"/> if there are zero elements.</summary>
    public bool IsEmpty => _array.IsDefaultOrEmpty;

    /// <summary>Element at the given zero-based index.</summary>
    public T this[int index] => _array[index];

    /// <summary>Allocation-free read-only span over the elements.</summary>
    public ReadOnlySpan<T> AsSpan() => _array.IsDefault ? [] : _array.AsSpan();

    /// <summary>Structural, element-wise equality.</summary>
    public bool Equals(EquatableArray<T> other)
    {
        if (_hash != other._hash)
        {
            return false;
        }

        var left = AsSpan();
        var right = other.AsSpan();
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hash;

    /// <summary>Enumerate elements.</summary>
    public IEnumerator<T> GetEnumerator() =>
        _array.IsDefault
            ? Enumerable.Empty<T>().GetEnumerator()
            : ((IEnumerable<T>)_array).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Structural equality operator.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Structural inequality operator.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>Implicit conversion from <see cref="ImmutableArray{T}"/>.</summary>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    // ─── internal ─────────────────────────────────────────────────────────────

    private const int EmptyHash = 0;

    private static int ComputeHash(ImmutableArray<T> array)
    {
        // FNV-1a over element hashes. Deterministic, no System.HashCode dependency
        // (not guaranteed on netstandard2.0).
        const uint seed = 2166136261u;
        const uint step = 16777619u;
        var hash = seed;
        foreach (var item in array)
        {
            var itemHash = (uint)(item?.GetHashCode() ?? 0);
            hash = unchecked((hash ^ itemHash) * step);
        }

        return unchecked((int)hash);
    }
}

/// <summary>Factory helpers for <see cref="EquatableArray{T}"/>.</summary>
public static class EquatableArray
{
    /// <summary>Create from an existing <see cref="ImmutableArray{T}"/>.</summary>
    public static EquatableArray<T> Create<T>(ImmutableArray<T> source)
        where T : IEquatable<T> => new(source);

    /// <summary>Create from a params array.</summary>
    public static EquatableArray<T> Create<T>(params T[] items)
        where T : IEquatable<T> =>
        items is null || items.Length == 0
            ? EquatableArray<T>.Empty
            : new EquatableArray<T>(ImmutableArray.Create(items));

    /// <summary>Create from an arbitrary enumerable.</summary>
    public static EquatableArray<T> From<T>(IEnumerable<T> source)
        where T : IEquatable<T>
    {
        if (source is null)
        {
            return EquatableArray<T>.Empty;
        }

        if (source is ImmutableArray<T> immutable)
        {
            return new EquatableArray<T>(immutable);
        }

        return new EquatableArray<T>(source.ToImmutableArray());
    }
}
