using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddParameterlessConstructorFix : IShapeFix
{
    public string Title(Diagnostic _) => "Add public parameterless constructor";

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic _,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var name = typeDecl.Identifier.ValueText;

        var ctor = SyntaxFactory.ConstructorDeclaration(name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block());

        var insertAt = 0;
        for (var i = 0; i < typeDecl.Members.Count; i++)
        {
            if (typeDecl.Members[i] is FieldDeclarationSyntax)
            {
                insertAt = i + 1;
                continue;
            }
            break;
        }

        var newTypeDecl = typeDecl.WithMembers(typeDecl.Members.Insert(insertAt, ctor));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }
}
