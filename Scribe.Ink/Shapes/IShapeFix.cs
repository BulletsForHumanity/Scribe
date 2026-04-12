using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes;

/// <summary>
///     A single code-fix action that operates on a type declaration. Each
///     <see cref="Scribe.Shapes.FixKind"/> maps to one implementation.
/// </summary>
internal interface IShapeFix
{
    string Title(Diagnostic diagnostic);

    Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct);
}
