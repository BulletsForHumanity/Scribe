using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Scribe.Ink.Shapes.Fixes;
using Scribe.Shapes;

namespace Scribe.Ink.Shapes;

/// <summary>
///     Custom <see cref="FixAllProvider"/> that chains Shape fixes targeting the
///     same type declaration. The built-in <see cref="WellKnownFixAllProviders.BatchFixer"/>
///     applies each fix to the original document and merges the results, which
///     fails when multiple diagnostics touch the same node. This provider applies
///     fixes sequentially per-type using a <see cref="SyntaxAnnotation"/> to
///     re-locate the type declaration across edits.
/// </summary>
internal sealed class ShapeFixAllProvider : FixAllProvider
{
    public static readonly ShapeFixAllProvider Instance = new();

    private ShapeFixAllProvider() { }

    public override IEnumerable<FixAllScope> GetSupportedFixAllScopes()
        => new[] { FixAllScope.Document, FixAllScope.Project, FixAllScope.Solution };

    public override Task<CodeAction?> GetFixAsync(FixAllContext context)
    {
        var title = context.Scope switch
        {
            FixAllScope.Document => "Apply Shape fixes (document)",
            FixAllScope.Project => "Apply Shape fixes (project)",
            FixAllScope.Solution => "Apply Shape fixes (solution)",
            _ => "Apply Shape fixes",
        };

        return Task.FromResult<CodeAction?>(CodeAction.Create(
            title: title,
            createChangedSolution: ct => FixAllAsync(context, ct),
            equivalenceKey: "Shape.FixAll." + context.Scope));
    }

    private static async Task<Solution> FixAllAsync(FixAllContext context, CancellationToken ct)
    {
        var solution = context.Solution;

        var documents = CollectDocuments(context, ct);

        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            var diagnostics = await context.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
            if (diagnostics.IsDefaultOrEmpty)
            {
                continue;
            }

            var updated = await FixDocumentAsync(document, diagnostics, ct).ConfigureAwait(false);
            if (updated is null)
            {
                continue;
            }

            solution = updated.Project.Solution;
        }

        return solution;
    }

    private static IReadOnlyList<Document> CollectDocuments(
        FixAllContext context, CancellationToken ct)
    {
        switch (context.Scope)
        {
            case FixAllScope.Document when context.Document is not null:
                return new[] { context.Document };

            case FixAllScope.Project when context.Project is not null:
                return context.Project.Documents.ToArray();

            case FixAllScope.Solution:
                var all = new List<Document>();
                foreach (var project in context.Solution.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    all.AddRange(project.Documents);
                }
                return all;

            default:
                return Array.Empty<Document>();
        }
    }

    private static async Task<Document?> FixDocumentAsync(
        Document document,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
        {
            return null;
        }

        // Group diagnostics by the type declaration they target, then annotate
        // each type so we can re-locate it after every edit.
        var perType = new Dictionary<TypeDeclarationSyntax, List<Diagnostic>>();
        foreach (var diagnostic in diagnostics)
        {
            ct.ThrowIfCancellationRequested();
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDecl is null)
            {
                continue;
            }

            if (!perType.TryGetValue(typeDecl, out var list))
            {
                list = new List<Diagnostic>();
                perType[typeDecl] = list;
            }
            list.Add(diagnostic);
        }

        if (perType.Count == 0)
        {
            return null;
        }

        var annotations = new Dictionary<SyntaxAnnotation, List<Diagnostic>>();
        var perTypeAnnotations = new Dictionary<TypeDeclarationSyntax, SyntaxAnnotation>();
        foreach (var entry in perType)
        {
            var annotation = new SyntaxAnnotation("Scribe.Shape.FixAll", Guid.NewGuid().ToString("N"));
            annotations[annotation] = entry.Value;
            perTypeAnnotations[entry.Key] = annotation;
        }

        var annotated = root.ReplaceNodes(
            perType.Keys,
            (original, _) => original.WithAdditionalAnnotations(perTypeAnnotations[original]));

        var workingDoc = document.WithSyntaxRoot(annotated);

        foreach (var pair in annotations)
        {
            var annotation = pair.Key;
            foreach (var diagnostic in pair.Value)
            {
                ct.ThrowIfCancellationRequested();

                if (!TryResolveFix(diagnostic, out var fix))
                {
                    continue;
                }

                var currentRoot = await workingDoc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (currentRoot is null)
                {
                    continue;
                }

                var typeDecl = currentRoot
                    .GetAnnotatedNodes(annotation)
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (typeDecl is null)
                {
                    continue;
                }

                workingDoc = await fix.FixAsync(workingDoc, typeDecl, diagnostic, ct).ConfigureAwait(false);
            }
        }

        return workingDoc;
    }

    private static bool TryResolveFix(Diagnostic diagnostic, out IShapeFix fix)
    {
        fix = null!;
        if (!diagnostic.Properties.TryGetValue("fixKind", out var fixKindStr)
            || string.IsNullOrEmpty(fixKindStr)
            || !Enum.TryParse<FixKind>(fixKindStr, out var fixKind)
            || fixKind == FixKind.None)
        {
            return false;
        }

        var resolved = FixResolver.Resolve(fixKind);
        if (resolved is null)
        {
            return false;
        }

        fix = resolved;
        return true;
    }
}
