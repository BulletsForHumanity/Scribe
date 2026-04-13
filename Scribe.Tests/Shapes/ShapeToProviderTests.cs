using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Cache;
using Scribe.Shapes;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Drives an incremental generator built on <see cref="Shape{TModel}.ToProvider"/>
///     through <see cref="CSharpGeneratorDriver"/> and asserts the projected
///     <see cref="ShapedSymbol{TModel}"/>s surface via a captured output.
/// </summary>
public class ShapeToProviderTests
{
    private readonly record struct CollectedModel(string Fqn, int ViolationCount);

    private sealed class CapturingGenerator : IIncrementalGenerator
    {
        public static readonly List<ShapedSymbol<CollectedModel>> Captured = new();

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var shape = Stencil.ExposeClass()
                .MustHaveAttribute("ThingAttribute")
                .MustBePartial()
                .Etch<CollectedModel>((in ShapeEtchContext ctx) => new CollectedModel(
                    Fqn: ctx.Fqn,
                    ViolationCount: 0));

            var provider = shape.ToProvider(context);

            context.RegisterSourceOutput(provider, (spc, value) =>
            {
                lock (Captured)
                {
                    Captured.Add(value);
                }

                spc.AddSource(
                    $"{value.Model.Fqn.Replace('.', '_').Replace('<', '_').Replace('>', '_')}.g.cs",
                    $"// collected: {value.Fqn}, violations: {value.Violations.Count}");
            });
        }
    }

    private sealed class NoAttributeGenerator : IIncrementalGenerator
    {
        public static readonly List<ShapedSymbol<CollectedModel>> Captured = new();

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var shape = Stencil.ExposeClass()
                .MustBePartial()
                .Etch<CollectedModel>((in ShapeEtchContext ctx) => new CollectedModel(
                    Fqn: ctx.Fqn,
                    ViolationCount: 0));

            context.RegisterSourceOutput(shape.ToProvider(context), (spc, value) =>
            {
                lock (Captured)
                {
                    Captured.Add(value);
                }
            });
        }
    }

    private static CSharpCompilation MakeCompilation(string source)
    {
        return CSharpCompilation.Create(
            "TestAsm",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    cancellationToken: TestContext.Current.CancellationToken),
            ],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.IDisposable).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void ToProvider_WithAttribute_CollectsMatchingTypes_AndFlagsViolations()
    {
        CapturingGenerator.Captured.Clear();

        var compilation = MakeCompilation("""
            using System;
            public sealed class ThingAttribute : Attribute {}

            [Thing] public partial class Alpha {}
            [Thing] public class Beta {}   // not partial -> violation
            public partial class Gamma {}  // not annotated -> ignored
            """);

        var driver = CSharpGeneratorDriver.Create(new CapturingGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(
            compilation, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        result.Diagnostics.IsEmpty.ShouldBeTrue();

        CapturingGenerator.Captured.Count.ShouldBe(2);
        var alpha = CapturingGenerator.Captured.Single(m => m.Fqn == "Alpha");
        alpha.Violations.IsEmpty.ShouldBeTrue();

        var beta = CapturingGenerator.Captured.Single(m => m.Fqn == "Beta");
        beta.Violations.Count.ShouldBe(1);
        beta.Violations[0].Id.ShouldBe("SCRIBE001");
    }

    [Fact]
    public void ToProvider_WithoutAttribute_CollectsAllClassKinds()
    {
        NoAttributeGenerator.Captured.Clear();

        var compilation = MakeCompilation("""
            public partial class Alpha {}
            public class Beta {}         // flagged
            public struct Gamma {}       // wrong kind, filtered out
            public interface IDelta {}   // wrong kind, filtered out
            """);

        var driver = CSharpGeneratorDriver.Create(new NoAttributeGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(
            compilation, TestContext.Current.CancellationToken);

        NoAttributeGenerator.Captured.Count.ShouldBe(2);
        NoAttributeGenerator.Captured.Select(c => c.Fqn).OrderBy(x => x)
            .ShouldBe(["Alpha", "Beta"]);
    }

    [Fact]
    public void ToProvider_ProjectionReceivesAttributeReader()
    {
        var captured = new List<string>();

        var generator = new InlineGenerator(ctx =>
        {
            var shape = Stencil.ExposeClass()
                .MustHaveAttribute("FooAttribute")
                .Etch<ValueModel>((in ShapeEtchContext pc) => new ValueModel(
                    pc.Fqn,
                    pc.Attribute.Ctor<string>(0) ?? "<none>"));

            ctx.RegisterSourceOutput(shape.ToProvider(ctx), (spc, v) =>
            {
                captured.Add($"{v.Model.Fqn}:{v.Model.Value}");
            });
        });

        var compilation = MakeCompilation("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string v) {} }
            [Foo("hello")] public class Target {}
            """);

        CSharpGeneratorDriver.Create(generator).RunGenerators(
            compilation, TestContext.Current.CancellationToken);

        captured.ShouldContain("Target:hello");
    }

    private readonly record struct ValueModel(string Fqn, string Value);

    private sealed class InlineGenerator : IIncrementalGenerator
    {
        private readonly Action<IncrementalGeneratorInitializationContext> _init;
        public InlineGenerator(Action<IncrementalGeneratorInitializationContext> init) => _init = init;
        public void Initialize(IncrementalGeneratorInitializationContext context) => _init(context);
    }
}
