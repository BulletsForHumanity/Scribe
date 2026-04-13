using Microsoft.CodeAnalysis;

namespace Scribe.Shapes;

/// <summary>
///     Selective override for a <see cref="TypeShape"/> check's default diagnostic
///     descriptor. Any field left <see langword="null"/> keeps the primitive's default.
/// </summary>
/// <param name="Id">Diagnostic ID (defaults to <c>SCRIBE</c>-prefixed per primitive).</param>
/// <param name="Title">One-line diagnostic title.</param>
/// <param name="Message">Message format — <c>{0}</c> etc. will be substituted with the primitive's arguments.</param>
/// <param name="Severity">Severity — <see langword="null"/> keeps the primitive default.</param>
/// <param name="Target">Squiggle anchor.</param>
/// <param name="Fix">Override for the auto-fix hint.</param>
public readonly record struct DiagnosticSpec(
    string? Id = null,
    string? Title = null,
    string? Message = null,
    DiagnosticSeverity? Severity = null,
    SquiggleAt? Target = null,
    FixSpec? Fix = null);
