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
///     Exercises the v1 leaf predicates on <see cref="FocusShape{MemberFocus}"/> —
///     <c>MustHaveAttribute</c>, <c>MustBePublic</c>, <c>MustBeStatic</c>,
///     <c>MustBeReadOnly</c> — reached via the <c>Members(...)</c> lens.
/// </summary>
public class MemberFocusLeafPredicatesTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void MustBePublic_fires_when_private_member_present()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Field, configure: m => m.MustBePublic())
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    private int _count;
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE061");
    }

    [Fact]
    public void MustBePublic_is_silent_when_every_member_is_public()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m => m.MustBePublic())
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
    public void MustBeReadOnly_fires_on_mutable_property()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m => m.MustBeReadOnly())
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE063");
    }

    [Fact]
    public void MustBeReadOnly_is_silent_on_init_only_property()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m => m.MustBeReadOnly())
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
    public void MustBeReadOnly_is_silent_on_readonly_field()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Field, configure: m => m.MustBeReadOnly())
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public readonly int Count;
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustBeStatic_fires_on_instance_field()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Field, configure: m => m.MustBeStatic())
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count;
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE062");
    }

    [Fact]
    public void MustHaveAttribute_fires_when_member_missing_attribute()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m =>
                m.MustHaveAttribute("System.ObsoleteAttribute"))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    public int Count { get; init; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE060");
        diagnostics.First(d => d.Id == "SCRIBE060")
            .GetMessage(CultureInfo.InvariantCulture).ShouldContain("Count");
    }

    [Fact]
    public void MustHaveAttribute_is_silent_when_member_has_attribute()
    {
        var shape = Stencil.ExposeRecord()
            .Members(kind: SymbolKind.Property, configure: m =>
                m.MustHaveAttribute("System.ObsoleteAttribute"))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    [System.Obsolete]
    public int Count { get; init; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustBePublic_null_shape_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            MemberFocusLeafPredicates.MustBePublic(null!));
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
