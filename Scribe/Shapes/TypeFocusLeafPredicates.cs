using System;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Leaf-predicate extensions on <see cref="FocusShape{TypeFocus}"/>. Parallel to
///     <see cref="TypeShape"/>'s core <c>MustBe*</c> / <c>MustHave*</c> catalog but
///     targeted at a nested type-shape sub-graph — e.g. a
///     <see cref="FocusShape{TypeFocus}"/> obtained via
///     <see cref="TypeArgFocusShapeExtensions.AsTypeShape"/> or
///     <see cref="BaseTypeChainFocusShapeExtensions.AsTypeShape"/>. Diagnostic IDs match
///     the root catalog so the same descriptors are reused when both surfaces coexist.
/// </summary>
/// <remarks>
///     This is a representative subset (v1). The root <see cref="TypeShape"/> catalog is
///     larger — additional primitives will be added here as needed. Auto-fix metadata
///     (squiggle targets and <see cref="FixKind"/>) is not propagated: violations emitted
///     on a nested focus land on the lens's smudge anchor, and the code-fix layer is
///     scoped to root-level checks in v1.
/// </remarks>
public static class TypeFocusLeafPredicates
{
    /// <summary>Require the type to be declared <c>partial</c>.</summary>
    public static FocusShape<TypeFocus> MustBePartial(
        this FocusShape<TypeFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE001"),
            Title: spec?.Title ?? "Type must be partial",
            MessageFormat: spec?.Message ?? "Type '{0}' must be declared 'partial'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, ct) => TypeShape.IsPartial(focus.Symbol, ct),
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>Require the type to be declared <c>sealed</c> (or be a value / static type).</summary>
    public static FocusShape<TypeFocus> MustBeSealed(
        this FocusShape<TypeFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE005"),
            Title: spec?.Title ?? "Type must be sealed",
            MessageFormat: spec?.Message ?? "Type '{0}' must be declared 'sealed'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) =>
                focus.Symbol.IsSealed || focus.Symbol.IsValueType || focus.Symbol.IsStatic,
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>Require the type to be declared <c>abstract</c>.</summary>
    public static FocusShape<TypeFocus> MustBeAbstract(
        this FocusShape<TypeFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE015"),
            Title: spec?.Title ?? "Type must be abstract",
            MessageFormat: spec?.Message ?? "Type '{0}' must be declared 'abstract'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => focus.Symbol.IsAbstract,
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>Require the type to be declared <c>static</c>.</summary>
    public static FocusShape<TypeFocus> MustBeStatic(
        this FocusShape<TypeFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE017"),
            Title: spec?.Title ?? "Type must be static",
            MessageFormat: spec?.Message ?? "Type '{0}' must be declared 'static'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => focus.Symbol.IsStatic,
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>Require the type to carry the attribute named by <paramref name="metadataName"/>.</summary>
    public static FocusShape<TypeFocus> MustHaveAttribute(
        this FocusShape<TypeFocus> shape,
        string metadataName,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE003"),
            Title: spec?.Title ?? "Type must have required attribute",
            MessageFormat: spec?.Message ?? "Type '{0}' must be annotated with '[{1}]'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => TypeShape.HasAttribute(focus.Symbol, interned),
            MessageArgs: focus => EquatableArray.Create(focus.Symbol.Name, interned)));
        return shape;
    }

    /// <summary>Require the type's name to match the regular expression <paramref name="pattern"/>.</summary>
    public static FocusShape<TypeFocus> MustBeNamed(
        this FocusShape<TypeFocus> shape,
        string pattern,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        shape.AddCheck(new FocusCheck<TypeFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE029"),
            Title: spec?.Title ?? "Type name must match required pattern",
            MessageFormat: spec?.Message ?? "Type '{0}' name does not match pattern '{1}'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => regex.IsMatch(focus.Symbol.Name),
            MessageArgs: focus => EquatableArray.Create(focus.Symbol.Name, pattern)));
        return shape;
    }
}
