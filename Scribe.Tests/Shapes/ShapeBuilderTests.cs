using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Cache;
using Scribe.Shapes;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Unit-tests the per-primitive predicate behaviour of <see cref="ShapeBuilder"/>.
///     Each test parses a compilation, picks out a named type, and invokes the
///     builder's internal check pipeline via <see cref="Shape{TModel}.ToProvider"/>
///     round-trips would be over-weight here — we probe the predicates directly
///     through a projected Shape on a synthetic dummy model.
/// </summary>
public class ShapeBuilderTests
{
    private readonly record struct TypeNameModel(string Name);

    private static (CSharpCompilation Compilation, INamedTypeSymbol Symbol) Compile(
        string source, string metadataName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            syntaxTrees: [tree],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.IDisposable).Assembly.Location),
            ]);

        var symbol = compilation.GetTypeByMetadataName(metadataName);
        symbol.ShouldNotBeNull($"type '{metadataName}' did not resolve");
        return (compilation, symbol!);
    }

    private static INamedTypeSymbol GetType(string source, string metadataName) =>
        Compile(source, metadataName).Symbol;

    private static EquatableArray<DiagnosticInfo> RunChecks(ShapeBuilder builder, string source, string metadataName)
    {
        var (compilation, symbol) = Compile(source, metadataName);
        var ct = TestContext.Current.CancellationToken;
        var diags = new System.Collections.Generic.List<DiagnosticInfo>();

        foreach (var check in builder.Checks)
        {
            if (!check.Predicate(symbol, compilation, ct))
            {
                diags.Add(new DiagnosticInfo(
                    Id: check.Id,
                    Severity: check.Severity,
                    MessageArgs: check.MessageArgs(symbol),
                    Location: null));
            }
        }

        return EquatableArray.From<DiagnosticInfo>(diags);
    }

    // Convenience overload for tests that already hold a compiled symbol.
    private static EquatableArray<DiagnosticInfo> RunChecks(ShapeBuilder builder, INamedTypeSymbol symbol)
    {
        var ct = TestContext.Current.CancellationToken;
        var tree = symbol.DeclaringSyntaxReferences[0].SyntaxTree;

        // Reuse the compilation the tree already belongs to by locating it via the symbol's containing assembly.
        // When symbols come from our Compile(...) helper, the compilation is the one we built.
        var compilation = CSharpCompilation.Create(
            "Probe",
            syntaxTrees: [tree],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.IDisposable).Assembly.Location),
            ]);
        var reResolved = compilation.GetTypeByMetadataName(symbol.ToDisplayString())!;

        var diags = new System.Collections.Generic.List<DiagnosticInfo>();
        foreach (var check in builder.Checks)
        {
            if (!check.Predicate(reResolved, compilation, ct))
            {
                diags.Add(new DiagnosticInfo(
                    Id: check.Id,
                    Severity: check.Severity,
                    MessageArgs: check.MessageArgs(reResolved),
                    Location: null));
            }
        }

        return EquatableArray.From<DiagnosticInfo>(diags);
    }

    // ───────────────────────────────────────────────────────────────
    //  MustBePartial
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustBePartial_PassesForPartialClass()
    {
        var symbol = GetType("public partial class Foo {}", "Foo");
        var builder = Shape.Class().MustBePartial();
        RunChecks(builder, symbol).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MustBePartial_FailsForNonPartialClass()
    {
        var symbol = GetType("public class Foo {}", "Foo");
        var builder = Shape.Class().MustBePartial();

        var diags = RunChecks(builder, symbol);
        diags.Count.ShouldBe(1);
        diags[0].Id.ShouldBe("SCRIBE001");
        diags[0].Severity.ShouldBe(DiagnosticSeverity.Error);
    }

    [Fact]
    public void MustBePartial_UsesOverrideId()
    {
        var symbol = GetType("public class Foo {}", "Foo");
        var builder = Shape.Class().MustBePartial(new DiagnosticSpec(Id: "CUSTOM01"));

        RunChecks(builder, symbol)[0].Id.ShouldBe("CUSTOM01");
    }

    // ───────────────────────────────────────────────────────────────
    //  MustBeSealed
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustBeSealed_PassesForSealedClass()
    {
        var symbol = GetType("public sealed class Foo {}", "Foo");
        RunChecks(Shape.Class().MustBeSealed(), symbol).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MustBeSealed_FailsForOpenClass()
    {
        var symbol = GetType("public class Foo {}", "Foo");
        var diags = RunChecks(Shape.Class().MustBeSealed(), symbol);

        diags.Count.ShouldBe(1);
        diags[0].Id.ShouldBe("SCRIBE005");
    }

    [Fact]
    public void MustBeSealed_PassesForValueType()
    {
        var symbol = GetType("public struct Foo {}", "Foo");
        RunChecks(Shape.Struct().MustBeSealed(), symbol).IsEmpty.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────
    //  MustImplement
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustImplement_PassesWhenInterfaceImplemented()
    {
        var symbol = GetType(
            "using System; public class Foo : IDisposable { public void Dispose() {} }",
            "Foo");
        RunChecks(Shape.Class().MustImplement<System.IDisposable>(), symbol).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MustImplement_FailsWhenInterfaceMissing()
    {
        var symbol = GetType("public class Foo {}", "Foo");
        var diags = RunChecks(Shape.Class().MustImplement<System.IDisposable>(), symbol);

        diags.Count.ShouldBe(1);
        diags[0].Id.ShouldBe("SCRIBE007");
    }

    [Fact]
    public void MustImplement_ByMetadataName_Works()
    {
        var symbol = GetType(
            "using System; public class Foo : IDisposable { public void Dispose() {} }",
            "Foo");
        RunChecks(Shape.Class().MustImplement("System.IDisposable"), symbol).IsEmpty.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────
    //  MustHaveAttribute
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustHaveAttribute_PassesWhenAttributePresent()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute {}
            [Foo] public class Target {}
            """, "Target");

        RunChecks(Shape.Class().MustHaveAttribute("FooAttribute"), symbol).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MustHaveAttribute_FailsWhenAttributeMissing()
    {
        var symbol = GetType("public class Target {}", "Target");
        var diags = RunChecks(Shape.Class().MustHaveAttribute("FooAttribute"), symbol);

        diags.Count.ShouldBe(1);
        diags[0].Id.ShouldBe("SCRIBE003");
    }

    [Fact]
    public void MustHaveAttribute_SetsPrimaryAttributeMetadataName()
    {
        var builder = Shape.Class().MustHaveAttribute("FooAttribute");
        builder.PrimaryAttributeMetadataName.ShouldBe("FooAttribute");
    }

    [Fact]
    public void MustHaveAttribute_SecondCall_DoesNotOverridePrimary()
    {
        var builder = Shape.Class()
            .MustHaveAttribute("FirstAttribute")
            .MustHaveAttribute("SecondAttribute");
        builder.PrimaryAttributeMetadataName.ShouldBe("FirstAttribute");
        builder.Checks.Count.ShouldBe(2);
    }

    // ───────────────────────────────────────────────────────────────
    //  MustBeNamed
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MustBeNamed_PassesWhenMatches()
    {
        var symbol = GetType("public class FooHandler {}", "FooHandler");
        RunChecks(Shape.Class().MustBeNamed(".*Handler$"), symbol).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void MustBeNamed_FailsWhenDoesNotMatch()
    {
        var symbol = GetType("public class FooManager {}", "FooManager");
        var diags = RunChecks(Shape.Class().MustBeNamed(".*Handler$"), symbol);

        diags.Count.ShouldBe(1);
        diags[0].Id.ShouldBe("SCRIBE029");
    }

    // ───────────────────────────────────────────────────────────────
    //  Fluent chaining
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleChecks_AllFail_YieldsAllDiagnostics()
    {
        var symbol = GetType("public class Foo {}", "Foo");
        var builder = Shape.Class()
            .MustBePartial()
            .MustBeSealed()
            .MustBeNamed(".*Handler$");

        var diags = RunChecks(builder, symbol);
        diags.Count.ShouldBe(3);
        var ids = diags.AsSpan().ToArray().Select(d => d.Id).ToHashSet();
        ids.ShouldBe(new System.Collections.Generic.HashSet<string> { "SCRIBE001", "SCRIBE005", "SCRIBE029" });
    }

    [Fact]
    public void ProjectSealsBuilder_IntoTypedShape()
    {
        var shape = Shape.Class()
            .MustBePartial()
            .Project<TypeNameModel>((in ShapeProjectionContext ctx) => new TypeNameModel(ctx.Symbol.Name));

        shape.ShouldNotBeNull();
    }
}
