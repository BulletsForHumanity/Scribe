using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddReadOnlyModifierFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add 'readonly' modifier";

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        if (typeDecl.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
        {
            return document.Project.Solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var readOnlyToken = SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Insert 'readonly' before 'partial' / 'ref' / type keyword for idiomatic ordering
        // (mirrors AddSealedModifierFix).
        var modifiers = typeDecl.Modifiers;
        var insertAt = modifiers.Count;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].IsKind(SyntaxKind.PartialKeyword)
                || modifiers[i].IsKind(SyntaxKind.RefKeyword))
            {
                insertAt = i;
                break;
            }
        }

        var newTypeDecl = typeDecl.WithModifiers(modifiers.Insert(insertAt, readOnlyToken));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }
}
