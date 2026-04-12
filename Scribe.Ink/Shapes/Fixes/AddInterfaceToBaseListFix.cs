using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddInterfaceToBaseListFix : IShapeFix
{
    public string Title(Diagnostic diagnostic)
    {
        var name = InterfaceName(diagnostic);
        return name is null ? "Add required interface" : $"Implement '{name}'";
    }

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var interfaceName = InterfaceName(diagnostic);
        if (interfaceName is null)
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName));

        var newTypeDecl = typeDecl.BaseList is null
            ? typeDecl.WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)))
            : typeDecl.WithBaseList(typeDecl.BaseList.AddTypes(baseType));

        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }

    private static string? InterfaceName(Diagnostic diagnostic)
    {
        return diagnostic.Properties.TryGetValue("interface", out var name) && !string.IsNullOrEmpty(name)
            ? name
            : null;
    }
}
