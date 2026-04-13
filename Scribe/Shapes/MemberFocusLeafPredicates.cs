using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Leaf-predicate extensions on <see cref="FocusShape{MemberFocus}"/>. v1 subset
///     per the gap analysis B.4 catalogue — covers the most common member-level
///     checks an author wants to declare under a <c>Members(...)</c> lens.
/// </summary>
public static class MemberFocusLeafPredicates
{
    /// <summary>Require the member to carry the attribute named by <paramref name="metadataName"/>.</summary>
    public static FocusShape<MemberFocus> MustHaveAttribute(
        this FocusShape<MemberFocus> shape,
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
        shape.AddCheck(new FocusCheck<MemberFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE060"),
            Title: spec?.Title ?? "Member must have required attribute",
            MessageFormat: spec?.Message ?? "Member '{0}' must be annotated with '[{1}]'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: (focus, _, _) => HasAttribute(focus.Symbol, interned),
            MessageArgs: focus => EquatableArray.Create(focus.Symbol.Name, interned)));
        return shape;
    }

    /// <summary>Require the member's declared accessibility to be <c>public</c>.</summary>
    public static FocusShape<MemberFocus> MustBePublic(
        this FocusShape<MemberFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<MemberFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE061"),
            Title: spec?.Title ?? "Member must be public",
            MessageFormat: spec?.Message ?? "Member '{0}' must be declared 'public'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => focus.Symbol.DeclaredAccessibility == Accessibility.Public,
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>Require the member to be declared <c>static</c>.</summary>
    public static FocusShape<MemberFocus> MustBeStatic(
        this FocusShape<MemberFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<MemberFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE062"),
            Title: spec?.Title ?? "Member must be static",
            MessageFormat: spec?.Message ?? "Member '{0}' must be declared 'static'",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => focus.Symbol.IsStatic,
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    /// <summary>
    ///     Require the member to be read-only. Applies to fields (<c>readonly</c>),
    ///     properties (no setter, or init-only), and auto-implemented properties backed by a
    ///     readonly field. Members that cannot meaningfully be read-only (methods, events)
    ///     pass silently.
    /// </summary>
    public static FocusShape<MemberFocus> MustBeReadOnly(
        this FocusShape<MemberFocus> shape,
        DiagnosticSpec? spec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.AddCheck(new FocusCheck<MemberFocus>(
            Id: InternPool.Intern(spec?.Id ?? "SCRIBE063"),
            Title: spec?.Title ?? "Member must be read-only",
            MessageFormat: spec?.Message ?? "Member '{0}' must be read-only",
            Severity: spec?.Severity ?? DiagnosticSeverity.Error,
            Predicate: static (focus, _, _) => IsReadOnly(focus.Symbol),
            MessageArgs: static focus => EquatableArray.Create(focus.Symbol.Name)));
        return shape;
    }

    private static bool IsReadOnly(ISymbol symbol) =>
        symbol switch
        {
            IFieldSymbol f => f.IsReadOnly || f.IsConst,
            IPropertySymbol p => p.SetMethod is null || p.SetMethod.IsInitOnly,
            _ => true,
        };

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var fqn = attribute.AttributeClass?.ToDisplayString();
            if (fqn is null)
            {
                continue;
            }

            if (string.Equals(fqn, metadataName, StringComparison.Ordinal))
            {
                return true;
            }

            var bracket = fqn.IndexOf('<');
            var bare = bracket > 0 ? fqn.Substring(0, bracket) : fqn;
            if (string.Equals(bare, metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
