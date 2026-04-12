using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Scribe.Ink.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Validates <see cref="CacheCorrectnessAnalyzer"/>: every
///     <c>ShapeBuilder.Project&lt;TModel&gt;</c> invocation with a <c>TModel</c>
///     that stores a Roslyn reference type (<c>ISymbol</c>, <c>SyntaxNode</c>,
///     <c>Compilation</c>, <c>SemanticModel</c>, <c>SyntaxTree</c>, <c>Location</c>,
///     <c>AttributeData</c>) should emit SCRIBE200 pointing at the offending member.
/// </summary>
public class CacheCorrectnessAnalyzerTests
{
    [Fact]
    public void Flags_ISymbol_field_on_projection_model()
    {
        const string source = @"
using Scribe.Shapes;
using Microsoft.CodeAnalysis;

public record struct Bad(INamedTypeSymbol Symbol);

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Bad>((in ShapeProjectionContext ctx) => new Bad(null!));
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE200");
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("Symbol");
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("ISymbol");
    }

    [Fact]
    public void Flags_SyntaxNode_property_on_projection_model()
    {
        const string source = @"
using Scribe.Shapes;
using Microsoft.CodeAnalysis;

public sealed record Bad
{
    public SyntaxNode? Node { get; init; }
}

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Bad>((in ShapeProjectionContext ctx) => new Bad());
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE200");
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("Node");
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("SyntaxNode");
    }

    [Fact]
    public void Flags_Compilation_and_Location_together()
    {
        const string source = @"
using Scribe.Shapes;
using Microsoft.CodeAnalysis;

public record struct Bad(Compilation Comp, Location Loc);

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Bad>((in ShapeProjectionContext ctx) => new Bad(null!, null!));
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(2);
        diagnostics.Select(d => d.Id).ShouldAllBe(id => id == "SCRIBE200");
        var messages = string.Join("|", diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        messages.ShouldContain("Compilation");
        messages.ShouldContain("Location");
    }

    [Fact]
    public void Does_not_flag_cache_safe_model()
    {
        const string source = @"
using Scribe.Shapes;
using Scribe.Cache;

public record struct Good(string Fqn, string Name, LocationInfo? Where);

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Good>((in ShapeProjectionContext ctx) => new Good(ctx.Fqn, ""x"", null));
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Ignores_static_members_and_constants()
    {
        const string source = @"
using Scribe.Shapes;
using Microsoft.CodeAnalysis;

public record struct Model(string Fqn)
{
    public static ISymbol? Shared = null;   // static — not cached per-item
    public const string Tag = ""x"";
}

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Model>((in ShapeProjectionContext ctx) => new Model(ctx.Fqn));
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Flags_ITypeSymbol_subinterface_via_ISymbol_base()
    {
        // ITypeSymbol : INamespaceOrTypeSymbol : ISymbol — inheritance must be walked.
        const string source = @"
using Scribe.Shapes;
using Microsoft.CodeAnalysis;

public record struct Bad(ITypeSymbol T);

public class Use
{
    public void M()
    {
        var _ = Shape.Class()
            .MustBePartial()
            .Project<Bad>((in ShapeProjectionContext ctx) => new Bad(null!));
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("SCRIBE200");
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(string source)
    {
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        // Ensure Scribe + Microsoft.CodeAnalysis are present even if their .Location
        // was empty at AppDomain scan time (bundled / single-file hosting).
        void AddIfMissing(System.Reflection.Assembly asm)
        {
            if (string.IsNullOrEmpty(asm.Location)) return;
            if (refs.OfType<PortableExecutableReference>().Any(r => string.Equals(r.FilePath, asm.Location, System.StringComparison.OrdinalIgnoreCase))) return;
            refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        AddIfMissing(typeof(Scribe.Shapes.Shape).Assembly);
        AddIfMissing(typeof(Scribe.Cache.LocationInfo).Assembly);
        AddIfMissing(typeof(Microsoft.CodeAnalysis.ISymbol).Assembly);
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (compileErrors.Count > 0)
        {
            throw new System.InvalidOperationException(
                "Test compilation failed: "
                + string.Join("; ", compileErrors.Select(e => e.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
        }
        var analyzer = new CacheCorrectnessAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
