using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Join two shape streams on a key. Produces a matched-pair stream plus an
///     opt-in diagnostic stream for orphaned sides. Orphans are silent by default —
///     the pair is a query, not a requirement — so the author must declare
///     what missing means by calling <see cref="PairBuilder{TLeft, TRight}.RequireLeftHasRight"/>
///     or <see cref="PairBuilder{TLeft, TRight}.WarnOnRightUnused"/>.
/// </summary>
public static class Relation
{
    /// <summary>
    ///     Join <paramref name="left"/> and <paramref name="right"/> by equal keys.
    ///     The returned builder lazily wires matched-pair and diagnostic providers
    ///     when their properties are first accessed.
    /// </summary>
    public static PairBuilder<TLeft, TRight> Pair<TLeft, TRight>(
        IncrementalValuesProvider<ShapedSymbol<TLeft>> left,
        IncrementalValuesProvider<ShapedSymbol<TRight>> right,
        Func<TLeft, string> leftKey,
        Func<TRight, string> rightKey)
        where TLeft : IEquatable<TLeft>
        where TRight : IEquatable<TRight>
        => new(left, right, leftKey, rightKey);
}

/// <summary>
///     Configuration surface for a <see cref="Relation.Pair{TLeft, TRight}"/> join.
///     Each orphan policy returns <c>this</c> so the declaration reads left-to-right.
/// </summary>
public sealed class PairBuilder<TLeft, TRight>
    where TLeft : IEquatable<TLeft>
    where TRight : IEquatable<TRight>
{
    private readonly IncrementalValuesProvider<ShapedSymbol<TLeft>> _left;
    private readonly IncrementalValuesProvider<ShapedSymbol<TRight>> _right;
    private readonly Func<TLeft, string> _leftKey;
    private readonly Func<TRight, string> _rightKey;

    private OrphanPolicy? _leftHasRight;
    private OrphanPolicy? _rightUnused;

    internal PairBuilder(
        IncrementalValuesProvider<ShapedSymbol<TLeft>> left,
        IncrementalValuesProvider<ShapedSymbol<TRight>> right,
        Func<TLeft, string> leftKey,
        Func<TRight, string> rightKey)
    {
        _left = left;
        _right = right;
        _leftKey = leftKey;
        _rightKey = rightKey;
    }

    /// <summary>
    ///     Emit a diagnostic for every left-side item whose key matches no right-side
    ///     item. The message receives <c>{0} = left.Fqn</c> and <c>{1} = missing key</c>.
    /// </summary>
    public PairBuilder<TLeft, TRight> RequireLeftHasRight(
        string id,
        string title,
        string messageFormat,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _leftHasRight = new OrphanPolicy(id, title, messageFormat, severity);
        return this;
    }

    /// <summary>
    ///     Emit a diagnostic for every right-side item whose key is not referenced by
    ///     any left-side item. The message receives <c>{0} = right.Fqn</c>.
    /// </summary>
    public PairBuilder<TLeft, TRight> WarnOnRightUnused(
        string id,
        string title,
        string messageFormat,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning)
    {
        _rightUnused = new OrphanPolicy(id, title, messageFormat, severity);
        return this;
    }

    /// <summary>
    ///     Stream of successful joins. Each left item is paired with the first
    ///     right-side item sharing its key; duplicate right-side keys drop to first-
    ///     wins in v1 (a <c>RequireUniqueRightKey</c> overload may be added later).
    /// </summary>
    public IncrementalValuesProvider<ShapedPair<TLeft, TRight>> Matched
    {
        get
        {
            var leftKey = _leftKey;
            var rightKey = _rightKey;

            var rightByKey = _right
                .Collect()
                .Select((arr, _) => BuildRightIndex(arr, rightKey));

            return _left
                .Combine(rightByKey)
                .Select((tuple, _) => TryPair(tuple.Left, tuple.Right, leftKey))
                .Where(p => p.HasValue)
                .Select((p, _) => p!.Value);
        }
    }

    /// <summary>
    ///     Stream of orphan diagnostics. Empty unless
    ///     <see cref="RequireLeftHasRight"/> or <see cref="WarnOnRightUnused"/> was
    ///     configured. Pair with <see cref="Descriptors"/> when reporting, or call
    ///     <see cref="RegisterDiagnostics"/> to wire everything up in one line.
    /// </summary>
    public IncrementalValuesProvider<DiagnosticInfo> Diagnostics
    {
        get
        {
            var leftKey = _leftKey;
            var rightKey = _rightKey;
            var leftPolicy = _leftHasRight;
            var rightPolicy = _rightUnused;

            var rightByKey = _right
                .Collect()
                .Select((arr, _) => BuildRightIndex(arr, rightKey));

            var leftOrphans = _left
                .Combine(rightByKey)
                .Select((tuple, _) => FindLeftOrphan(tuple.Left, tuple.Right, leftKey, leftPolicy))
                .Where(d => d.HasValue)
                .Select((d, _) => d!.Value);

            var leftKeys = _left
                .Collect()
                .Select((arr, _) => BuildLeftKeySet(arr, leftKey));

            var rightOrphans = _right
                .Combine(leftKeys)
                .Select((tuple, _) => FindRightOrphan(tuple.Left, tuple.Right, rightKey, rightPolicy))
                .Where(d => d.HasValue)
                .Select((d, _) => d!.Value);

            return leftOrphans.Collect()
                .Combine(rightOrphans.Collect())
                .SelectMany((pair, _) => Concat(pair.Left, pair.Right));
        }
    }

    /// <summary>
    ///     One descriptor per configured orphan policy, keyed by diagnostic id.
    ///     Consumer-side analyzers/generators must include these in their
    ///     <c>SupportedDiagnostics</c> when reporting.
    /// </summary>
    public ImmutableArray<DiagnosticDescriptor> Descriptors
    {
        get
        {
            var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>(2);
            if (_leftHasRight is { } l)
            {
                builder.Add(BuildDescriptor(l));
            }

            if (_rightUnused is { } r)
            {
                builder.Add(BuildDescriptor(r));
            }

            return builder.ToImmutable();
        }
    }

    /// <summary>
    ///     One-liner: materialize every orphan <see cref="DiagnosticInfo"/> with the
    ///     matching descriptor and report it. Call from
    ///     <c>IncrementalGenerator.Initialize</c>.
    /// </summary>
    public void RegisterDiagnostics(IncrementalGeneratorInitializationContext context)
    {
        var descriptors = Descriptors;
        if (descriptors.IsEmpty)
        {
            return;
        }

        context.RegisterSourceOutput(Diagnostics, (spc, info) =>
        {
            foreach (var descriptor in descriptors)
            {
                if (string.Equals(descriptor.Id, info.Id, StringComparison.Ordinal))
                {
                    spc.ReportDiagnostic(info.Materialize(descriptor));
                    return;
                }
            }
        });
    }

    private static DiagnosticDescriptor BuildDescriptor(OrphanPolicy policy) =>
        new(
            id: policy.Id,
            title: policy.Title,
            messageFormat: policy.MessageFormat,
            category: "Scribe.Relation",
            defaultSeverity: policy.Severity,
            isEnabledByDefault: true);

    private static IEnumerable<DiagnosticInfo> Concat(
        ImmutableArray<DiagnosticInfo> a,
        ImmutableArray<DiagnosticInfo> b)
    {
        foreach (var d in a)
        {
            yield return d;
        }

        foreach (var d in b)
        {
            yield return d;
        }
    }

    private static ImmutableDictionary<string, ShapedSymbol<TRight>> BuildRightIndex(
        ImmutableArray<ShapedSymbol<TRight>> rights,
        Func<TRight, string> keyOf)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ShapedSymbol<TRight>>(StringComparer.Ordinal);
        foreach (var right in rights)
        {
            var key = keyOf(right.Model);
            if (key is null)
            {
                continue;
            }

            if (!builder.ContainsKey(key))
            {
                builder.Add(key, right);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildLeftKeySet(
        ImmutableArray<ShapedSymbol<TLeft>> lefts,
        Func<TLeft, string> keyOf)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var left in lefts)
        {
            var key = keyOf(left.Model);
            if (key is null)
            {
                continue;
            }

            builder.Add(key);
        }

        return builder.ToImmutable();
    }

    private static ShapedPair<TLeft, TRight>? TryPair(
        ShapedSymbol<TLeft> left,
        ImmutableDictionary<string, ShapedSymbol<TRight>> rightByKey,
        Func<TLeft, string> leftKey)
    {
        var key = leftKey(left.Model);
        if (key is null)
        {
            return null;
        }

        return rightByKey.TryGetValue(key, out var right)
            ? new ShapedPair<TLeft, TRight>(left, right)
            : null;
    }

    private static DiagnosticInfo? FindLeftOrphan(
        ShapedSymbol<TLeft> left,
        ImmutableDictionary<string, ShapedSymbol<TRight>> rightByKey,
        Func<TLeft, string> leftKey,
        OrphanPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        var key = leftKey(left.Model);
        if (key is null || rightByKey.ContainsKey(key))
        {
            return null;
        }

        return new DiagnosticInfo(
            Id: policy.Value.Id,
            Severity: policy.Value.Severity,
            MessageArgs: EquatableArray.Create(left.Fqn, key),
            Location: left.Location);
    }

    private static DiagnosticInfo? FindRightOrphan(
        ShapedSymbol<TRight> right,
        ImmutableHashSet<string> leftKeys,
        Func<TRight, string> rightKey,
        OrphanPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        var key = rightKey(right.Model);
        if (key is null || leftKeys.Contains(key))
        {
            return null;
        }

        return new DiagnosticInfo(
            Id: policy.Value.Id,
            Severity: policy.Value.Severity,
            MessageArgs: EquatableArray.Create(right.Fqn),
            Location: right.Location);
    }

    private readonly record struct OrphanPolicy(
        string Id,
        string Title,
        string MessageFormat,
        DiagnosticSeverity Severity);
}
