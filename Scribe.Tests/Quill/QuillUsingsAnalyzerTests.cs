using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Scribe.Ink;
using Shouldly;
using Xunit;

namespace Scribe.Tests;

/// <summary>
///     Validates <see cref="QuillUsingsAnalyzer"/> (SCRIBE300): every emitted
///     <c>global::Ns.Type</c> reference in a Quill content method must be covered
///     by a <c>q.Using(Ns)</c> registration on the same containing type, otherwise
///     Quill leaves the reference verbose in its output.
/// </summary>
public class QuillUsingsAnalyzerTests
{
    [Fact]
    public void Single_method_with_matching_using_emits_no_diagnostic()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        q.Line(""var x = global::System.DateTime.Now;"");
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Single_method_with_missing_using_flags_global_reference()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System.Text"");
        q.Line(""var x = global::System.Collections.Generic.List<int>();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
        var msg = diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        msg.ShouldContain("global::System.Collections.Generic.List");
        msg.ShouldContain("System.Collections.Generic");
    }

    [Fact]
    public void Helper_method_on_same_type_inherits_usings()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        EmitBody(q);
        return q.Inscribe();
    }

    private static void EmitBody(Quill q)
    {
        q.Line(""var x = global::System.DateTime.Now;"");
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Helper_method_on_different_type_with_no_creation_is_skipped()
    {
        const string source = @"
using Scribe;

public class Helpers
{
    // No new Quill() in this type — Quill came from outside, type is skipped.
    public static void EmitBody(Quill q)
    {
        q.Line(""var x = global::System.DateTime.Now;"");
    }
}

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        Helpers.EmitBody(q);
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Renderer_class_with_using_inside_render_is_clean()
    {
        const string source = @"
using Scribe;

internal sealed class Renderer
{
    public string Render()
    {
        var q = new Quill();
        q.Usings(""System"", ""System.Text"");
        q.Line(""var sb = new global::System.Text.StringBuilder();"");
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Shorter_using_prefix_still_covers_longer_global()
    {
        const string source = @"
using Scribe;

internal sealed class Renderer
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        q.Line(""var sb = new global::System.Text.StringBuilder();"");
        return q.Inscribe();
    }
}
";
        // Only ""System"" is registered, but global::System.Text.StringBuilder still gets
        // partly shortened by Quill (to Text.StringBuilder). The analyzer reports covered
        // because some registered prefix matches; suggesting a longer ""System.Text"" is a
        // different style concern, not a correctness one.
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Renderer_class_with_unrelated_using_fires_diagnostic()
    {
        const string source = @"
using Scribe;

internal sealed class Renderer
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System.Text"");
        q.Line(""var x = new global::Foo.Bar.Baz();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
    }

    [Fact]
    public void Multiple_usings_cover_multiple_globals()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Usings(""Foo.Bar"", ""Baz.Qux"");
        q.Line(""var a = new global::Foo.Bar.A();"");
        q.Line(""var b = new global::Baz.Qux.B();"");
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Computed_using_argument_taints_and_silences_type()
    {
        const string source = @"
using Scribe;

public class Use
{
    private static string GetNs() => ""Foo.Bar"";

    public string Render()
    {
        var q = new Quill();
        q.Using(GetNs());
        q.Line(""var x = new global::Foo.Bar.Baz();"");
        return q.Inscribe();
    }
}
";
        // Computed Using() — analyzer can't know what was registered, so it goes silent
        // rather than risk a false positive.
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Interpolated_string_text_portion_is_scanned()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render(string name)
    {
        var q = new Quill();
        q.Using(""System.Text"");
        q.Line($""var x = new global::Foo.Bar.{name}();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
    }

    [Fact]
    public void Interpolated_string_only_inside_braces_is_not_flagged()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        var dynamic = ""global::Foo.Bar.Baz"";  // dynamic — analyzer cannot see this
        q.Line($""var x = {dynamic};"");
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Longest_prefix_wins_with_overlapping_namespaces()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Usings(""System"", ""System.Text"");
        q.Line(""var x = new global::System.Text.StringBuilder();"");
        return q.Inscribe();
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Block_header_with_global_is_scanned()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        using (q.Block(""public class Foo : global::Bar.Baz.IFoo""))
        {
            q.Line(""// body"");
        }
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
        diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("Bar.Baz");
    }

    [Fact]
    public void Case_expression_with_global_is_scanned()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        using (q.Case(""global::Foo.Bar.SomeEnum.X""))
        {
            q.Line(""// case body"");
        }
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
    }

    [Fact]
    public void BlockScope_attribute_argument_is_scanned()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Using(""System"");
        using (var bs = q.Block(""public class Foo""))
        {
            bs.Attribute(""Obsolete"", ""typeof(global::Foo.Bar.Baz)"");
            q.Line(""// body"");
        }
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
    }

    [Fact]
    public void Alias_call_does_not_register_namespace()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        var w = q.Alias(""Foo.Bar"", ""Widget"");
        // Alias() does NOT register Foo.Bar as a using-namespace; ResolveGlobalReferences
        // skips alias entries. So global::Foo.Bar.Other is still uncovered.
        q.Line(""var x = new global::Foo.Bar.Other();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe(QuillUsingsAnalyzer.DiagnosticId);
    }

    [Fact]
    public void Quill_field_shared_across_methods_aggregates_usings()
    {
        const string source = @"
using Scribe;

public class Use
{
    private readonly Quill _q = new Quill();

    public void Setup() => _q.Using(""Foo.Bar"");

    public void Emit() => _q.Line(""var x = new global::Foo.Bar.Baz();"");

    public string Finish() => _q.Inscribe();
}
";
        // Setup registers the using; Emit emits the global. Per-type aggregation
        // sees both.
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Type_with_no_creation_is_skipped_even_when_global_is_unregistered()
    {
        const string source = @"
using Scribe;

public class Helpers
{
    // No new Quill() in this type. Even if a global isn't registered, we don't know
    // — the caller may have registered it. Silence rather than false-positive.
    public static void Emit(Quill q)
    {
        q.Line(""var x = new global::Foo.Bar.Baz();"");
    }
}
";
        RunAnalyzer(source).ShouldBeEmpty();
    }

    [Fact]
    public void Diagnostic_severity_is_info()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        q.Line(""var x = new global::Foo.Bar.Baz();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Info);
    }

    [Fact]
    public void Generic_with_inner_global_emits_separate_diagnostics_for_each()
    {
        const string source = @"
using Scribe;

public class Use
{
    public string Render()
    {
        var q = new Quill();
        // No usings registered — both the outer List and inner Baz should fire.
        q.Line(""var xs = new global::System.Collections.Generic.List<global::Foo.Bar.Baz>();"");
        return q.Inscribe();
    }
}
";
        var diagnostics = RunAnalyzer(source);
        diagnostics.Length.ShouldBe(2);
        var paths = diagnostics
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        paths.ShouldContain(m => m.Contains("System.Collections.Generic.List"));
        paths.ShouldContain(m => m.Contains("Foo.Bar.Baz"));
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        void AddIfMissing(System.Reflection.Assembly asm)
        {
            if (string.IsNullOrEmpty(asm.Location)) return;
            if (refs.OfType<PortableExecutableReference>()
                .Any(r => string.Equals(r.FilePath, asm.Location, StringComparison.OrdinalIgnoreCase))) return;
            refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }

        AddIfMissing(typeof(global::Scribe.Quill).Assembly);
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
            throw new InvalidOperationException(
                "Test compilation failed: "
                + string.Join("; ", compileErrors.Select(e =>
                    e.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
        }

        var analyzer = new QuillUsingsAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
