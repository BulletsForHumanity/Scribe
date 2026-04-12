using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddSealedModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add 'sealed' modifier";

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        if (typeDecl.Modifiers.Any(SyntaxKind.SealedKeyword))
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Insert 'sealed' before 'partial' / 'abstract' / type keyword for idiomatic ordering.
        var modifiers = typeDecl.Modifiers;
        var insertAt = modifiers.Count;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].IsKind(SyntaxKind.PartialKeyword)
                || modifiers[i].IsKind(SyntaxKind.AbstractKeyword))
            {
                insertAt = i;
                break;
            }
        }

        var newTypeDecl = typeDecl.WithModifiers(modifiers.Insert(insertAt, sealedToken));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }
}
