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
///     Validates the Scriptorium-generated severity variants:
///     <c>Should*</c> demotes the default <see cref="DiagnosticSeverity.Error"/> to
///     <see cref="DiagnosticSeverity.Warning"/>, <c>Could*</c> demotes to
///     <see cref="DiagnosticSeverity.Info"/>. All three variants share the same
///     diagnostic id as the <c>Must*</c> primitive, so <c>#pragma warning disable</c>
///     silences them uniformly. A caller-supplied severity in the spec wins.
/// </summary>
public class ShapeSeverityVariantsTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void ShouldBePartial_reports_SCRIBE001_at_Warning_severity()
    {
        var shape = Stencil.ExposeClass()
            .ShouldBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE001");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void CouldBePartial_reports_SCRIBE001_at_Info_severity()
    {
        var shape = Stencil.ExposeClass()
            .CouldBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE001");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Info);
    }

    [Fact]
    public void MustBePartial_ShouldBePartial_and_CouldBePartial_all_share_SCRIBE001()
    {
        var must = Stencil.ExposeClass().MustBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));
        var should = Stencil.ExposeClass().ShouldBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));
        var could = Stencil.ExposeClass().CouldBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        const string source = "public class Widget { }";

        RunAnalyzer(must, source).Single().Id.ShouldBe("SCRIBE001");
        RunAnalyzer(should, source).Single().Id.ShouldBe("SCRIBE001");
        RunAnalyzer(could, source).Single().Id.ShouldBe("SCRIBE001");
    }

    [Fact]
    public void Pragma_warning_disable_SCRIBE001_silences_the_ShouldBe_variant()
    {
        var shape = Stencil.ExposeClass()
            .ShouldBePartial()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        const string source = @"
#pragma warning disable SCRIBE001
public class Widget { }
#pragma warning restore SCRIBE001
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Caller_supplied_severity_wins_over_variant_default()
    {
        // ShouldBePartial defaults to Warning, but the caller pins Hidden.
        var shape = Stencil.ExposeClass()
            .ShouldBePartial(new DiagnosticSpec(Severity: DiagnosticSeverity.Hidden))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Hidden);
    }

    [Fact]
    public void ShouldImplement_generic_variant_flows_through_type_parameter_constraint()
    {
        // Exercises the Scriptorium-generated generic overload with its
        // `where T : class` constraint, plus severity demotion.
        var shape = Stencil.ExposeClass()
            .ShouldImplement<System.IDisposable>()
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE007");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
        diagnostics[0].Properties["interface"].ShouldBe("System.IDisposable");
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
