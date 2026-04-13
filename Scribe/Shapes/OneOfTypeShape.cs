using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Terminal authoring node representing a disjunction of two or more
///     <see cref="TypeShape"/> alternatives. The resulting <see cref="Shape{TModel}"/>
///     passes a given symbol iff <em>at least one</em> alternative's check tree
///     (kind filter, leaf checks, lens branches) produces zero violations against
///     it. When every alternative fails, a single fused diagnostic is emitted at
///     the symbol's primary location listing each branch's unsatisfied checks.
/// </summary>
/// <remarks>
///     <para>
///         Obtained from <see cref="Stencil.OneOf(TypeShape[])"/> or
///         <see cref="TypeShape.OneOf(TypeShape[])"/>. Only <see cref="Etch{TModel}"/>
///         is exposed: an already-built disjunction cannot accumulate further
///         lenses or checks, since those would be ambiguous across branches. To
///         add more constraints, add them to the alternative(s) before calling
///         <c>OneOf</c>.
///     </para>
///     <para>
///         <b>v1 constraints.</b> All alternatives must share the same primary
///         attribute-driving metadata name (or none). Member-check alternatives
///         are rejected for v1 — those live on the alternative's
///         <see cref="TypeShape"/> instances but are not re-entered by the fused
///         branch runner. Both restrictions can be lifted in follow-up work.
///     </para>
/// </remarks>
public sealed class OneOfTypeShape
{
    private readonly TypeShape[] _alternatives;
    private readonly DiagnosticSpec? _fusionSpec;

    internal OneOfTypeShape(TypeShape[] alternatives, DiagnosticSpec? fusionSpec)
    {
        if (alternatives is null)
        {
            throw new ArgumentNullException(nameof(alternatives));
        }

        if (alternatives.Length < 2)
        {
            throw new ArgumentException("OneOf requires at least two alternatives.", nameof(alternatives));
        }

        foreach (var alt in alternatives)
        {
            if (alt is null)
            {
                throw new ArgumentException("OneOf alternatives must be non-null.", nameof(alternatives));
            }

            if (alt.MemberChecks.Count > 0)
            {
                throw new ArgumentException(
                    "OneOf alternatives cannot declare member checks (v1 restriction). "
                    + "Move the check onto a Members(...) lens branch instead.",
                    nameof(alternatives));
            }
        }

        var primaryAttr = alternatives[0].PrimaryAttributeMetadataName;
        var primaryIface = alternatives[0].PrimaryInterfaceMetadataName;
        foreach (var alt in alternatives)
        {
            if (!string.Equals(alt.PrimaryAttributeMetadataName, primaryAttr, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "All OneOf alternatives must share the same primary attribute driver (or have none).",
                    nameof(alternatives));
            }

            if (!string.Equals(alt.PrimaryInterfaceMetadataName, primaryIface, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "All OneOf alternatives must share the same primary interface driver (or have none).",
                    nameof(alternatives));
            }
        }

        _alternatives = alternatives;
        _fusionSpec = fusionSpec;
    }

    internal IReadOnlyList<TypeShape> Alternatives => _alternatives;

    internal DiagnosticSpec? FusionSpec => _fusionSpec;

    /// <summary>
    ///     Etch the disjunction into a sealed <see cref="Shape{TModel}"/>. The
    ///     <paramref name="etch"/> callback runs on any symbol matched by at least
    ///     one alternative's kind filter and driving attribute/interface.
    /// </summary>
    public Shape<TModel> Etch<TModel>(EtchDelegate<TModel> etch)
        where TModel : IEquatable<TModel>
    {
        if (etch is null)
        {
            throw new ArgumentNullException(nameof(etch));
        }

        var branches = _alternatives.Select(a => new OneOfBranch(
                Kind: a.Kind,
                Checks: a.Checks.ToArray(),
                LensBranches: a.LensBranches.ToArray()))
            .ToArray();

        var fusion = new LensQuantifierSpec(
            Id: InternPool.Intern(_fusionSpec?.Id ?? "SCRIBE100"),
            Title: _fusionSpec?.Title ?? "Disjunction (OneOf) — no alternative satisfied",
            MessageFormat: _fusionSpec?.Message ?? "None of the {0} alternatives passed: {1}",
            Severity: _fusionSpec?.Severity ?? DiagnosticSeverity.Error);

        return new Shape<TModel>(
            alternatives: branches,
            primaryAttributeMetadataName: _alternatives[0].PrimaryAttributeMetadataName,
            primaryInterfaceMetadataName: _alternatives[0].PrimaryInterfaceMetadataName,
            fusionSpec: fusion,
            etch: etch);
    }
}

/// <summary>
///     Flattened per-alternative payload used by the OneOf evaluator. Captures the
///     alternative's kind filter and its full check tree so each branch can be
///     evaluated independently against a candidate symbol.
/// </summary>
internal sealed record OneOfBranch(
    TypeKindFilter Kind,
    ShapeCheck[] Checks,
    ILensBranch<TypeFocus>[] LensBranches);
