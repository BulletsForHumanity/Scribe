using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Scribe.Ink.Shapes;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Validates that the custom <c>ShapeFixAllProvider</c> chains multiple
///     Shape fixes that target the same type declaration — the scenario the
///     built-in <c>BatchFixer</c> cannot handle.
/// </summary>
public class ShapeFixAllProviderTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public async Task FixAll_chains_partial_and_sealed_fixes_on_same_type()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .MustBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await RunFixAll(shape, "public class Widget { }", FixAllScope.Document);

        result.ShouldContain("partial");
        result.ShouldContain("sealed");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public async Task FixAll_chains_visibility_plus_remove_modifier_fixes()
    {
        var shape = Shape.Class()
            .MustBePublic()
            .MustNotBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await RunFixAll(shape, "internal sealed class Widget { }", FixAllScope.Document);

        result.ShouldContain("public");
        result.ShouldNotContain("internal");
        result.ShouldNotContain("sealed");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public async Task FixAll_applies_fixes_per_type_independently()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Widget { }
public class Gadget { }
";
        var result = await RunFixAll(shape, source, FixAllScope.Document);

        result.ShouldContain("partial class Widget");
        result.ShouldContain("partial class Gadget");
    }

    private static async Task<string> RunFixAll<TModel>(
        Shape<TModel> shape, string source, FixAllScope scope)
        where TModel : IEquatable<TModel>
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("P", LanguageNames.CSharp)
            .WithMetadataReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)));
        var document = project.AddDocument("T.cs", SourceText.From(source));
        project = document.Project;

        var compilation = await project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Compilation unavailable.");
        var analyzer = shape.ToAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        diagnostics.Length.ShouldBeGreaterThan(0);

        var fixProvider = shape.ToFixProvider();
        var fixAll = fixProvider.GetFixAllProvider();
        fixAll.ShouldNotBeNull();

        var context = new FixAllContext(
            document: document,
            codeFixProvider: fixProvider,
            scope: scope,
            codeActionEquivalenceKey: "Shape.FixAll." + scope,
            diagnosticIds: fixProvider.FixableDiagnosticIds,
            fixAllDiagnosticProvider: new TestDiagnosticProvider(diagnostics),
            cancellationToken: CancellationToken.None);

        var action = await fixAll.GetFixAsync(context).ConfigureAwait(false);
        action.ShouldNotBeNull();

        var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applied = operations.OfType<ApplyChangesOperation>().First();
        var newDoc = applied.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDoc.GetTextAsync().ConfigureAwait(false);
        return text.ToString();
    }

    private sealed class TestDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public TestDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
            => _diagnostics = diagnostics;

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken ct)
            => Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken ct)
            => Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken ct)
            => Task.FromResult<IEnumerable<Diagnostic>>(Enumerable.Empty<Diagnostic>());
    }
}
