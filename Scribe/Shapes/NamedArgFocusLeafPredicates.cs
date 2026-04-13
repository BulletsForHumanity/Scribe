using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Leaf-predicate extensions on <see cref="FocusShape{NamedArgFocus}"/>. v1 subset
///     per the gap analysis B.4 catalogue — covers the "does this named attribute
///     argument equal / satisfy …?" checks. Mirrors
///     <see cref="ConstructorArgFocusLeafPredicates"/>.
/// </summary>
public static class NamedArgFocusLeafPredicates
{
    /// <summary>Require the named argument value to equal <paramref name="expected"/>.</summary>
    public static FocusShape<NamedArgFocus<T>> MustBe<T>(
        this FocusShape<NamedArgFocus<T>> shape,
        T expected,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        var comparer = EqualityComparer<T>.Default;
        var expectedText = expected?.ToString() ?? "null";
        shape.AddCheck(new FocusCheck<NamedArgFocus<T>>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE085"),
            Title: spec?.Title ?? "Named argument must have required value",
            MessageFormat: spec?.Message ?? "Named argument '{0}' must equal '{1}'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => comparer.Equals(focus.Value!, expected),
            MessageArgs: focus => EquatableArray.Create(focus.Name, expectedText)));
        return shape;
    }

    /// <summary>
    ///     Require a <c>string</c> named argument to be non-null and non-empty.
    ///     Whitespace-only strings are considered empty.
    /// </summary>
    public static FocusShape<NamedArgFocus<string>> MustNotBeEmpty(
        this FocusShape<NamedArgFocus<string>> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<NamedArgFocus<string>>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE086"),
            Title: spec?.Title ?? "Named argument must not be empty",
            MessageFormat: spec?.Message ?? "Named argument '{0}' must not be empty",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => !string.IsNullOrWhiteSpace(focus.Value),
            MessageArgs: static focus => EquatableArray.Create(focus.Name)));
        return shape;
    }
}
