using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink.Shapes.Fixes;

internal sealed class SetVisibilityFix : IShapeFix
{
    private static readonly SyntaxKind[] VisibilityKinds =
    {
        SyntaxKind.PublicKeyword,
        SyntaxKind.InternalKeyword,
        SyntaxKind.PrivateKeyword,
        SyntaxKind.ProtectedKeyword,
    };

    public string Title(Diagnostic diagnostic)
    {
        var target = Target(diagnostic);
        return target is null ? "Change visibility" : $"Make '{target}'";
    }

    public async Task<Document> FixAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var target = Target(diagnostic);
        if (target is null)
        {
            return document;
        }

        if (!TryGetKeywordKind(target, out var targetKind))
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var modifiers = typeDecl.Modifiers;

        var kept = new List<SyntaxToken>(modifiers.Count);
        var firstVisibilityTrivia = default(SyntaxTriviaList);
        var replaced = false;
        foreach (var token in modifiers)
        {
            if (IsVisibilityKind(token.Kind()))
            {
                if (!replaced)
                {
                    firstVisibilityTrivia = token.LeadingTrivia;
                    replaced = true;
                }
                continue;
            }
            kept.Add(token);
        }

        var newToken = SyntaxFactory.Token(targetKind)
            .WithTrailingTrivia(SyntaxFactory.Space);

        if (replaced)
        {
            newToken = newToken.WithLeadingTrivia(firstVisibilityTrivia);
            kept.Insert(0, newToken);
        }
        else
        {
            var existingLeading = typeDecl.GetLeadingTrivia();
            newToken = newToken.WithLeadingTrivia(existingLeading);
            var newTypeDecl = typeDecl
                .WithoutLeadingTrivia()
                .WithModifiers(modifiers.Insert(0, newToken));
            return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
        }

        var newModifiers = SyntaxFactory.TokenList(kept);
        var updated = typeDecl.WithModifiers(newModifiers);
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, updated));
    }

    private static bool IsVisibilityKind(SyntaxKind kind)
    {
        foreach (var k in VisibilityKinds)
        {
            if (k == kind) return true;
        }
        return false;
    }

    private static bool TryGetKeywordKind(string visibility, out SyntaxKind kind)
    {
        switch (visibility)
        {
            case "public":
                kind = SyntaxKind.PublicKeyword;
                return true;
            case "internal":
                kind = SyntaxKind.InternalKeyword;
                return true;
            case "private":
                kind = SyntaxKind.PrivateKeyword;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string? Target(Diagnostic diagnostic)
    {
        return diagnostic.Properties.TryGetValue("visibility", out var v) && !string.IsNullOrEmpty(v)
            ? v
            : null;
    }
}
