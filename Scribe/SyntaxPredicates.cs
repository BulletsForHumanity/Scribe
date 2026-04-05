using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe;

/// <summary>
///     Shared syntactic predicates for <see cref="SyntaxValueProvider.CreateSyntaxProvider"/>
///     and <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/>.
/// </summary>
public static class SyntaxPredicates
{
    /// <summary>Node is a <c>partial record</c> (class or struct).</summary>
    public static bool IsPartialRecord(SyntaxNode node) =>
        node is RecordDeclarationSyntax r && r.Modifiers.Any(SyntaxKind.PartialKeyword);

    /// <summary>Node is a <c>partial record struct</c>.</summary>
    public static bool IsPartialRecordStruct(SyntaxNode node) =>
        node is RecordDeclarationSyntax r
        && r.IsKind(SyntaxKind.RecordStructDeclaration)
        && r.Modifiers.Any(SyntaxKind.PartialKeyword);

    /// <summary>Node is a <c>partial class</c>.</summary>
    public static bool IsPartialClass(SyntaxNode node) =>
        node is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword);

    /// <summary>Node is any type declaration with the <c>partial</c> modifier.</summary>
    public static bool IsPartialType(SyntaxNode node) =>
        node is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword);
}
