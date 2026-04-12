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
///     Validates that <see cref="ShapeInkExtensions.ToFixProvider"/> dispatches
///     each <see cref="FixKind"/> to the correct code action and produces the
///     expected source transformation.
/// </summary>
public class ShapeFixProviderTests
{
    private static readonly string[] _allThreeIds = ["SCRIBE001", "SCRIBE002", "SCRIBE003"];

    private readonly record struct Collected(string Fqn);

    [Fact]
    public async Task AddPartialModifier_adds_partial_keyword()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public class Widget { }");
        result.ShouldContain("partial class Widget");
    }

    [Fact]
    public async Task AddSealedModifier_adds_sealed_keyword_before_partial()
    {
        var shape = Shape.Class()
            .MustBeSealed()
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public partial class Widget { }");
        result.ShouldContain("sealed partial class Widget");
    }

    [Fact]
    public async Task AddInterfaceToBaseList_appends_interface_when_base_list_absent()
    {
        var shape = Shape.Class()
            .MustImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var result = await ApplyFirstFix(shape, "public sealed class Widget { public void Dispose() { } }");
        result.ShouldContain(": System.IDisposable");
    }

    [Fact]
    public async Task AddInterfaceToBaseList_appends_to_existing_base_list()
    {
        var shape = Shape.Class()
            .MustImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = "public class Widget : System.IComparable { public int CompareTo(object o) => 0; public void Dispose() { } }";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldContain("IComparable");
        result.ShouldContain("IDisposable");
    }

    [Fact]
    public async Task AddAttribute_prepends_attribute_without_Attribute_suffix()
    {
        // Primary selector is ThingAttribute; Marker is the secondary must-have that fires.
        var shape = Shape.Class()
            .MustHaveAttribute("ThingAttribute")
            .MustHaveAttribute("MarkerAttribute")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var source = @"
public sealed class ThingAttribute : System.Attribute { }
public sealed class MarkerAttribute : System.Attribute { }
[Thing] public class Widget { }
";
        var result = await ApplyFirstFix(shape, source);
        result.ShouldContain("[Marker]");
        result.ShouldContain("class Widget");
    }

    [Fact]
    public async Task ToFixProvider_reports_all_check_ids_as_fixable()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .MustBeSealed()
            .MustImplement("System.IDisposable")
            .Project<Collected>((in ShapeProjectionContext ctx) => new Collected(ctx.Fqn));

        var provider = shape.ToFixProvider();
        provider.FixableDiagnosticIds.ShouldBe(_allThreeIds);
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

        var first = diagnostics.OrderBy(d => d.Location.SourceSpan.Start).First();

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
