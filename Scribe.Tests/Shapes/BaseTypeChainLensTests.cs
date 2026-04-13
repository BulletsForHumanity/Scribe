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
///     Exercises <c>TypeShape.BaseTypeChain(configure, min, max)</c> end-to-end against
///     the analyzer materialisation path. Validates inheritance navigation, excludes
///     <see cref="object"/> from the chain, and enforces presence constraints.
/// </summary>
public class BaseTypeChainLensTests
{
    private readonly record struct Collected(string Fqn);

    // Tests target record types (ExposeRecord) so the `class Gadget { }` base in the
    // source is filtered out by TypeKindFilter before checks/lens branches run — no
    // stray diagnostics on the base type contaminate the test assertions.

    [Fact]
    public void BaseTypeChain_min1_emits_violation_when_type_inherits_directly_from_object()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE052");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 0");
    }

    [Fact]
    public void BaseTypeChain_min1_is_silent_when_type_inherits_from_a_non_object_base()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // Gadget is a class (not a record), so it's filtered out by ExposeRecord.
        // Only Widget is evaluated, and it has 1 base → passes.
        var source = @"
public class Gadget { }
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void BaseTypeChain_max0_emits_violation_when_type_has_a_non_object_base()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(max: 0)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Gadget;
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        // Widget has 1 base; Gadget has 0 (passes max:0). Only Widget emits.
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE052");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 1");
    }

    [Fact]
    public void BaseTypeChain_excludes_system_object()
    {
        // Widget extends Gadget which itself extends object. The chain must stop at
        // the object boundary: observed 1 (not 2), so min:2 triggers a violation on
        // Widget. Gadget has chain length 0 — also violates min:2.
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(min: 2)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Gadget;
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(2);
        diagnostics.ShouldContain(d =>
            d.GetMessage(CultureInfo.InvariantCulture).Contains("observed 1"));
        diagnostics.ShouldContain(d =>
            d.GetMessage(CultureInfo.InvariantCulture).Contains("observed 0"));
    }

    [Fact]
    public void BaseTypeChain_walks_transitively_through_grandparent()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(min: 2)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Root;
public record Gadget : Root;
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        // Widget: chain = [Gadget, Root] → length 2 (pass)
        // Gadget: chain = [Root]          → length 1 (fail)
        // Root:   chain = []              → length 0 (fail)
        diagnostics.Length.ShouldBe(2);
    }

    [Fact]
    public void BaseTypeChain_custom_presence_spec_overrides_id_and_severity()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(
                min: 1,
                presenceSpec: new DiagnosticSpec(Id: "CUST300", Severity: DiagnosticSeverity.Warning))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("CUST300");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void BaseTypeChain_negative_min_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stencil.ExposeClass().BaseTypeChain(min: -1));
    }

    [Fact]
    public void BaseTypeChain_max_less_than_min_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stencil.ExposeClass().BaseTypeChain(min: 5, max: 3));
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
