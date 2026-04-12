using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddPartialModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add 'partial' modifier";

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        if (typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var newTypeDecl = typeDecl.WithModifiers(typeDecl.Modifiers.Add(partialToken));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }
}
