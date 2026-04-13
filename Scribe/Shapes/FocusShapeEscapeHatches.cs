using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Generic custom-predicate escape hatch on any <see cref="FocusShape{TFocus}"/>.
///     Per the design principle "escape hatches as first-class citizens", every focus
///     shape exposes <see cref="Satisfy{TFocus}(FocusShape{TFocus}, Func{TFocus, bool}, string, string, string, DiagnosticSeverity)"/>
///     at the same fluent position as its built-in predicates — so bolting on a custom
///     check feels native instead of dropping out of the DSL.
/// </summary>
public static class FocusShapeEscapeHatches
{
    /// <summary>
    ///     Register a custom positive predicate. <see langword="true"/> means the
    ///     focus is satisfied; <see langword="false"/> emits one diagnostic per
    ///     offending focus at the lens's smudge anchor.
    /// </summary>
    public static FocusShape<TFocus> Satisfy<TFocus>(
        this FocusShape<TFocus> shape,
        Func<TFocus, bool> predicate,
        string id,
        string title,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        where TFocus : IEquatable<TFocus>
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        ValidateId(id);
        shape.AddCheck(new FocusCheck<TFocus>(
            Id: InternPool.Intern(id),
            Title: title,
            MessageFormat: message,
            Severity: severity,
            Predicate: (focus, _, _) => predicate(focus),
            MessageArgs: static _ => EquatableArray<string>.Empty));
        return shape;
    }

    /// <summary>
    ///     Richer overload whose predicate receives the <see cref="Compilation"/> and
    ///     <see cref="System.Threading.CancellationToken"/>. Use when the check needs
    ///     semantic-model access (e.g. looking up a type by metadata name) or must
    ///     cooperate with the incremental pipeline's cancellation.
    /// </summary>
    public static FocusShape<TFocus> Satisfy<TFocus>(
        this FocusShape<TFocus> shape,
        Func<TFocus, Compilation, System.Threading.CancellationToken, bool> predicate,
        string id,
        string title,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        where TFocus : IEquatable<TFocus>
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        ValidateId(id);
        shape.AddCheck(new FocusCheck<TFocus>(
            Id: InternPool.Intern(id),
            Title: title,
            MessageFormat: message,
            Severity: severity,
            Predicate: predicate,
            MessageArgs: static _ => EquatableArray<string>.Empty));
        return shape;
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Diagnostic id must not be empty.", nameof(id));
        }
    }
}
