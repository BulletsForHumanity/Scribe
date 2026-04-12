using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddBaseClassFix : IShapeFix
{
    public string Title(Diagnostic diagnostic)
    {
        var name = BaseClassName(diagnostic);
        return name is null ? "Add required base class" : $"Extend '{name}'";
    }

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var baseClassName = BaseClassName(diagnostic);
        if (baseClassName is null)
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseClassName));

        // Base class must come first in C#'s base list.
        TypeDeclarationSyntax newTypeDecl;
        if (typeDecl.BaseList is null)
        {
            newTypeDecl = typeDecl.WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        }
        else
        {
            var existing = typeDecl.BaseList.Types;
            var withBase = existing.Insert(0, baseType);
            newTypeDecl = typeDecl.WithBaseList(typeDecl.BaseList.WithTypes(withBase));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }

    private static string? BaseClassName(Diagnostic diagnostic)
    {
        return diagnostic.Properties.TryGetValue("baseClass", out var name) && !string.IsNullOrEmpty(name)
            ? name
            : null;
    }
}
