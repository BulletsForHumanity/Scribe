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
///     Exercises <see cref="ConstructorArgFocusLeafPredicates"/> and
///     <see cref="NamedArgFocusLeafPredicates"/> — <c>MustBe</c> and
///     <c>MustNotBeEmpty</c> — on positional and named attribute arguments.
/// </summary>
public class ConstructorAndNamedArgLeafPredicatesTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void ConstructorArg_MustBe_fires_on_mismatch()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, configure: a => a.MustBe("deprecated")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"please don't\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE080");
    }

    [Fact]
    public void ConstructorArg_MustBe_is_silent_on_match()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, configure: a => a.MustBe("deprecated")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void ConstructorArg_MustNotBeEmpty_fires_on_empty_string()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, configure: a => a.MustNotBeEmpty()))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE081");
    }

    [Fact]
    public void ConstructorArg_MustNotBeEmpty_is_silent_on_non_empty_string()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, configure: a => a.MustNotBeEmpty()))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void NamedArg_MustBe_fires_on_mismatch()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.NamedArg<bool>(name: "Flag", configure: a => a.MustBe(true)))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute : System.Attribute { public bool Flag { get; set; } }
[Thing(Flag = false)] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE085");
    }

    [Fact]
    public void NamedArg_MustBe_is_silent_on_match()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.NamedArg<bool>(name: "Flag", configure: a => a.MustBe(true)))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute : System.Attribute { public bool Flag { get; set; } }
[Thing(Flag = true)] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void NamedArg_MustNotBeEmpty_fires_on_whitespace()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.NamedArg<string>(name: "DiagnosticId", configure: a => a.MustNotBeEmpty()))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\", DiagnosticId = \"   \")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE086");
    }

    [Fact]
    public void ConstructorArg_MustBe_null_shape_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            ConstructorArgFocusLeafPredicates.MustBe<string>(null!, "x"));
    }

    [Fact]
    public void NamedArg_MustBe_null_shape_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            NamedArgFocusLeafPredicates.MustBe<bool>(null!, true));
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
