using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scribe.Ink;

/// <summary>
///     Code fix for <see cref="QuillUsingsAnalyzer"/> SCRIBE300 diagnostics. Adds
///     a <c>q.Using("Foo.Bar")</c> statement next to an existing
///     <c>Using(...)</c>/<c>Usings(...)</c> call site in the same containing type.
///     If no existing call site exists, no fix is offered (the diagnostic still
///     fires; the author resolves manually).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(QuillUsingsCodeFixProvider))]
[Shared]
public sealed class QuillUsingsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(QuillUsingsAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id != QuillUsingsAnalyzer.DiagnosticId)
            {
                continue;
            }

            var suggested = ExtractSuggestedNamespace(diagnostic);
            if (string.IsNullOrEmpty(suggested))
            {
                continue;
            }

            var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);
            var typeDecl = diagnosticNode.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDecl is null)
            {
                continue;
            }

            var anchor = FindFirstUsingCall(typeDecl);
            if (anchor is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add q.Using(\"{suggested}\") to the Quill builder",
                    createChangedDocument: ct => InsertUsingAfterAsync(context.Document, anchor, suggested!, ct),
                    equivalenceKey: $"ScribeAddQuillUsing:{suggested}"),
                diagnostic);
        }
    }

    /// <summary>
    ///     The diagnostic message embeds the suggested namespace as the second
    ///     format argument. Pull it out by parsing the <c>Using("...")</c> portion.
    ///     The analyzer always emits this shape, so a string scan is sufficient.
    /// </summary>
    private static string? ExtractSuggestedNamespace(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        const string token = "q.Using(\"";
        var start = message.IndexOf(token, System.StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += token.Length;
        var end = message.IndexOf('"', start);
        if (end <= start)
        {
            return null;
        }

        return message.Substring(start, end - start);
    }

    private static InvocationExpressionSyntax? FindFirstUsingCall(TypeDeclarationSyntax typeDecl)
    {
        foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                continue;
            }

            var name = member.Name.Identifier.ValueText;
            if (name == "Using" || name == "Usings")
            {
                return invocation;
            }
        }

        return null;
    }

    private static async Task<Document> InsertUsingAfterAsync(
        Document document,
        InvocationExpressionSyntax anchor,
        string suggestedNamespace,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (anchor.Expression is not MemberAccessExpressionSyntax member)
        {
            return document;
        }

        var anchorStatement = anchor.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (anchorStatement is null)
        {
            return document;
        }

        var newCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    member.Expression.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("Using")))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(suggestedNamespace))))));

        var newStatement = SyntaxFactory.ExpressionStatement(newCall)
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);

        var newRoot = root.InsertNodesAfter(anchorStatement, new[] { newStatement });
        return document.WithSyntaxRoot(newRoot);
    }
}
