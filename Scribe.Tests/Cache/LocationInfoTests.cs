using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Scribe.Cache;

namespace Scribe.Tests.Cache;

public class LocationInfoTests
{
    private static SyntaxTree Parse(string source, string path) =>
        CSharpSyntaxTree.ParseText(
            source,
            path: path,
            cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public void From_Null_ReturnsNull()
    {
        LocationInfo.From(null).ShouldBeNull();
    }

    [Fact]
    public void From_LocationNone_ReturnsNull()
    {
        LocationInfo.From(Location.None).ShouldBeNull();
    }

    [Fact]
    public void From_SourceLocation_CapturesFilePath()
    {
        var tree = Parse("class Foo {}", "test.cs");
        var node = tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().First();
        var loc = node.GetLocation();

        var info = LocationInfo.From(loc);

        info.ShouldNotBeNull();
        info!.Value.FilePath.ShouldBe("test.cs");
    }

    [Fact]
    public void From_SourceLocation_CapturesTextSpan()
    {
        var tree = Parse("class Foo {}", "test.cs");
        var node = tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().First();
        var loc = node.GetLocation();

        var info = LocationInfo.From(loc);

        info!.Value.TextSpan.ShouldBe(loc.SourceSpan);
    }

    [Fact]
    public void ToLocation_RoundTrips_FilePath()
    {
        var tree = Parse("class Foo {}", "/path/to/test.cs");
        var node = tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().First();
        var original = node.GetLocation();

        var info = LocationInfo.From(original)!.Value;
        var materialised = info.ToLocation();

        materialised.GetLineSpan().Path.ShouldBe(original.GetLineSpan().Path);
        materialised.SourceSpan.ShouldBe(original.SourceSpan);
    }

    [Fact]
    public void Equality_By_Value()
    {
        var a = new LocationInfo("a.cs", new TextSpan(0, 10), new LinePositionSpan(default, default));
        var b = new LocationInfo("a.cs", new TextSpan(0, 10), new LinePositionSpan(default, default));
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_Path_Not_Equal()
    {
        var a = new LocationInfo("a.cs", new TextSpan(0, 10), default);
        var b = new LocationInfo("b.cs", new TextSpan(0, 10), default);
        a.ShouldNotBe(b);
    }
}
