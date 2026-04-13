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
///     Exercises the attribute sub-lenses — <c>GenericTypeArg</c>, <c>ConstructorArg&lt;T&gt;</c>,
///     and <c>NamedArg&lt;T&gt;</c> — end-to-end against the analyzer materialisation
///     path. Validates navigation, presence constraints, and silent-pass semantics
///     when an argument does not match the requested shape.
/// </summary>
public class AttributeSubLensTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void ConstructorArg_min1_emits_violation_when_attribute_has_no_matching_arg_at_index()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // [Obsolete] with no arguments — ConstructorArg<string>(0) yields nothing.
        var source = "[System.Obsolete] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE054");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 0");
    }

    [Fact]
    public void ConstructorArg_min1_is_silent_when_matching_arg_is_present()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: 0, min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void ConstructorArg_type_mismatch_yields_silent_pass()
    {
        // Arg is string but we ask for int → TryCoerce fails → 0 observed.
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<int>(index: 0, min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE054");
    }

    [Fact]
    public void NamedArg_min1_emits_violation_when_named_arg_is_absent()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.NamedArg<bool>(name: "DiagnosticId", min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE055");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("DiagnosticId");
    }

    [Fact]
    public void NamedArg_min1_is_silent_when_named_arg_is_present()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.NamedArg<string>(name: "DiagnosticId", min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete(\"deprecated\", DiagnosticId = \"X1\")] public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GenericTypeArg_min1_is_silent_when_generic_arg_is_present()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, min: 1))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
[Thing<string>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void ConstructorArg_negative_index_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stencil.ExposeRecord().Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.ConstructorArg<string>(index: -1)));
    }

    [Fact]
    public void NamedArg_empty_name_throws()
    {
        Should.Throw<ArgumentException>(() =>
            Stencil.ExposeRecord().Attributes("System.ObsoleteAttribute", configure: attr =>
                attr.NamedArg<string>(name: "")));
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
