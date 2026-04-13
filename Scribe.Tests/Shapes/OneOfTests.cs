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
///     Exercises <see cref="OneOfTypeShape"/> — B.5 disjunction composition.
///     Verifies that a symbol passes when at least one alternative is silent
///     against it, that a fused diagnostic summarises every branch's unsatisfied
///     checks when no alternative passes, and that kind-mismatched branches
///     cannot accidentally satisfy a symbol.
/// </summary>
public class OneOfTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Stencil_OneOf_passes_when_first_alternative_is_silent()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly().MustBePartial();
        var abstractRecord = Stencil.ExposeRecord().MustBeAbstract().MustBePartial();

        var shape = Stencil.OneOf(readonlyStruct, abstractRecord)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public readonly partial record struct Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Stencil_OneOf_passes_when_second_alternative_is_silent()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly().MustBePartial();
        var abstractRecord = Stencil.ExposeRecord().MustBeAbstract().MustBePartial();

        var shape = Stencil.OneOf(readonlyStruct, abstractRecord)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public abstract partial record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Stencil_OneOf_fires_fused_diagnostic_when_no_alternative_passes()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly().MustBePartial();
        var abstractRecord = Stencil.ExposeRecord().MustBeAbstract().MustBePartial();

        var shape = Stencil.OneOf(readonlyStruct, abstractRecord)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // A mutable, non-partial record — neither alternative can satisfy.
        var source = "public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE100");
        var msg = diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        msg.ShouldContain("branch1");
        msg.ShouldContain("branch2");
        msg.ShouldContain("—OR—");
    }

    [Fact]
    public void TypeShape_OneOf_instance_method_composes_with_sibling()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly().MustBePartial();
        var abstractRecord = Stencil.ExposeRecord().MustBeAbstract().MustBePartial();

        var shape = readonlyStruct.OneOf(abstractRecord)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public readonly partial record struct Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void OneOf_three_branch_fusion_lists_all_branch_ids()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly();
        var abstractClass = Stencil.ExposeClass().MustBeAbstract().MustBePartial();
        var sealedClass = Stencil.ExposeClass().MustBeSealed();

        var shape = Stencil.OneOf(readonlyStruct, abstractClass, sealedClass)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // A plain non-sealed, non-abstract class — neither of the three branches passes.
        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE100");
        var msg = diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        msg.ShouldContain("branch1");
        msg.ShouldContain("branch2");
        msg.ShouldContain("branch3");
    }

    [Fact]
    public void OneOf_custom_fusion_spec_overrides_id_and_severity()
    {
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly();
        var abstractRecord = Stencil.ExposeRecord().MustBeAbstract();

        var shape = Stencil.OneOf(
                fusionSpec: new DiagnosticSpec(
                    Id: "CUST800",
                    Severity: DiagnosticSeverity.Warning,
                    Message: "Expected [{0}] alternatives; none matched: {1}"),
                alternatives: new[] { readonlyStruct, abstractRecord })
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public record Widget;";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("CUST800");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void OneOf_kind_mismatched_branch_does_not_accidentally_pass()
    {
        // Branch 1 is for record-structs; branch 2 for classes. A class should
        // be evaluated by branch 2 only — if branch 1 were silently treated as
        // "irrelevant => passes", the fusion wouldn't fire.
        var readonlyStruct = Stencil.ExposeRecordStruct().MustBeReadOnly();
        var abstractClass = Stencil.ExposeClass().MustBeAbstract();

        var shape = Stencil.OneOf(readonlyStruct, abstractClass)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        // Plain class — branch 2 fails MustBeAbstract; branch 1 is kind-mismatched.
        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE100");
    }

    [Fact]
    public void OneOf_requires_at_least_two_alternatives()
    {
        Should.Throw<ArgumentException>(() =>
            Stencil.OneOf(Stencil.ExposeClass()));
    }

    [Fact]
    public void OneOf_rejects_alternatives_with_differing_primary_attribute()
    {
        var left = Stencil.ExposeClass().MustHaveAttribute("System.ObsoleteAttribute");
        var right = Stencil.ExposeClass();

        // Shapes with asymmetric primary-attribute drivers aren't fusable —
        // ForAttributeWithMetadataName can only target one name per analyzer pass.
        // v1 enforces this at authoring time rather than silently losing branches.
        Should.Throw<ArgumentException>(() => Stencil.OneOf(left, right));
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
