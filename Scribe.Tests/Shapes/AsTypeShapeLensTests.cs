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
///     Exercises <c>AsTypeShape()</c> on <see cref="FocusShape{TypeArgFocus}"/> and
///     <see cref="FocusShape{BaseTypeChainFocus}"/> — re-entering the type-shape world
///     rooted at a generic argument or a base-chain step. Validates that nested
///     type-level lenses (attributes, members, base-type-chain) fire against the
///     inner type and that non-named-type generic arguments pass silently.
/// </summary>
public class AsTypeShapeLensTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void AsTypeShape_on_type_arg_enables_nested_attribute_check_on_the_argument_type()
    {
        // Shape requires: [Thing<T>] on Widget → the T itself must carry [System.Obsolete].
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t =>
                        t.Attributes("System.ObsoleteAttribute", min: 1))))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // Payload is NOT marked Obsolete → inner attributes min:1 fires on its TypeFocus.
        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE050");
    }

    [Fact]
    public void AsTypeShape_on_type_arg_is_silent_when_nested_constraint_is_satisfied()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t =>
                        t.Attributes("System.ObsoleteAttribute", min: 1))))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
[System.Obsolete(""deprecated"")] public class Payload { }
[Thing<Payload>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void AsTypeShape_on_type_arg_silent_passes_for_non_named_type_arguments()
    {
        // T = int[] (an array, IArrayTypeSymbol not INamedTypeSymbol) → navigation yields
        // no TypeFocus → inner Attributes(...) min:1 has 0 parents to fire against →
        // silent pass.
        var shape = Stencil.ExposeRecord()
            .Attributes("ThingAttribute", configure: attr =>
                attr.GenericTypeArg(index: 0, configure: arg =>
                    arg.AsTypeShape(t =>
                        t.Attributes("System.ObsoleteAttribute", min: 1))))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute<T> : System.Attribute { }
[Thing<int[]>] public record Widget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void AsTypeShape_on_base_chain_enables_nested_attribute_check_on_each_base()
    {
        // Require every base in the chain to carry [System.Obsolete]. Gadget doesn't.
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(configure: step =>
                step.AsTypeShape(t =>
                    t.Attributes("System.ObsoleteAttribute", min: 1)))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Gadget { }
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        // Widget's base chain = [Gadget] → Gadget has no Obsolete → 1 violation.
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE050");
    }

    [Fact]
    public void AsTypeShape_on_base_chain_is_silent_when_every_base_satisfies_nested_constraint()
    {
        var shape = Stencil.ExposeRecord()
            .BaseTypeChain(configure: step =>
                step.AsTypeShape(t =>
                    t.Attributes("System.ObsoleteAttribute", min: 1)))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
[System.Obsolete(""d"")] public class Gadget { }
public record Widget : Gadget;
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
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
