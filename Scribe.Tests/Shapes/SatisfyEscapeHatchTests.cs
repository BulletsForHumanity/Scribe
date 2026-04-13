using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Exercises the generic <c>Satisfy</c> escape hatch surfaced on every
///     <see cref="FocusShape{TFocus}"/> — see <see cref="FocusShapeEscapeHatches"/>.
/// </summary>
public class SatisfyEscapeHatchTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Satisfy_fires_custom_diagnostic_when_member_predicate_false()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m =>
                m.Satisfy(
                    predicate: focus => focus.Symbol.Name.StartsWith('_'),
                    id: "CUSTOM001",
                    title: "Property must start with underscore",
                    message: "Property does not start with underscore",
                    severity: DiagnosticSeverity.Warning))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count { get; init; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "CUSTOM001");
    }

    [Fact]
    public void Satisfy_is_silent_when_predicate_true()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m =>
                m.Satisfy(
                    predicate: static focus => focus.Symbol.DeclaredAccessibility == Accessibility.Public,
                    id: "CUSTOM002",
                    title: "Public-only",
                    message: "nope"))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count { get; init; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Satisfy_with_compilation_overload_can_access_semantic_model()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m =>
                m.Satisfy(
                    predicate: static (focus, compilation, _) =>
                        compilation.GetTypeByMetadataName("System.IDisposable") is not null
                        && focus.Symbol.Name != "Count",
                    id: "CUSTOM003",
                    title: "No property named Count",
                    message: "Property 'Count' is reserved"))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count { get; init; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "CUSTOM003");
    }

    [Fact]
    public void Satisfy_null_predicate_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            Stencil.ExposeRecord()
                .Members(kind: SymbolKind.Property, configure: m =>
                    m.Satisfy(predicate: (Func<MemberFocus, bool>)null!, id: "X", title: "t", message: "m")));
    }

    [Fact]
    public void Satisfy_empty_id_throws()
    {
        Should.Throw<ArgumentException>(() =>
            Stencil.ExposeRecord()
                .Members(kind: SymbolKind.Property, configure: m =>
                    m.Satisfy(predicate: static _ => true, id: "", title: "t", message: "m")));
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer<TModel>(Shape<TModel> shape, string source)
        where TModel : IEquatable<TModel>
    {
        var compilation = Compile(source);
        var analyzer = shape.ToAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
