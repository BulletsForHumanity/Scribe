using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Non-generic tree node in a focus-stream evaluation plan. A parent
///     <see cref="FocusShape{TFocus}"/> (or <see cref="TypeShape"/>) holds a list of
///     branches; each branch binds a <see cref="Lens{TSource, TTarget}"/> to a nested
///     <see cref="FocusShape{TChild}"/>. At evaluation time, the branch navigates
///     the lens, applies per-child checks, and recurses into its own sub-branches.
/// </summary>
/// <typeparam name="TParent">The focus type flowing into the lens.</typeparam>
internal interface ILensBranch<in TParent>
{
    /// <summary>
    ///     Evaluate this branch against the given parent focus, appending any
    ///     <see cref="DiagnosticInfo"/> violations to <paramref name="violations"/>.
    /// </summary>
    /// <param name="parent">Parent focus flowing into the lens.</param>
    /// <param name="compilation">Current compilation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="violations">Destination list for any produced violations.</param>
    /// <param name="parentPath">
    ///     B.8 — accumulated focus-path breadcrumb from enclosing lenses. Branches
    ///     append their own <c>HopDescription</c> and stamp it onto every
    ///     <c>DiagnosticInfo.FocusPath</c> they produce. <see langword="null"/>
    ///     at the root.
    /// </param>
    void Evaluate(
        TParent parent,
        Compilation compilation,
        CancellationToken ct,
        List<DiagnosticInfo> violations,
        string? parentPath = null);

    /// <summary>
    ///     Emit every diagnostic descriptor this branch (and its nested subtree) can produce.
    ///     Used by <see cref="Shape{TModel}.ToAnalyzer"/> to populate
    ///     <see cref="Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer.SupportedDiagnostics"/>.
    /// </summary>
    IEnumerable<(string Id, string Title, string MessageFormat, DiagnosticSeverity Severity)> CollectDescriptors();
}

