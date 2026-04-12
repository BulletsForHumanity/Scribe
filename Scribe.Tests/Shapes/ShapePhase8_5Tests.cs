using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Scribe.Ink.Shapes;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Exercises the Phase 8.5 additions to the <c>MustBe*</c>/<c>MustNot*</c>
///     catalog: direct negations (SCRIBE002–020), visibility (SCRIBE011–026),
///     declaration kind (SCRIBE019–030), and parameterless constructor
///     (SCRIBE031), along with the matching fix implementations.
/// </summary>
public class ShapePhase8_5Tests
{
    private readonly record struct Collected(string Fqn);

    // ───────────────────────────────────────────────────────────────
    //  Direct negations
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustNotBePartial_emits_SCRIBE002_with_RemovePartialModifier_fix()
    {
        var shape = Shape.Class()
            .MustNotBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public partial class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE002");
        diagnostics[0].Properties["fixKind"].ShouldBe("RemovePartialModifier");
    }

    [Fact]
    public async Task MustNotBePartial_fix_removes_partial_keyword()
    {
        var shape = Shape.Class()
            .MustNotBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public partial class Widget { }");
        result.ShouldNotContain("partial");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public void MustNotBeSealed_emits_SCRIBE006_with_RemoveSealedModifier_fix()
    {
        var shape = Shape.Class()
            .MustNotBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public sealed class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE006");
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveSealedModifier");
    }

    [Fact]
    public async Task MustNotBeSealed_fix_removes_sealed_keyword()
    {
        var shape = Shape.Class()
            .MustNotBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public sealed class Widget { }");
        result.ShouldNotContain("sealed");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public void MustNotHaveAttribute_emits_SCRIBE004_with_RemoveAttribute_fix()
    {
        var shape = Shape.Class()
            .MustNotHaveAttribute("MarkerAttribute")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
[Marker] public class Widget { }
";
        var diagnostics = RunAnalyzer(shape, source)
            .Where(d => d.Id == "SCRIBE004")
            .ToArray();

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveAttribute");
        diagnostics[0].Properties["attribute"].ShouldBe("MarkerAttribute");
    }

    [Fact]
    public async Task MustNotHaveAttribute_fix_removes_attribute_from_type()
    {
        var shape = Shape.Class()
            .MustNotHaveAttribute("MarkerAttribute")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
[Marker] public class Widget { }
";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldNotContain("[Marker]");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public void MustNotBeNamed_emits_SCRIBE030_with_no_fix()
    {
        var shape = Shape.Class()
            .MustNotBeNamed(@"^.*Impl$")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class WidgetImpl { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE030");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustNotBeStatic_emits_SCRIBE018_with_RemoveStaticModifier_fix()
    {
        var shape = Shape.Class()
            .MustNotBeStatic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public static class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE018");
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveStaticModifier");
    }

    [Fact]
    public async Task MustNotBeStatic_fix_removes_static_keyword()
    {
        var shape = Shape.Class()
            .MustNotBeStatic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public static class Widget { }");
        result.ShouldNotContain("static");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public void MustNotExtend_emits_SCRIBE010_and_encodes_baseClass_for_fix()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("MarkerAttribute")
            .MustNotExtend("MyBase")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
public class MyBase { }
[Marker] public class Widget : MyBase { }
";
        var diagnostics = RunAnalyzer(shape, source)
            .Where(d => d.Id == "SCRIBE010")
            .ToArray();

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveFromBaseList");
        diagnostics[0].Properties["baseClass"].ShouldBe("MyBase");
    }

    [Fact]
    public async Task MustNotExtend_fix_removes_base_class_from_base_list()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("MarkerAttribute")
            .MustNotExtend("MyBase")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
public class MyBase { }
[Marker] public class Widget : MyBase, System.IComparable { public int CompareTo(object o) => 0; }
";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldNotContain("MyBase,");
        result.ShouldNotContain(": MyBase");
        result.ShouldContain("IComparable");
    }

    [Fact]
    public void MustNotBeInNamespace_emits_SCRIBE028_with_no_fix()
    {
        var shape = Shape.Class()
            .MustNotBeInNamespace(@"^MyApp\.Legacy(\..*)?$")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "namespace MyApp.Legacy { public class Widget { } }";
        var diagnostics = RunAnalyzer(shape, source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE028");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustBeGeneric_emits_SCRIBE023_on_non_generic_class_with_no_fix()
    {
        var shape = Shape.Class()
            .MustBeGeneric()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE023");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustBeGeneric_allows_generic_class()
    {
        var shape = Shape.Class()
            .MustBeGeneric()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public class Widget<T> { }").ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────
    //  Visibility
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustBePublic_emits_SCRIBE011_with_SetVisibility_fix_and_visibility_property()
    {
        var shape = Shape.Class()
            .MustBePublic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "internal class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE011");
        diagnostics[0].Properties["fixKind"].ShouldBe("SetVisibility");
        diagnostics[0].Properties["visibility"].ShouldBe("public");
    }

    [Fact]
    public async Task MustBePublic_fix_rewrites_visibility_to_public()
    {
        var shape = Shape.Class()
            .MustBePublic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "internal class Widget { }");
        result.ShouldContain("public class Widget");
        result.ShouldNotContain("internal class");
    }

    [Fact]
    public void MustBeInternal_emits_SCRIBE013_with_SetVisibility_fix()
    {
        var shape = Shape.Class()
            .MustBeInternal()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE013");
        diagnostics[0].Properties["visibility"].ShouldBe("internal");
    }

    [Fact]
    public async Task MustBeInternal_fix_rewrites_visibility_to_internal()
    {
        var shape = Shape.Class()
            .MustBeInternal()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public class Widget { }");
        result.ShouldContain("internal class Widget");
        result.ShouldNotContain("public class");
    }

    [Fact]
    public void MustNotBePublic_emits_SCRIBE012_with_no_fix()
    {
        var shape = Shape.Class()
            .MustNotBePublic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE012");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustNotBeInternal_allows_public_class()
    {
        var shape = Shape.Class()
            .MustNotBeInternal()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public class Widget { }").ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────
    //  Declaration kind
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustBeRecord_emits_SCRIBE019_on_plain_class_with_no_fix()
    {
        var shape = Shape.Class()
            .MustBeRecord()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE019");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustBeRecord_allows_record_class()
    {
        var shape = Shape.Class()
            .MustBeRecord()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public record class Widget(int X);").ShouldBeEmpty();
    }

    [Fact]
    public void MustNotBeRecord_emits_SCRIBE020_on_record()
    {
        var shape = Shape.Record()
            .MustNotBeRecord()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public record class Widget(int X);");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE020");
    }

    [Fact]
    public void MustBeValueType_emits_SCRIBE021_on_class_and_skips_struct()
    {
        var shape = Shape.Struct()
            .MustBeValueType()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public struct Widget { }").ShouldBeEmpty();

        var shapeClass = Shape.Class()
            .MustBeValueType()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shapeClass, "public class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE021");
    }

    [Fact]
    public void MustNotBeValueType_emits_SCRIBE022_on_struct()
    {
        var shape = Shape.Struct()
            .MustNotBeValueType()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public struct Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE022");
    }

    // ───────────────────────────────────────────────────────────────
    //  Parameterless constructor
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustHaveParameterlessConstructor_allows_class_with_no_explicit_ctors()
    {
        var shape = Shape.Class()
            .MustHaveParameterlessConstructor()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public class Widget { }").ShouldBeEmpty();
    }

    [Fact]
    public void MustHaveParameterlessConstructor_emits_SCRIBE031_when_only_parameterised_ctor_exists()
    {
        var shape = Shape.Class()
            .MustHaveParameterlessConstructor()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { public Widget(int x) { } }";
        var diagnostics = RunAnalyzer(shape, source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE031");
        diagnostics[0].Properties["fixKind"].ShouldBe("AddParameterlessConstructor");
    }

    [Fact]
    public async Task MustHaveParameterlessConstructor_fix_adds_public_parameterless_ctor()
    {
        var shape = Shape.Class()
            .MustHaveParameterlessConstructor()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget { public Widget(int x) { } }";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldContain("public Widget()");
    }

    // ───────────────────────────────────────────────────────────────
    //  Harness
    // ───────────────────────────────────────────────────────────────

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

    private static async Task<string> ApplyFirstFix<TModel>(Shape<TModel> shape, string source)
        where TModel : IEquatable<TModel>
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("P", LanguageNames.CSharp)
            .WithMetadataReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)));
        var document = project.AddDocument("T.cs", SourceText.From(source));
        project = document.Project;

        var compilation = await project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Compilation unavailable.");
        var analyzer = shape.ToAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var first = diagnostics
            .OrderBy(d => d.Location.SourceSpan.Start)
            .First(d => d.Properties.TryGetValue("fixKind", out var k)
                && !string.IsNullOrEmpty(k)
                && k != "None");

        var fixProvider = shape.ToFixProvider();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            first,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await fixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);

        actions.Count.ShouldBeGreaterThan(0);

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applied = operations.OfType<ApplyChangesOperation>().First();
        var newDoc = applied.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDoc.GetTextAsync().ConfigureAwait(false);
        return text.ToString();
    }
}
