using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddStaticModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add 'static' modifier";

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        if (typeDecl.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var staticToken = SyntaxFactory.Token(SyntaxKind.StaticKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var modifiers = typeDecl.Modifiers;
        var insertAt = modifiers.Count;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].IsKind(SyntaxKind.PartialKeyword))
            {
                insertAt = i;
                break;
            }
        }

        var newTypeDecl = typeDecl.WithModifiers(modifiers.Insert(insertAt, staticToken));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }
}
