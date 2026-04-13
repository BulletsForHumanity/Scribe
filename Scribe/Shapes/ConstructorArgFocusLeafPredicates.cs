using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Leaf-predicate extensions on <see cref="FocusShape{ConstructorArgFocus}"/>. v1
///     subset per the gap analysis B.4 catalogue — covers the "does this positional
///     attribute argument equal / satisfy …?" checks.
/// </summary>
public static class ConstructorArgFocusLeafPredicates
{
    /// <summary>Require the argument value to equal <paramref name="expected"/>.</summary>
    public static FocusShape<ConstructorArgFocus<T>> MustBe<T>(
        this FocusShape<ConstructorArgFocus<T>> shape,
        T expected,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        var comparer = EqualityComparer<T>.Default;
        var expectedText = expected?.ToString() ?? "null";
        shape.AddCheck(new FocusCheck<ConstructorArgFocus<T>>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE080"),
            Title: spec?.Title ?? "Constructor argument must have required value",
            MessageFormat: spec?.Message ?? "Constructor argument at position {0} must equal '{1}'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => comparer.Equals(focus.Value!, expected),
            MessageArgs: focus => EquatableArray.Create(focus.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), expectedText)));
        return shape;
    }

    /// <summary>
    ///     Require a <c>string</c> constructor argument to be non-null and non-empty.
    ///     Whitespace-only strings are considered empty.
    /// </summary>
    public static FocusShape<ConstructorArgFocus<string>> MustNotBeEmpty(
        this FocusShape<ConstructorArgFocus<string>> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<ConstructorArgFocus<string>>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE081"),
            Title: spec?.Title ?? "Constructor argument must not be empty",
            MessageFormat: spec?.Message ?? "Constructor argument at position {0} must not be empty",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => !string.IsNullOrWhiteSpace(focus.Value),
            MessageArgs: static focus => EquatableArray.Create(focus.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return shape;
    }
}
