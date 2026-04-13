using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Fluent lens entry points on <see cref="FocusShape{TypeFocus}"/>. These extensions
///     mirror the type-level navigation surface exposed on <see cref="TypeShape"/> —
///     <c>Attributes</c>, <c>Members</c>, <c>BaseTypeChain</c> — so nested
///     <see cref="FocusShape{TypeFocus}"/> sub-shapes (e.g. obtained via
///     <c>AsTypeShape()</c>) can compose identically to a root type shape.
/// </summary>
public static class TypeFocusShapeExtensions
{
    /// <summary>
    ///     Enter the attributes lens: navigate every attribute application matching
    ///     <paramref name="attributeFqn"/>. Mirrors
    ///     <see cref="TypeShape.Attributes"/>.
    /// </summary>
    public static FocusShape<TypeFocus> Attributes(
        this FocusShape<TypeFocus> shape,
        string attributeFqn,
        Action<FocusShape<AttributeFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (string.IsNullOrEmpty(attributeFqn))
        {
            throw new ArgumentException("Attribute FQN must not be empty.", nameof(attributeFqn));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<AttributeFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.Attributes(attributeFqn);

        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE050",
            defaultTitle: "Attribute presence constraint",
            defaultMessage: "Expected [{0}..{1}] applications of attribute '"
                + attributeFqn + "', observed {2}");

        var quantifierDescriptor = TypeShape.BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE092" : "SCRIBE093",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one application of attribute '" + attributeFqn + "' must satisfy the required checks"
                : "No application of attribute '" + attributeFqn + "' may satisfy the disallowed checks");

        shape.AddBranch(new LensBranch<TypeFocus, AttributeFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor));

        return shape;
    }

    /// <summary>
    ///     Enter the members lens. Mirrors <see cref="TypeShape.Members"/>.
    /// </summary>
    public static FocusShape<TypeFocus> Members(
        this FocusShape<TypeFocus> shape,
        SymbolKind? kind = null,
        Action<FocusShape<MemberFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<MemberFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.Members(kind);

        var kindDescription = kind?.ToString() ?? "any kind";
        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE051",
            defaultTitle: "Member presence constraint",
            defaultMessage: "Expected [{0}..{1}] members of " + kindDescription + ", observed {2}");

        var quantifierDescriptor = TypeShape.BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE090" : "SCRIBE091",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one member of " + kindDescription + " must satisfy the required checks"
                : "No member of " + kindDescription + " may satisfy the disallowed checks");

        shape.AddBranch(new LensBranch<TypeFocus, MemberFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor));

        return shape;
    }

    /// <summary>
    ///     Enter the base-type-chain lens. Mirrors <see cref="TypeShape.BaseTypeChain"/>.
    /// </summary>
    public static FocusShape<TypeFocus> BaseTypeChain(
        this FocusShape<TypeFocus> shape,
        Action<FocusShape<BaseTypeChainFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<BaseTypeChainFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.BaseTypeChain();

        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE052",
            defaultTitle: "Base-type-chain length constraint",
            defaultMessage: "Expected [{0}..{1}] base-type-chain steps, observed {2}");

        var quantifierDescriptor = TypeShape.BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE094" : "SCRIBE095",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one base-type-chain step must satisfy the required checks"
                : "No base-type-chain step may satisfy the disallowed checks");

        shape.AddBranch(new LensBranch<TypeFocus, BaseTypeChainFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor));

        return shape;
    }

    private static void ValidateCounts(int min, int? max)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum count must be non-negative.");
        }

        if (max is { } m && m < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum count must be at least the minimum.");
        }
    }

    private static LensPresenceSpec? BuildPresence(
        int min,
        int? max,
        DiagnosticSpec? spec,
        string defaultId,
        string defaultTitle,
        string defaultMessage)
    {
        var hasPresence = min > 0 || max is not null || spec is not null;
        if (!hasPresence)
        {
            return null;
        }

        return new LensPresenceSpec(
            Id: InternPool.Intern(spec?.Id ?? defaultId),
            Title: spec?.Title ?? defaultTitle,
            MessageFormat: spec?.Message ?? defaultMessage,
            Severity: spec?.Severity ?? DiagnosticSeverity.Error);
    }
}
