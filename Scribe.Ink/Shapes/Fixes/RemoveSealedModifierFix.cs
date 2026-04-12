using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class RemoveSealedModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Remove 'sealed' modifier";

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        var token = typeDecl.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.SealedKeyword));
        if (token == default)
        {
            return document.Project.Solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var newTypeDecl = typeDecl.WithModifiers(typeDecl.Modifiers.Remove(token));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }
}
