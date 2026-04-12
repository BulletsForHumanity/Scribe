using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddAbstractModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add 'abstract' modifier";

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        if (typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
        {
            return document.Project.Solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var abstractToken = SyntaxFactory.Token(SyntaxKind.AbstractKeyword)
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

        var newTypeDecl = typeDecl.WithModifiers(modifiers.Insert(insertAt, abstractToken));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }
}
