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
///     Exercises <c>TypeShape.Members(kind?, configure, min, max)</c> end-to-end against
///     the analyzer materialisation path. Validates kind filtering, source-order
///     navigation, and presence-constraint diagnostics.
/// </summary>
public class MembersLensTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Members_min1_emits_presence_violation_when_type_has_no_declared_members()
    {
        var shape = Stencil.ExposeClass()
            .Members(min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE051");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 0");
    }

    [Fact]
    public void Members_min1_is_silent_when_type_has_declared_member()
    {
        var shape = Stencil.ExposeClass()
            .Members(min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { public int Size; }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Members_kind_filter_only_counts_matching_members()
    {
        var shape = Stencil.ExposeClass()
            .Members(kind: SymbolKind.Property, min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // Widget has a field but no property — should violate the Property min:1 constraint.
        var source = "public class Widget { public int Size; }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE051");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 0");
    }

    [Fact]
    public void Members_kind_filter_silent_when_matching_member_present()
    {
        var shape = Stencil.ExposeClass()
            .Members(kind: SymbolKind.Property, min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { public int Size { get; set; } }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Members_max_violation_fires_when_count_exceeds_max()
    {
        var shape = Stencil.ExposeClass()
            .Members(kind: SymbolKind.Field, max: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { public int A; public int B; public int C; }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE051");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 3");
    }

    [Fact]
    public void Members_custom_presence_spec_overrides_id_and_severity()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                min: 1,
                presenceSpec: new DiagnosticSpec(Id: "CUST200", Severity: DiagnosticSeverity.Info))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("CUST200");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Info);
    }

    [Fact]
    public void Members_negative_min_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stencil.ExposeClass().Members(min: -1));
    }

    [Fact]
    public void Members_max_less_than_min_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stencil.ExposeClass().Members(min: 5, max: 3));
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
