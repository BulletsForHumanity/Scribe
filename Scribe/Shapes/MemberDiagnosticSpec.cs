using Microsoft.CodeAnalysis;

namespace Scribe.Shapes;

/// <summary>
///     Declarative descriptor for a member-level check registered via
///     <see cref="TypeShape.ForEachMember"/>. Unlike <see cref="DiagnosticSpec"/>
///     (which overrides a primitive's built-in defaults) this spec is fully
///     user-defined — <see cref="Id"/>, <see cref="Title"/>, and <see cref="Message"/>
///     are required.
/// </summary>
/// <param name="Id">Diagnostic ID (e.g. <c>WORD1005</c>).</param>
/// <param name="Title">One-line diagnostic title.</param>
/// <param name="Message">Message format — <c>{0}</c>/<c>{1}</c>/... substituted with <c>MessageArgs</c>.</param>
/// <param name="Severity">Severity — defaults to <see cref="DiagnosticSeverity.Warning"/>.</param>
/// <param name="Squiggle">Anchor where the diagnostic squiggle lands on the offending member.</param>
/// <param name="Fix">Auto-fix kind. <see cref="FixKind.Custom"/> pairs with <paramref name="CustomFixTag"/> to dispatch to a user-registered handler.</param>
/// <param name="CustomFixTag">Opaque tag used to look up the custom-fix handler when <paramref name="Fix"/> is <see cref="FixKind.Custom"/>.</param>
public readonly record struct MemberDiagnosticSpec(
    string Id,
    string Title,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Warning,
    MemberSquiggleAt Squiggle = MemberSquiggleAt.Identifier,
    FixKind Fix = FixKind.None,
    string? CustomFixTag = null);
