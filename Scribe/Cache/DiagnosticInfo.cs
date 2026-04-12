using Microsoft.CodeAnalysis;

namespace Scribe.Cache;

/// <summary>
///     A cache-safe record of a diagnostic — the ID, severity, message arguments, and
///     location — without holding references to a <see cref="DiagnosticDescriptor"/> or
///     <see cref="Location"/>.
/// </summary>
/// <remarks>
///     <para>
///         Flows through the incremental pipeline alongside the symbol data it accompanies.
///         At the emission stage, callers combine it with a user-provided
///         <see cref="DiagnosticDescriptor"/> (which contains non-cacheable state) and
///         materialise a real <see cref="Diagnostic"/> via <see cref="Materialize"/>.
///     </para>
/// </remarks>
public readonly record struct DiagnosticInfo(
    string Id,
    DiagnosticSeverity Severity,
    EquatableArray<string> MessageArgs,
    LocationInfo? Location)
{
    /// <summary>
    ///     Combine this cache-safe record with the user-supplied descriptor and produce a
    ///     real <see cref="Diagnostic"/> for reporting.
    /// </summary>
    /// <param name="descriptor">
    ///     The descriptor to use. Its <see cref="DiagnosticDescriptor.Id"/> should match
    ///     <see cref="Id"/> — this is not enforced here, callers are responsible.
    /// </param>
    public Diagnostic Materialize(DiagnosticDescriptor descriptor)
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        return MessageArgs.IsEmpty
            ? Diagnostic.Create(descriptor, location)
            : Diagnostic.Create(descriptor, location, ToObjectArray(MessageArgs));
    }

    private static object?[] ToObjectArray(EquatableArray<string> args)
    {
        var result = new object?[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            result[i] = args[i];
        }

        return result;
    }
}
