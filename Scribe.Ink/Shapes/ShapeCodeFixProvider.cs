using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Scribe.Ink.Shapes.Fixes;
using Scribe.Shapes;

namespace Scribe.Ink.Shapes;

/// <summary>
///     Dispatching <see cref="CodeFixProvider"/> for Shape-emitted diagnostics.
///     Reads the <c>fixKind</c> property written by the analyzer and delegates
///     to the matching <see cref="IShapeFix"/> implementation.
/// </summary>
internal sealed class ShapeCodeFixProvider : CodeFixProvider
{
    private readonly ImmutableArray<string> _ids;

    internal ShapeCodeFixProvider(ImmutableArray<string> diagnosticIds) => _ids = diagnosticIds;

    public override ImmutableArray<string> FixableDiagnosticIds => _ids;

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

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
            if (!diagnostic.Properties.TryGetValue("fixKind", out var fixKindStr)
                || string.IsNullOrEmpty(fixKindStr)
                || !Enum.TryParse<FixKind>(fixKindStr, out var fixKind)
                || fixKind == FixKind.None)
            {
                continue;
            }

            var fix = ResolveFix(fixKind);
            if (fix is null)
            {
                continue;
            }

            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDecl is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: fix.Title(diagnostic),
                    createChangedDocument: ct => fix.FixAsync(context.Document, typeDecl, diagnostic, ct),
                    equivalenceKey: fixKind.ToString()),
                diagnostic);
        }
    }

    private static IShapeFix? ResolveFix(FixKind kind) => kind switch
    {
        FixKind.AddPartialModifier => new AddPartialModifierFix(),
        FixKind.AddSealedModifier => new AddSealedModifierFix(),
        FixKind.AddInterfaceToBaseList => new AddInterfaceToBaseListFix(),
        FixKind.AddAttribute => new AddAttributeFix(),
        _ => null,
    };
}
