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
///     Exercises the <c>TypeShape.Attributes(fqn, min, max)</c> entry point end-to-end
///     against the analyzer materialisation path. Validates that the lens navigates
///     matching attribute applications, that presence constraints emit a diagnostic
///     at the parent type's span, and that the custom presence spec is honoured.
/// </summary>
public class AttributesLensTests
{
    private readonly record struct Collected(string Fqn);

    // Uses System.ObsoleteAttribute so the test source only contains a single class (Widget).
    // Attribute-derived classes in the source would otherwise be matched by ExposeClass()
    // and produce their own presence-violation diagnostics.

    [Fact]
    public void Attributes_min1_emits_presence_violation_when_no_attribute_is_applied()
    {
        var shape = Stencil.ExposeClass()
            .Attributes("System.ObsoleteAttribute", min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE050");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("ObsoleteAttribute");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 0");
    }

    [Fact]
    public void Attributes_min1_is_silent_when_attribute_is_applied()
    {
        var shape = Stencil.ExposeClass()
            .Attributes("System.ObsoleteAttribute", min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "[System.Obsolete] public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Attributes_max1_emits_presence_violation_when_attribute_is_applied_twice()
    {
        var shape = Stencil.ExposeClass()
            .Attributes("ThingAttribute", min: 0, max: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = @"
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
public sealed class ThingAttribute : System.Attribute { }
[Thing][Thing] public class Widget { }
";
        var diagnostics = RunAnalyzer(shape, source);

        // ThingAttribute has 0 Thing applications (passes max:1); Widget has 2 (violates).
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE050");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("observed 2");
    }

    [Fact]
    public void Attributes_custom_presence_spec_overrides_id_and_severity()
    {
        var shape = Stencil.ExposeClass()
            .Attributes(
                "System.ObsoleteAttribute",
                min: 1,
                presenceSpec: new DiagnosticSpec(Id: "CUST100", Severity: DiagnosticSeverity.Warning))
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("CUST100");
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Attributes_presence_violation_squiggles_at_type_origin()
    {
        var shape = Stencil.ExposeClass()
            .Attributes("System.ObsoleteAttribute", min: 1)
            .Etch<Collected>((in ShapeEtchContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Location.SourceSpan.Length.ShouldBeGreaterThan(0);
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
