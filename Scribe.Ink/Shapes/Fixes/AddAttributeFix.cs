using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class AddAttributeFix : IShapeFix
{
    public string Title(Diagnostic diagnostic)
    {
        var name = AttributeName(diagnostic);
        return name is null ? "Add required attribute" : $"Add '[{Shorten(name)}]'";
    }

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var attributeName = AttributeName(diagnostic);
        if (attributeName is null)
        {
            return document.Project.Solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName(Shorten(attributeName)));
        var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));

        var newTypeDecl = typeDecl.WithAttributeLists(typeDecl.AttributeLists.Add(attrList));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }

    private static string? AttributeName(Diagnostic diagnostic)
    {
        return diagnostic.Properties.TryGetValue("attribute", out var name) && !string.IsNullOrEmpty(name)
            ? name
            : null;
    }

    // Strip trailing "Attribute" for the bracket form; keep full name otherwise.
    private static string Shorten(string name)
    {
        const string Suffix = "Attribute";
        return name.EndsWith(Suffix, System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - Suffix.Length)
            : name;
    }
}
