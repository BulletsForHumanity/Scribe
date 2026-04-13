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
///     Exercises B.9 — LINQ query-comprehension parity. Verifies that a Shape
///     authored with <c>from / where / select</c> produces the same analyzer
///     behaviour as the equivalent fluent-form chain.
/// </summary>
public class QueryComprehensionTests
{
    private readonly record struct Collected(string Fqn);

    [Fact]
    public void Select_aliases_Etch_and_surfaces_fqn()
    {
        Shape<Collected> shape =
            from t in Stencil.ExposeClass().MustBePartial()
            select new Collected(t.Fqn);

        var diagnostics = RunAnalyzer(shape, "public partial class Widget { }");

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Select_reports_underlying_check_violations()
    {
        Shape<Collected> shape =
            from t in Stencil.ExposeClass().MustBePartial()
            select new Collected(t.Fqn);

        var diagnostics = RunAnalyzer(shape, "public class Widget { }");

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE001");
    }

    [Fact]
    public void Query_form_with_where_clause_desugars_and_fires()
    {
        // `from t in ... where <bool> select ...` desugars to
        // `source.Where(t => <bool>).Select(t => ...)`.
        Shape<Collected> shape =
            from t in Stencil.ExposeClass()
            where t.Symbol.Name.StartsWith("Widget", StringComparison.Ordinal)
            select new Collected(t.Fqn);

        var passing = RunAnalyzer(shape, "public class Widget { }");
        passing.ShouldBeEmpty();

        var failing = RunAnalyzer(shape, "public class Gadget { }");
        failing.Length.ShouldBe(1);
        failing[0].Id.ShouldBe("SCRIBE200");
    }

    [Fact]
    public void Where_explicit_spec_pins_custom_id()
    {
        var shape = Stencil.ExposeClass()
            .Where(
                t => t.Symbol.Name.StartsWith("Widget", StringComparison.Ordinal),
                new DiagnosticSpec(Id: "WQ100", Message: "Type '{0}' must start with 'Widget'"))
            .Select(t => new Collected(t.Fqn));

        var failing = RunAnalyzer(shape, "public class Gadget { }");
        failing.Length.ShouldBe(1);
        failing[0].Id.ShouldBe("WQ100");
    }

    [Fact]
    public void Fluent_and_query_forms_are_equivalent()
    {
        var query =
            from t in Stencil.ExposeClass().MustBePartial()
            where t.Fqn.Length > 0
            select new Collected(t.Fqn);

        var fluent = Stencil.ExposeClass()
            .MustBePartial()
            .Where(t => t.Fqn.Length > 0)
            .Select(t => new Collected(t.Fqn));

        var a = RunAnalyzer(query, "public class Widget { }");
        var b = RunAnalyzer(fluent, "public class Widget { }");

        a.Length.ShouldBe(b.Length);
        a.Select(d => d.Id).OrderBy(id => id).ShouldBe(b.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public void Where_explicit_overload_requires_non_empty_id()
    {
        Should.Throw<ArgumentException>(() =>
            Stencil.ExposeClass().Where(_ => true, new DiagnosticSpec()));
    }

    [Fact]
    public void Select_rejects_null_selector()
    {
        Should.Throw<ArgumentNullException>(() =>
            Stencil.ExposeClass().Select<Collected>(null!));
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
