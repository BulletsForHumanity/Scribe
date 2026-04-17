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
///     Exercises <see cref="TypeArgFocusLeafPredicates"/> — <c>MustImplement</c> and
///     <c>MustExtend</c> — reached via a generic type argument on an attribute
///     application.
/// </summary>
public class TypeArgFocusLeafPredicatesTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void MustImplement_fires_when_type_arg_does_not_implement_interface()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.MustImplement("System.IDisposable")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE070");
    }

    [Fact]
    public void MustImplement_is_silent_when_type_arg_implements_interface()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.MustImplement("System.IDisposable")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload : System.IDisposable { public void Dispose() { } }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustExtend_fires_when_type_arg_does_not_derive_from_base()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.MustExtend("System.Exception")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldContain(d => d.Id == "SCRIBE071");
    }

    [Fact]
    public void MustExtend_is_silent_when_type_arg_derives_transitively()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.MustExtend("System.Exception")))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload : System.InvalidOperationException { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustImplement_null_shape_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            TypeArgFocusLeafPredicates.MustImplement(null!, "System.IDisposable"));
    }

    [Fact]
    public void MustImplement_empty_metadata_name_throws()
    {
        Should.Throw<ArgumentException>(() =>
            Stencil.ExposeRecord().Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg => arg.MustImplement(""))));
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
