using System;
using System.Collections.Immutable;
using System.Globalization;
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
///     B.8 — verifies that lens branches stamp a human-readable focus-path
///     breadcrumb onto every diagnostic they produce, so readers can tell which
///     navigation hops led to a violation (e.g. <c>[Attributes("Foo")] ...</c>).
/// </summary>
public class FocusPathTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Top_level_check_has_no_focus_path_prefix()
    {
        var shape = Stencil.ExposeClass()
            .MustBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldNotStartWith("[");
    }

    [Fact]
    public void Attributes_presence_violation_includes_attributes_hop_in_path()
    {
        var shape = Stencil.ExposeClass()
            .Attributes("System.ObsoleteAttribute", min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        var message = diagnostics[0].GetMessage(CultureInfo.InvariantCulture);
        message.ShouldStartWith("[Attributes(\"System.ObsoleteAttribute\")]");
    }

    [Fact]
    public void Nested_sub_lens_violation_includes_full_hop_chain()
    {
        // Attributes → GenericTypeArg(0, min=1) — class has no generic arg, so
        // the inner presence check fires. Path should include both hops.
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        var message = diagnostics[0].GetMessage(CultureInfo.InvariantCulture);
        message.ShouldContain("Attributes(\"System.ObsoleteAttribute\")");
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