/// <summary>
///     Concrete lens branch: navigate from <typeparamref name="TParent"/> to
///     <typeparamref name="TChild"/> via <see cref="Lens"/>, then evaluate the
///     nested <see cref="FocusShape{TChild}"/> against each produced child.
/// </summary>
/// <param name="Lens">Navigation + smudge primitive.</param>
/// <param name="Nested">Authoring-time shape describing checks on the child focus.</param>
/// <param name="MinCount">Minimum number of navigated children required. <c>0</c> disables the minimum check.</param>
/// <param name="MaxCount">Maximum number of navigated children allowed. <see langword="null"/> disables the maximum check.</param>
/// <param name="Presence">Descriptor metadata for the presence-violation diagnostic emitted when <see cref="MinCount"/> / <see cref="MaxCount"/> are breached. <see langword="null"/> means no presence constraint.</param>
/// <param name="ParentOrigin">Locator for the parent focus's span — used as the diagnostic target when the child set is empty and no child span exists to squiggle.</param>
/// <param name="Quantifier">How nested-check results are aggregated across children. <see cref="Shapes.Quantifier.All"/> (default) preserves pre-quantifier behaviour: per-child violations flush directly to the parent list.</param>
/// <param name="QuantifierSpec">Descriptor metadata for the aggregate diagnostic emitted by <see cref="Shapes.Quantifier.Any"/> / <see cref="Shapes.Quantifier.None"/>. Required when <see cref="Quantifier"/> is not <see cref="Shapes.Quantifier.All"/>.</param>
/// <param name="HopDescription">B.8 — human-readable name of this navigation step (e.g. <c>Attributes("Foo")</c>), folded into child <c>DiagnosticInfo.FocusPath</c> so violations show which lens hops produced them. <see langword="null"/> omits this hop from the path.</param>
internal sealed record LensBranch<TParent, TChild>(
    Lens<TParent, TChild> Lens,
    FocusShape<TChild> Nested,
    int MinCount,
    int? MaxCount,
    LensPresenceSpec? Presence,
    Func<TParent, LocationInfo?> ParentOrigin,
    Quantifier Quantifier = Quantifier.All,
    LensQuantifierSpec? QuantifierSpec = null,
    string? HopDescription = null) : ILensBranch<TParent>
    where TChild : IEquatable<TChild>
{
    public void Evaluate(
        TParent parent,
        Compilation compilation,
        CancellationToken ct,
        List<DiagnosticInfo> violations,
        string? parentPath = null)
    {
        ct.ThrowIfCancellationRequested();

        var path = ComposePath(parentPath);

        var children = new List<TChild>();
        foreach (var child in Lens.Navigate(parent))
        {
            children.Add(child);
        }

        if (Presence is { } presence)
        {
            var outOfRange =
                children.Count < MinCount
                || (MaxCount is { } max && children.Count > max);
            if (outOfRange)
            {
                violations.Add(new DiagnosticInfo(
                    Id: presence.Id,
                    Severity: presence.Severity,
                    MessageArgs: EquatableArray.Create(
                        MinCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        (MaxCount?.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? "\u221e",
                        children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Location: ParentOrigin(parent),
                    FocusPath: path));
            }
        }

        switch (Quantifier)
        {
            case Quantifier.All:
                EvaluateAll(children, compilation, ct, violations, path);
                break;
            case Quantifier.Any:
                EvaluateAny(parent, children, compilation, ct, violations, path);
                break;
            case Quantifier.None:
                EvaluateNone(children, compilation, ct, violations, path);
                break;
        }
    }

    private string? ComposePath(string? parentPath)
    {
        if (HopDescription is null)
        {
            return parentPath;
        }

        return parentPath is null
            ? HopDescription
            : parentPath + " → " + HopDescription;
    }

    private void EvaluateAll(
        List<TChild> children,
        Compilation compilation,
        CancellationToken ct,
        List<DiagnosticInfo> violations,
        string? path)
    {
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var check in Nested.Checks)
            {
                ct.ThrowIfCancellationRequested();
                if (check.Predicate(child, compilation, ct))
                {
                    continue;
                }

                violations.Add(new DiagnosticInfo(
                    Id: check.Id,
                    Severity: check.Severity,
                    MessageArgs: check.MessageArgs(child),
                    Location: Lens.Smudge(child),
                    FocusPath: path));
            }

            foreach (var sub in Nested.Branches)
            {
                sub.Evaluate(child, compilation, ct, violations, path);
            }
        }
    }

    private void EvaluateAny(
        TParent parent,
        List<TChild> children,
        Compilation compilation,
        CancellationToken ct,
        List<DiagnosticInfo> violations,
        string? path)
    {
        foreach (var child in children)
        {
            if (ChildPasses(child, compilation, ct, path))
            {
                return;
            }
        }

        if (QuantifierSpec is { } spec)
        {
            violations.Add(new DiagnosticInfo(
                Id: spec.Id,
                Severity: spec.Severity,
                MessageArgs: EquatableArray<string>.Empty,
                Location: ParentOrigin(parent),
                FocusPath: path));
        }
    }

    private void EvaluateNone(
        List<TChild> children,
        Compilation compilation,
        CancellationToken ct,
        List<DiagnosticInfo> violations,
        string? path)
    {
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (!ChildPasses(child, compilation, ct, path))
            {
                continue;
            }

            if (QuantifierSpec is { } spec)
            {
                violations.Add(new DiagnosticInfo(
                    Id: spec.Id,
                    Severity: spec.Severity,
                    MessageArgs: EquatableArray<string>.Empty,
                    Location: Lens.Smudge(child),
                    FocusPath: path));
            }
        }
    }

    private bool ChildPasses(TChild child, Compilation compilation, CancellationToken ct, string? path)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var check in Nested.Checks)
        {
            if (!check.Predicate(child, compilation, ct))
            {
                return false;
            }
        }

        if (Nested.Branches.Count == 0)
        {
            return true;
        }

        var scratch = new List<DiagnosticInfo>();
        foreach (var sub in Nested.Branches)
        {
            sub.Evaluate(child, compilation, ct, scratch, path);
            if (scratch.Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    public IEnumerable<(string Id, string Title, string MessageFormat, DiagnosticSeverity Severity)> CollectDescriptors()
    {
        if (Presence is { } presence)
        {
            yield return (presence.Id, presence.Title, presence.MessageFormat, presence.Severity);
        }

        if (QuantifierSpec is { } qspec)
        {
            yield return (qspec.Id, qspec.Title, qspec.MessageFormat, qspec.Severity);
        }

        foreach (var check in Nested.Checks)
        {
            yield return (check.Id, check.Title, check.MessageFormat, check.Severity);
        }

        foreach (var sub in Nested.Branches)
        {
            foreach (var descriptor in sub.CollectDescriptors())
            {
                yield return descriptor;
            }
        }
    }
}

/// <summary>
///     Descriptor metadata for the diagnostic emitted when a lens branch's
///     <c>min</c> / <c>max</c> presence constraints are breached. Three message
///     slots: <c>{0}</c> = min, <c>{1}</c> = max (or <c>∞</c>), <c>{2}</c> = observed.
/// </summary>
internal sealed record LensPresenceSpec(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity);

/// <summary>
///     Descriptor metadata for the aggregate diagnostic emitted by a lens branch
///     whose <see cref="LensBranch{TParent, TChild}.Quantifier"/> is
///     <see cref="Shapes.Quantifier.Any"/> or <see cref="Shapes.Quantifier.None"/>.
///     No format slots — the message is a fixed string chosen at authoring time.
/// </summary>
internal sealed record LensQuantifierSpec(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity);
