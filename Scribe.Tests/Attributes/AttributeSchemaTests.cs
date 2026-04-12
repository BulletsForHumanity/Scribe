using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Attributes;
using Scribe.Cache;

namespace Scribe.Tests.Attributes;

public class AttributeSchemaTests
{
    private static INamedTypeSymbol GetType(string source, string metadataName = "Target")
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
            ]);

        var symbol = compilation.GetTypeByMetadataName(metadataName);
        symbol.ShouldNotBeNull($"type '{metadataName}' not resolved; diagnostics: "
            + string.Join("; ", compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return symbol!;
    }

    // ───────────────────────────────────────────────────────────────
    //  Has / For basics
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Has_ReturnsTrue_WhenAttributePresent()
    {
        var symbol = GetType("""
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class FooAttribute : Attribute {}
            [Foo] public class Target {}
            """);

        AttributeSchema.Has(symbol, "FooAttribute").ShouldBeTrue();
    }

    [Fact]
    public void Has_ReturnsFalse_WhenAttributeAbsent()
    {
        var symbol = GetType("public class Target {}");
        AttributeSchema.Has(symbol, "FooAttribute").ShouldBeFalse();
    }

    [Fact]
    public void Has_MatchesWithOrWithoutAttributeSuffix()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute {}
            [Foo] public class Target {}
            """);

        AttributeSchema.Has(symbol, "FooAttribute").ShouldBeTrue();
        AttributeSchema.Has(symbol, "Foo").ShouldBeTrue();
    }

    [Fact]
    public void For_MissingAttribute_ReaderExistsIsFalse()
    {
        var symbol = GetType("public class Target {}");
        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Exists.ShouldBeFalse();
    }

    [Fact]
    public void For_MissingAttribute_ReadsReturnDefault_NoErrors()
    {
        var symbol = GetType("public class Target {}");
        var reader = AttributeSchema.For(symbol, "FooAttribute");

        reader.Ctor<string>(0).ShouldBeNull();
        reader.Named<int>("X", 42).ShouldBe(42);
        reader.DrainErrors().IsEmpty.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────
    //  Ctor / CtorOpt
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_StringArg_HappyPath()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string name) {} }
            [Foo("hello")] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Exists.ShouldBeTrue();
        reader.Ctor<string>(0).ShouldBe("hello");
        reader.DrainErrors().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Ctor_IntArg_HappyPath()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(int value) {} }
            [Foo(42)] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Ctor<int>(0).ShouldBe(42);
    }

    [Fact]
    public void Ctor_MultipleArgs_ReadByIndex()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a, int b) {} }
            [Foo("x", 7)] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Ctor<string>(0).ShouldBe("x");
        reader.Ctor<int>(1).ShouldBe(7);
    }

    [Fact]
    public void Ctor_OutOfRange_ReturnsDefault_PushesError()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a) {} }
            [Foo("x")] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Ctor<string>(99).ShouldBeNull();

        var errors = reader.DrainErrors();
        errors.IsEmpty.ShouldBeFalse();
        errors[0].Id.ShouldBe(DiagnosticIds.AttributeCtorMissing);
    }

    [Fact]
    public void Ctor_TypeMismatch_ReturnsDefault_PushesError()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a) {} }
            [Foo("x")] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Ctor<int>(0).ShouldBe(0);

        var errors = reader.DrainErrors();
        errors.IsEmpty.ShouldBeFalse();
        errors[0].Id.ShouldBe(DiagnosticIds.AttributeValueMismatch);
    }

    [Fact]
    public void CtorOpt_OutOfRange_SilentDefault()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a) {} }
            [Foo("x")] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.CtorOpt<string>(99).ShouldBeNull();
        reader.DrainErrors().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void CtorArgCount_ReportsActualCount()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(int a, int b, int c) {} }
            [Foo(1, 2, 3)] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.CtorArgCount.ShouldBe(3);
    }

    // ───────────────────────────────────────────────────────────────
    //  Named
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Named_HappyPath()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public int Max { get; set; } }
            [Foo(Max = 100)] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Named<int>("Max", 0).ShouldBe(100);
    }

    [Fact]
    public void Named_Missing_ReturnsFallback()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public int Max { get; set; } }
            [Foo] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Named<int>("Max", 42).ShouldBe(42);
        reader.DrainErrors().IsEmpty.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────
    //  TypeArg
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void TypeArg_ReadsGenericArgument()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute<T> : Attribute {}
            [Foo<int>] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        var typeArg = reader.TypeArg(0);
        typeArg.ShouldNotBeNull();
        typeArg!.Name.ShouldBe("Int32");
    }

    [Fact]
    public void TypeArg_OutOfRange_PushesError()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute {}
            [Foo] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.TypeArg(0).ShouldBeNull();
        reader.DrainErrors()[0].Id.ShouldBe(DiagnosticIds.AttributeTypeArgMissing);
    }

    // ───────────────────────────────────────────────────────────────
    //  Location
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Location_CapturedForPresentAttribute()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute {}
            [Foo] public class Target {}
            """);

        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Location.ShouldNotBeNull();
    }

    [Fact]
    public void Location_NullForMissingAttribute()
    {
        var symbol = GetType("public class Target {}");
        var reader = AttributeSchema.For(symbol, "FooAttribute");
        reader.Location.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────
    //  Read<TModel> projection
    // ───────────────────────────────────────────────────────────────

    private readonly record struct FooModel(string Name, int Max);

    [Fact]
    public void Read_Projection_CapturesModelAndExists()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute
            {
                public FooAttribute(string name) {}
                public int Max { get; set; }
            }
            [Foo("alpha", Max = 7)] public class Target {}
            """);

        var result = AttributeSchema.Read<FooModel>(symbol, "FooAttribute", (ref AttributeReader r) =>
            new FooModel(
                Name: r.Ctor<string>(0) ?? string.Empty,
                Max: r.Named<int>("Max", 0)));

        result.Exists.ShouldBeTrue();
        result.Model.Name.ShouldBe("alpha");
        result.Model.Max.ShouldBe(7);
        result.Errors.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Read_MissingAttribute_ProducesDefaultModel_ExistsFalse()
    {
        var symbol = GetType("public class Target {}");

        var result = AttributeSchema.Read<FooModel>(symbol, "FooAttribute", (ref AttributeReader r) =>
            new FooModel(
                Name: r.Ctor<string>(0) ?? "fallback",
                Max: r.Named<int>("Max", 99)));

        result.Exists.ShouldBeFalse();
        result.Model.Name.ShouldBe("fallback");
        result.Model.Max.ShouldBe(99);
        result.Errors.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Read_CollectsErrors_FromTypedReadMismatches()
    {
        var symbol = GetType("""
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a) {} }
            [Foo("x")] public class Target {}
            """);

        var result = AttributeSchema.Read<FooModel>(symbol, "FooAttribute", (ref AttributeReader r) =>
            new FooModel(
                Name: r.Ctor<string>(0) ?? "",
                Max: r.Ctor<int>(5)));   // wrong index — pushes error

        result.Exists.ShouldBeTrue();
        result.Errors.IsEmpty.ShouldBeFalse();
        result.Errors[0].Id.ShouldBe(DiagnosticIds.AttributeCtorMissing);
    }

    [Fact]
    public void Read_IsEquatable_ByValue()
    {
        var source = """
            using System;
            public sealed class FooAttribute : Attribute { public FooAttribute(string a) {} }
            [Foo("x")] public class Target {}
            """;
        var a = AttributeSchema.Read<FooModel>(GetType(source), "FooAttribute", (ref AttributeReader r) =>
            new FooModel(r.Ctor<string>(0) ?? "", 0));
        var b = AttributeSchema.Read<FooModel>(GetType(source), "FooAttribute", (ref AttributeReader r) =>
            new FooModel(r.Ctor<string>(0) ?? "", 0));

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
