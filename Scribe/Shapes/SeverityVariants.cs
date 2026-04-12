using Microsoft.CodeAnalysis;

namespace Scribe.Shapes;

/// <summary>
///     Helpers used by the generated <c>Should*</c> / <c>Could*</c> /
///     <c>ShouldNot*</c> variants of each <c>Must*</c> primitive. Applies a
///     default severity only when the caller has not explicitly chosen one.
/// </summary>
internal static class SeverityVariants
{
    /// <summary>
    ///     Return a <see cref="DiagnosticSpec"/> whose <see cref="DiagnosticSpec.Severity"/>
    ///     is the one the caller provided, or — if the caller left it unset —
    ///     <paramref name="defaultSeverity"/>.
    /// </summary>
    public static DiagnosticSpec WithSeverity(DiagnosticSpec? spec, DiagnosticSeverity defaultSeverity)
    {
        if (spec is null)
        {
            return new DiagnosticSpec(Severity: defaultSeverity);
        }

        return spec.Value.Severity is null
            ? spec.Value with { Severity = defaultSeverity }
            : spec.Value;
    }
}
