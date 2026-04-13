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
///     Exercises the representative v1 subset of leaf predicates on
///     <see cref="FocusShape{TypeFocus}"/> — <c>MustBePartial</c>, <c>MustBeSealed</c>,
///     <c>MustBeAbstract</c>, <c>MustBeStatic</c>, <c>MustHaveAttribute</c>,
///     <c>MustBeNamed</c> — composed through <c>AsTypeShape()</c> so the checks run on
///     a nested <see cref="TypeFocus"/> reached via a lens.
/// </summary>
public class TypeFocusLeafPredicatesTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void MustBeSealed_fires_when_nested_type_argument_is_not_sealed()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t => t.MustBeSealed())))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE005");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("Payload");
    }

    [Fact]
    public void MustBeSealed_is_silent_when_nested_type_argument_is_sealed()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t => t.MustBeSealed())))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public sealed class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustBeAbstract_fires_when_nested_base_is_concrete()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(configure: step =>
                step.AsTypeShape(t => t.MustBeAbstract()))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Gadget { }
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE015");
    }

    [Fact]
    public void MustBeAbstract_is_silent_when_nested_base_is_abstract()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(configure: step =>
                step.AsTypeShape(t => t.MustBeAbstract()))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public abstract class Gadget { }
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustHaveAttribute_fires_when_nested_type_argument_is_missing_required_attribute()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t => t.MustHaveAttribute("System.ObsoleteAttribute"))))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE003");
    }

    [Fact]
    public void MustBeNamed_fires_when_nested_type_argument_name_violates_pattern()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t => t.MustBeNamed(@"^[A-Z][a-zA-Z0-9]*Dto$"))))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE029");
    }

    [Fact]
    public void MustBePartial_and_MustBeStatic_compose_on_nested_type_focus()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t => t.MustBePartial().MustBeStatic())))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        // Payload is neither partial nor static → both fire.
        diagnostics.Length.ShouldBe(2);
        diagnostics.ShouldContain(d => d.Id == "SCRIBE001");
        diagnostics.ShouldContain(d => d.Id == "SCRIBE017");
    }

    [Fact]
    public void MustBeSealed_null_shape_throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            TypeFocusLeafPredicates.MustBeSealed(null!));
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
