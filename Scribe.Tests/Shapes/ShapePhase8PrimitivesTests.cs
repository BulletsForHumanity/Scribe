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
///     Exercises the Phase 8 extensions to the <c>MustBe*</c>/<c>MustNot*</c>
///     catalog: analyzer emissions for SCRIBE015–012 and the matching code
///     fix application. Each test pairs an analyzer assertion with a fix
///     assertion where a fix exists.
/// </summary>
public class ShapePhase8PrimitivesTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void MustBeAbstract_emits_SCRIBE015_with_AddAbstractModifier_fix_kind()
    {
        var shape = Shape.Class()
            .MustBeAbstract()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE015");
        diagnostics[0].Properties["fixKind"].ShouldBe("AddAbstractModifier");
    }

    [Fact]
    public void MustBeAbstract_emits_nothing_when_already_abstract()
    {
        var shape = Shape.Class()
            .MustBeAbstract()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public abstract class Widget { }").ShouldBeEmpty();
    }

    [Fact]
    public async Task MustBeAbstract_fix_adds_abstract_keyword()
    {
        var shape = Shape.Class()
            .MustBeAbstract()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public class Widget { }");
        result.ShouldContain("abstract class Widget");
    }

    [Fact]
    public void MustBeStatic_emits_SCRIBE017_with_AddStaticModifier_fix_kind()
    {
        var shape = Shape.Class()
            .MustBeStatic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE017");
        diagnostics[0].Properties["fixKind"].ShouldBe("AddStaticModifier");
    }

    [Fact]
    public async Task MustBeStatic_fix_adds_static_keyword()
    {
        var shape = Shape.Class()
            .MustBeStatic()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public class Widget { }");
        result.ShouldContain("static class Widget");
    }

    [Fact]
    public void MustExtend_emits_SCRIBE009_and_encodes_base_class_in_fix_properties()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("MarkerAttribute")
            .MustExtend("MyBase")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
public class MyBase { }
[Marker] public class Widget { }
";
        var diagnostics = RunAnalyzer(shape, source)
            .Where(d => d.Id == "SCRIBE009")
            .ToArray();

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Properties["fixKind"].ShouldBe("AddBaseClass");
        diagnostics[0].Properties["baseClass"].ShouldBe("MyBase");
    }

    [Fact]
    public void MustExtend_emits_nothing_when_already_extends()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("MarkerAttribute")
            .MustExtend("MyBase")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
public class MyBase { }
[Marker] public class Widget : MyBase { }
";
        RunAnalyzer(shape, source).Where(d => d.Id == "SCRIBE009").ShouldBeEmpty();
    }

    [Fact]
    public async Task MustExtend_fix_inserts_base_class_at_head_of_base_list()
    {
        var shape = Shape.Class()
            .MustHaveAttribute("MarkerAttribute")
            .MustExtend("MyBase")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class MarkerAttribute : System.Attribute { }
public class MyBase { }
[Marker] public class Widget : System.IComparable { public int CompareTo(object o) => 0; }
";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldContain("Widget : MyBase");
        result.ShouldContain("IComparable");
    }

    [Fact]
    public void MustBeInNamespace_emits_SCRIBE027_with_no_fix()
    {
        var shape = Shape.Class()
            .MustBeInNamespace(@"^MyApp\.Domain(\..*)?$")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
namespace MyApp.Other { public class Widget { } }
";
        var diagnostics = RunAnalyzer(shape, source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE027");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustBeInNamespace_allows_matching_namespace()
    {
        var shape = Shape.Class()
            .MustBeInNamespace(@"^MyApp\.Domain(\..*)?$")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
namespace MyApp.Domain.Things { public class Widget { } }
";
        RunAnalyzer(shape, source).ShouldBeEmpty();
    }

    [Fact]
    public void MustNotBeAbstract_emits_SCRIBE016_with_RemoveAbstractModifier_fix()
    {
        var shape = Shape.Class()
            .MustNotBeAbstract()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public abstract class Widget { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE016");
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveAbstractModifier");
    }

    [Fact]
    public async Task MustNotBeAbstract_fix_removes_abstract_keyword()
    {
        var shape = Shape.Class()
            .MustNotBeAbstract()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public abstract class Widget { }");
        result.ShouldNotContain("abstract");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public void MustNotBeGeneric_emits_SCRIBE024_with_no_fix_on_generic_class()
    {
        var shape = Shape.Class()
            .MustNotBeGeneric()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var diagnostics = RunAnalyzer(shape, "public class Widget<T> { }");
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE024");
        diagnostics[0].Properties["fixKind"].ShouldBe("None");
    }

    [Fact]
    public void MustNotBeGeneric_allows_non_generic_class()
    {
        var shape = Shape.Class()
            .MustNotBeGeneric()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        RunAnalyzer(shape, "public class Widget { }").ShouldBeEmpty();
    }

    [Fact]
    public void MustNotImplement_emits_SCRIBE008_and_encodes_interface_for_fix()
    {
        var shape = Shape.Class()
            .MustNotImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.IDisposable { public void Dispose() { } }";
        var diagnostics = RunAnalyzer(shape, source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE008");
        diagnostics[0].Properties["fixKind"].ShouldBe("RemoveFromBaseList");
        diagnostics[0].Properties["interface"].ShouldBe("System.IDisposable");
    }

    [Fact]
    public async Task MustNotImplement_fix_removes_forbidden_interface_from_base_list()
    {
        var shape = Shape.Class()
            .MustNotImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.IComparable, System.IDisposable { public int CompareTo(object o) => 0; public void Dispose() { } }";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldContain("IComparable");
        result.ShouldNotContain("IDisposable");
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
