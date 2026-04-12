using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Scribe.Cache;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Validates that <see cref="Shape{TModel}.ToAnalyzer"/> projects a shape's
///     checks into a working <see cref="DiagnosticAnalyzer"/> that emits the
///     expected diagnostics at the configured <see cref="SquiggleAt"/> anchors
///     with the expected fix-dispatch properties.
/// </summary>
public class ShapeAnalyzerTests
{
    private static readonly string[] _scribe001 = ["SCRIBE001"];

    private readonly record struct Collected(string Fqn);

    [Fact]
    public void MustBePartial_emits_SCRIBE001_at_type_keyword_on_non_partial_class()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        var diag = diagnostics[0];
        diag.Id.ShouldBe("SCRIBE001");
        diag.Properties["fixKind"].ShouldBe("AddPartialModifier");
        diag.Properties["squiggleAt"].ShouldBe("TypeKeyword");
        var text = source.Substring(diag.Location.SourceSpan.Start, diag.Location.SourceSpan.Length);
        text.ShouldBe("class");
    }

    [Fact]
    public void MustBePartial_emits_nothing_when_class_is_partial()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public partial class Widget { }");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void MustBeSealed_emits_SCRIBE005_at_type_keyword()
    {
        var shape = Shape.Class()
            .MustBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE005");
        diagnostics[0].Properties["fixKind"].ShouldBe("AddSealedModifier");
    }

    [Fact]
    public void Secondary_MustHaveAttribute_emits_SCRIBE003_and_encodes_attribute_name_in_properties()
    {
        // First MustHaveAttribute is promoted to the selector — it narrows the shape's
        // scope to types that carry ThingAttribute. A second MustHaveAttribute runs as
        // a regular check on those narrowed types.
        var shape = Shape.Class()
            .MustHaveAttribute("ThingAttribute")
            .MustHaveAttribute("MarkerAttribute")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute : System.Attribute { }
public sealed class MarkerAttribute : System.Attribute { }
[Thing] public class Widget { }
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        var diag = diagnostics[0];
        diag.Id.ShouldBe("SCRIBE003");
        diag.Properties["fixKind"].ShouldBe("AddAttribute");
        diag.Properties["attribute"].ShouldBe("MarkerAttribute");
    }

    [Fact]
    public void MustImplement_squiggles_at_base_list_when_present_and_encodes_interface()
    {
        var shape = Shape.Class()
            .MustImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.IComparable { public int CompareTo(object o) => 0; }";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Length.ShouldBe(1);
        var diag = diagnostics[0];
        diag.Id.ShouldBe("SCRIBE007");
        diag.Properties["fixKind"].ShouldBe("AddInterfaceToBaseList");
        diag.Properties["interface"].ShouldBe("System.IDisposable");
        var text = source.Substring(diag.Location.SourceSpan.Start, diag.Location.SourceSpan.Length);
        text.ShouldContain("IComparable");
    }

    [Fact]
    public void Analyzer_ignores_types_that_do_not_carry_the_primary_attribute()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("ThingAttribute")
            .MustBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute : System.Attribute { }
public class Alpha { }                 // no attribute — ignored by MustBePartial
[Thing] public class Beta { }          // has attribute but not partial → SCRIBE001
";
        var diagnostics = RunAnalyzer(shape, source);

        diagnostics.Select(d => d.Id).ShouldBe(_scribe001);
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
