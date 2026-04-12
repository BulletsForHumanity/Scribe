using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Shapes;

/// <summary>
///     Resolves a <see cref="MemberSquiggleAt"/> anchor against a member's
///     first declaring syntax reference. Parallel to <see cref="SquiggleLocator"/>
///     for member-level checks.
/// </summary>
internal static class MemberSquiggleLocator
{
    public static Location Resolve(ISymbol symbol, MemberSquiggleAt anchor, CancellationToken ct)
    {
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length == 0)
        {
            return symbol.Locations.Length == 0 ? Location.None : symbol.Locations[0];
        }

        var node = refs[0].GetSyntax(ct);
        return anchor switch
        {
            MemberSquiggleAt.Identifier => IdentifierLocation(node) ?? node.GetLocation(),
            MemberSquiggleAt.FullDeclaration => node.GetLocation(),
            MemberSquiggleAt.TypeAnnotation => TypeAnnotationLocation(node) ?? node.GetLocation(),
            MemberSquiggleAt.FirstAttribute => FirstAttributeLocation(node) ?? IdentifierLocation(node) ?? node.GetLocation(),
            MemberSquiggleAt.ModifierList => ModifierListLocation(node) ?? IdentifierLocation(node) ?? node.GetLocation(),
            _ => node.GetLocation(),
        };
    }

    private static Location? IdentifierLocation(SyntaxNode node) => node switch
    {
        PropertyDeclarationSyntax p => p.Identifier.GetLocation(),
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.GetLocation(),
        EventFieldDeclarationSyntax ef => ef.Declaration.Variables.FirstOrDefault()?.Identifier.GetLocation(),
        EventDeclarationSyntax e => e.Identifier.GetLocation(),
        MethodDeclarationSyntax m => m.Identifier.GetLocation(),
        ConstructorDeclarationSyntax c => c.Identifier.GetLocation(),
        DestructorDeclarationSyntax d => d.Identifier.GetLocation(),
        IndexerDeclarationSyntax ix => ix.ThisKeyword.GetLocation(),
        OperatorDeclarationSyntax op => op.OperatorToken.GetLocation(),
        ConversionOperatorDeclarationSyntax co => co.Type.GetLocation(),
        TypeDeclarationSyntax t => t.Identifier.GetLocation(),
        DelegateDeclarationSyntax del => del.Identifier.GetLocation(),
        EnumMemberDeclarationSyntax em => em.Identifier.GetLocation(),
        VariableDeclaratorSyntax v => v.Identifier.GetLocation(),
        _ => null,
    };

    private static Location? TypeAnnotationLocation(SyntaxNode node) => node switch
    {
        PropertyDeclarationSyntax p => p.Type.GetLocation(),
        FieldDeclarationSyntax f => f.Declaration.Type.GetLocation(),
        EventFieldDeclarationSyntax ef => ef.Declaration.Type.GetLocation(),
        EventDeclarationSyntax e => e.Type.GetLocation(),
        MethodDeclarationSyntax m => m.ReturnType.GetLocation(),
        IndexerDeclarationSyntax ix => ix.Type.GetLocation(),
        OperatorDeclarationSyntax op => op.ReturnType.GetLocation(),
        ConversionOperatorDeclarationSyntax co => co.Type.GetLocation(),
        DelegateDeclarationSyntax del => del.ReturnType.GetLocation(),
        _ => null,
    };

    private static Location? FirstAttributeLocation(SyntaxNode node) => node switch
    {
        MemberDeclarationSyntax m => m.AttributeLists.FirstOrDefault()?.GetLocation(),
        _ => null,
    };

    private static Location? ModifierListLocation(SyntaxNode node)
    {
        var modifiers = node switch
        {
            BaseMethodDeclarationSyntax bm => bm.Modifiers,
            BasePropertyDeclarationSyntax bp => bp.Modifiers,
            BaseFieldDeclarationSyntax bf => bf.Modifiers,
            BaseTypeDeclarationSyntax bt => bt.Modifiers,
            DelegateDeclarationSyntax dd => dd.Modifiers,
            _ => default,
        };

        if (modifiers.Count == 0)
        {
            return null;
        }

        var first = modifiers.First();
        var last = modifiers.Last();
        var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(first.Span.Start, last.Span.End);
        return Location.Create(first.SyntaxTree!, span);
    }
}
