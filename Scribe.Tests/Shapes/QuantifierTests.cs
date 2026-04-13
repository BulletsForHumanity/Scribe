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
///     Exercises the B.6 set-level quantifiers on lens branches — <see cref="Quantifier.Any"/>
///     (at least one child must pass) and <see cref="Quantifier.None"/> (no child may pass) —
///     across Members, Attributes, and BaseTypeChain entry points.
/// </summary>
public class QuantifierTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Members_Any_is_silent_when_at_least_one_member_passes()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustBePublic(),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Widget
{
    private int _hidden { get; set; }
    public int Visible { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Members_Any_emits_one_aggregate_when_no_member_passes()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustBePublic(),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Widget
{
    private int A { get; set; }
    private int B { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE090");
    }

    [Fact]
    public void Members_None_is_silent_when_no_member_matches()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustBePublic(),
                quantifier: Quantifier.None)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // None property is public — None quantifier requires every child to FAIL `MustBePublic`,
        // i.e. no public property exists.
        var source = @"
public class Widget
{
    private int A { get; set; }
    private int B { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Members_None_fires_per_offender_when_one_child_passes()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustBePublic(),
                quantifier: Quantifier.None)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Widget
{
    public int A { get; set; }
    public int B { get; set; }
    private int C { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(2);
        diagnostics.ShouldAllBe(d => d.Id == "SCRIBE091");
    }

    [Fact]
    public void Attributes_Any_silent_when_one_application_passes()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes(
                "Ns.ThingAttribute",
                configure: a => a.ConstructorArg<string>(0, p => p.MustBe("ok")),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
namespace Ns
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class ThingAttribute : System.Attribute
    {
        public ThingAttribute(string value) { }
    }

    [Thing(""no"")]
    [Thing(""ok"")]
    public record Widget;
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Attributes_Any_fires_aggregate_when_no_application_passes()
    {
        var shape = Stencil.ExposeRecord()
            .Attributes(
                "Ns.ThingAttribute",
                configure: a => a.ConstructorArg<string>(0, p => p.MustBe("ok")),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
namespace Ns
{
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class ThingAttribute : System.Attribute
    {
        public ThingAttribute(string value) { }
    }

    [Thing(""no"")]
    [Thing(""nope"")]
    public record Widget;
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE092");
    }

    [Fact]
    public void BaseTypeChain_Any_silent_when_chain_step_passes()
    {
        var shape = Stencil.ExposeClass()
            .BaseTypeChain(
                configure: b => b.AsTypeShape(t => t.MustBeNamed("Exception")),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.Exception { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void BaseTypeChain_Any_fires_aggregate_when_no_step_matches()
    {
        var shape = Stencil.ExposeClass()
            .BaseTypeChain(
                configure: b => b.AsTypeShape(t => t.MustBeNamed("Missing")),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.Exception { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Any(d => d.Id == "SCRIBE094").ShouldBeTrue();
    }

    [Fact]
    public void Custom_quantifierSpec_overrides_id_and_severity()
    {
        var shape = Stencil.ExposeClass()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustBePublic(),
                quantifier: Quantifier.Any,
                quantifierSpec: new DiagnosticSpec(
                    Id: "CUST700",
                    Severity: DiagnosticSeverity.Warning,
                    Message: "Need at least one public property"))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public class Widget
{
    private int A { get; set; }
}
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("CUST700");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Any_folds_leaf_predicate_result_into_pass_fail()
    {
        // Child "passes" only when every nested leaf predicate returns true. Any
        // succeeds as soon as one property carries the required attribute.
        var shape = Stencil.ExposeRecord()
            .Members(
                kind: SymbolKind.Property,
                configure: m => m.MustHaveAttribute("System.ObsoleteAttribute"),
                quantifier: Quantifier.Any)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public record Widget
{
    [System.Obsolete]
    public int Tagged { get; init; }
    public int Untagged { get; init; }
}
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
