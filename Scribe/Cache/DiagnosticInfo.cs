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
    LocationInfo? Location,
    string? FocusPath = null)
{
    /// <summary>
    ///     Combine this cache-safe record with the user-supplied descriptor and produce a
    ///     real <see cref="Diagnostic"/> for reporting. When <see cref="FocusPath"/> is
    ///     non-null, the rendered message is prefixed with <c>"[path] "</c> — the B.8
    ///     focus-path breadcrumb that tells the reader which lens hops produced the
    ///     violation.
    /// </summary>
    /// <param name="descriptor">
    ///     The descriptor to use. Its <see cref="DiagnosticDescriptor.Id"/> should match
    ///     <see cref="Id"/> — this is not enforced here, callers are responsible.
    /// </param>
    public Diagnostic Materialize(DiagnosticDescriptor descriptor)
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;

        if (FocusPath is null)
        {
            return MessageArgs.IsEmpty
                ? Diagnostic.Create(descriptor, location)
                : Diagnostic.Create(descriptor, location, ToObjectArray(MessageArgs));
        }

        var rendered = MessageArgs.IsEmpty
            ? descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ToObjectArray(MessageArgs));

        var wrapped = new DiagnosticDescriptor(
            id: descriptor.Id,
            title: descriptor.Title,
            messageFormat: "[" + FocusPath + "] " + rendered,
            category: descriptor.Category,
            defaultSeverity: descriptor.DefaultSeverity,
            isEnabledByDefault: descriptor.IsEnabledByDefault,
            description: descriptor.Description,
            helpLinkUri: descriptor.HelpLinkUri);

        return Diagnostic.Create(wrapped, location);
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
