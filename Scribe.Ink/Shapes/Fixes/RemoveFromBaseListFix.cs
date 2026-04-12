using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class RemoveFromBaseListFix : IShapeFix
{
    public string Title(Diagnostic diagnostic)
    {
        var name = TargetName(diagnostic);
        return name is null ? "Remove forbidden base type" : $"Remove '{name}'";
    }

    public async Task<Solution> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        if (typeDecl.BaseList is null)
        {
            return document.Project.Solution;
        }

        var target = TargetName(diagnostic);
        if (target is null)
        {
            return document.Project.Solution;
        }

        var simpleName = ShortName(target);
        var match = typeDecl.BaseList.Types.FirstOrDefault(t => MatchesTypeName(t, target, simpleName));
        if (match is null)
        {
            return document.Project.Solution;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var remaining = typeDecl.BaseList.Types.Remove(match);
        TypeDeclarationSyntax newTypeDecl = remaining.Count == 0
            ? typeDecl.WithBaseList(null)
            : typeDecl.WithBaseList(typeDecl.BaseList.WithTypes(remaining));

        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl)).Project.Solution;
    }

    private static string? TargetName(Diagnostic diagnostic)
    {
        if (diagnostic.Properties.TryGetValue("interface", out var i) && !string.IsNullOrEmpty(i))
        {
            return i;
        }

        return diagnostic.Properties.TryGetValue("baseClass", out var b) && !string.IsNullOrEmpty(b)
            ? b
            : null;
    }

    private static string ShortName(string metadataName)
    {
        var lastDot = metadataName.LastIndexOf('.');
        return lastDot < 0 ? metadataName : metadataName.Substring(lastDot + 1);
    }

    private static bool MatchesTypeName(BaseTypeSyntax baseType, string full, string simple)
    {
        var text = baseType.Type.ToString();
        return string.Equals(text, full, StringComparison.Ordinal)
            || string.Equals(text, simple, StringComparison.Ordinal)
            || text.EndsWith("." + simple, StringComparison.Ordinal);
    }
}
