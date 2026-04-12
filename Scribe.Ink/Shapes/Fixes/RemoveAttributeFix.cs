using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class RemoveAttributeFix : IShapeFix
{
    public string Title(Diagnostic diagnostic)
    {
        var name = AttributeName(diagnostic);
        return name is null ? "Remove forbidden attribute" : $"Remove '[{Shorten(name)}]'";
    }

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var target = AttributeName(diagnostic);
        if (target is null)
        {
            return document.Project.Solution;
        }

        var shortName = Shorten(target);
        var fullName = target;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var newTypeDecl = typeDecl;

        foreach (var list in typeDecl.AttributeLists)
        {
            var keepAttrs = list.Attributes
                .Where(a => !Matches(a, fullName, shortName))
                .ToArray();

            if (keepAttrs.Length == list.Attributes.Count)
            {
                continue;
            }

            if (keepAttrs.Length == 0)
            {
                newTypeDecl = newTypeDecl.WithAttributeLists(
                    newTypeDecl.AttributeLists.Remove(list));
            }
            else
            {
                var newList = list.WithAttributes(SyntaxFactory.SeparatedList(keepAttrs));
                newTypeDecl = newTypeDecl.WithAttributeLists(
                    newTypeDecl.AttributeLists.Replace(list, newList));
            }
        }

        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }

    private static bool Matches(AttributeSyntax attribute, string full, string shortName)
    {
        var text = attribute.Name.ToString();
        var bareShort = Shorten(shortName);
        return string.Equals(text, full, StringComparison.Ordinal)
            || string.Equals(text, shortName, StringComparison.Ordinal)
            || string.Equals(text, bareShort, StringComparison.Ordinal)
            || text.EndsWith("." + shortName, StringComparison.Ordinal)
            || text.EndsWith("." + bareShort, StringComparison.Ordinal);
    }

    private static string? AttributeName(Diagnostic diagnostic)
    {
        return diagnostic.Properties.TryGetValue("attribute", out var name) && !string.IsNullOrEmpty(name)
            ? name
            : null;
    }

    private static string Shorten(string name)
    {
        const string Suffix = "Attribute";
        return name.EndsWith(Suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - Suffix.Length)
            : name;
    }
}
