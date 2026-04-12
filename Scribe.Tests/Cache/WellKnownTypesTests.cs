using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Cache;

namespace Scribe.Tests.Cache;

public class WellKnownTypesTests
{
    private static CSharpCompilation MakeCompilation(string source = "class Foo {}") =>
        CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    cancellationToken: TestContext.Current.CancellationToken),
            ],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ]);

    [Fact]
    public void From_ResolvesKnownBclType()
    {
        var compilation = MakeCompilation();
        var known = WellKnownTypes.From(compilation, "System.Object");

        known.Get("System.Object").ShouldNotBeNull();
    }

    [Fact]
    public void From_ReturnsNullForMissingType()
    {
        var compilation = MakeCompilation();
        var known = WellKnownTypes.From(compilation, "DoesNotExist.Whatsoever");

        known.Get("DoesNotExist.Whatsoever").ShouldBeNull();
    }

    [Fact]
    public void Get_ReturnsNullForUnregisteredName()
    {
        var compilation = MakeCompilation();
        var known = WellKnownTypes.From(compilation, "System.Object");

        known.Get("System.String").ShouldBeNull();
    }

    [Fact]
    public void TryGet_ReturnsTrueForKnown_FalseForUnknown()
    {
        var compilation = MakeCompilation();
        var known = WellKnownTypes.From(compilation, "System.Object");

        known.TryGet("System.Object", out var obj).ShouldBeTrue();
        obj.ShouldNotBeNull();

        known.TryGet("System.String", out _).ShouldBeFalse();
    }

    [Fact]
    public void Resolve_LazilyCachesNewNames()
    {
        var compilation = MakeCompilation();
        var known = WellKnownTypes.From(compilation);

        known.TryGet("System.String", out _).ShouldBeFalse();

        var first = known.Resolve(compilation, "System.String");
        first.ShouldNotBeNull();

        known.TryGet("System.String", out var cached).ShouldBeTrue();
        cached!.ShouldBe(first!, SymbolEqualityComparer.Default);
    }
}
