using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes;

/// <summary>
///     A single code-fix action. Each <see cref="Scribe.Shapes.FixKind"/> maps to
///     one implementation. Returning <see cref="Solution"/> (instead of a single
///     <see cref="Document"/>) accommodates cross-file fixers such as Renamer-based
///     symbol renames without forcing a second, parallel interface.
/// </summary>
internal interface IShapeFix
{
    string Title(Diagnostic diagnostic);

    Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct);
}
