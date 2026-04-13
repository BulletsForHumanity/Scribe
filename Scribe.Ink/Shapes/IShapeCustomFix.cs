using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Scribe.Ink.Shapes;

/// <summary>
///     User-supplied fix handler for diagnostics emitted with
///     <see cref="Scribe.Shapes.FixKind.Custom"/>. Registered on a shape via
///     <see cref="ShapeInkExtensions.WithCustomFix{TModel}"/> under a
///     <c>customFixTag</c> that the <see cref="Scribe.Shapes.TypeShape.ForEachMember"/>
///     or <see cref="Scribe.Shapes.TypeShape.Check"/> declaration carried.
/// </summary>
/// <remarks>
///     <para>
///         The <c>diagnosticNode</c> argument to <see cref="FixAsync"/> is the
///         smallest syntax node covering <see cref="Diagnostic.Location"/>.
///         For member-level diagnostics, that's typically the member identifier
///         token or declaration; the implementation walks ancestors as needed
///         (e.g. <c>AncestorsAndSelf().OfType&lt;PropertyDeclarationSyntax&gt;()</c>).
///     </para>
///     <para>
///         Returning <see cref="Solution"/> (rather than <see cref="Document"/>)
///         accommodates cross-file rewriters such as symbol renames or the
///         generation of sidecar declarations.
///     </para>
/// </remarks>
public interface IShapeCustomFix
{
    /// <summary>Title shown in the editor's light-bulb menu.</summary>
    string Title(Diagnostic diagnostic);

    /// <summary>Apply the fix and return the updated solution.</summary>
    Task<Solution> FixAsync(
        Document document,
        SyntaxNode diagnosticNode,
        Diagnostic diagnostic,
        CancellationToken ct);
}
