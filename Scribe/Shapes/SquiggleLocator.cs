using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Shapes;

/// <summary>
///     Resolves a <see cref="SquiggleAt"/> anchor to a concrete <see cref="Location"/> on
///     a type's primary declaration. Used by <see cref="Shape{TModel}.ToAnalyzer"/> to
///     land diagnostics at the most editorially-useful place.
/// </summary>
internal static class SquiggleLocator
{
    public static Location Resolve(INamedTypeSymbol symbol, SquiggleAt anchor, CancellationToken ct)
    {
        var fallback = FirstLocationOrNone(symbol);
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length == 0)
        {
            return fallback;
        }

        var syntax = refs[0].GetSyntax(ct);
        if (syntax is not TypeDeclarationSyntax tds)
        {
            return fallback;
        }

        return anchor switch
        {
            SquiggleAt.TypeKeyword => tds.Keyword.GetLocation(),
            SquiggleAt.Identifier => tds.Identifier.GetLocation(),
            SquiggleAt.ModifierList => ModifierListLocation(tds),
            SquiggleAt.BaseList => tds.BaseList?.GetLocation() ?? tds.Identifier.GetLocation(),
            SquiggleAt.AttributeList => tds.AttributeLists.FirstOrDefault()?.GetLocation() ?? tds.Identifier.GetLocation(),
            SquiggleAt.ContainingNamespace => ContainingNamespaceLocation(tds) ?? tds.Identifier.GetLocation(),
            SquiggleAt.EntireDeclaration => tds.GetLocation(),
            SquiggleAt.FirstMemberOfKind => tds.Members.FirstOrDefault()?.GetLocation() ?? tds.Identifier.GetLocation(),
            _ => tds.Identifier.GetLocation(),
        };
    }

    private static Location ModifierListLocation(TypeDeclarationSyntax tds)
    {
        if (tds.Modifiers.Count == 0)
        {
            return tds.Keyword.GetLocation();
        }

        var first = tds.Modifiers.First();
        var last = tds.Modifiers.Last();
        var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(first.Span.Start, last.Span.End);
        return Location.Create(tds.SyntaxTree, span);
    }

    private static Location? ContainingNamespaceLocation(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case FileScopedNamespaceDeclarationSyntax fsn:
                    return fsn.Name.GetLocation();
                case NamespaceDeclarationSyntax nsd:
                    return nsd.Name.GetLocation();
            }
        }

        return null;
    }

    private static Location FirstLocationOrNone(INamedTypeSymbol symbol) =>
        symbol.Locations.Length == 0 ? Location.None : symbol.Locations[0];
}
