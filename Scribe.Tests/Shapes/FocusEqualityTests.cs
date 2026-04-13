using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Scribe.Cache;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Equality contract for every cache-safe focus type introduced by B.2.
///     Each focus carries a Roslyn payload (symbol, attribute, typed-constant)
///     that must be excluded from equality; identity comes from the stable
///     breadcrumb (FQN + index/depth/name + Origin). These tests lock in that
///     contract so the incremental pipeline can trust focus equality as its
///     cache key.
/// </summary>
public class FocusEqualityTests
{
    private static LocationInfo Loc(string path, int start) =>
        new(path,
            new TextSpan(start, 1),
            new LinePositionSpan(new LinePosition(start, 0), new LinePosition(start, 1)));

    // ---------- AttributeFocus ----------

    [Fact]
    public void AttributeFocus_equal_when_breadcrumbs_match()
    {
        var origin = Loc("a.cs", 1);
        var a = new AttributeFocus(null!, "N.Attr", "N.Owner", 0, origin);
        var b = new AttributeFocus(null!, "N.Attr", "N.Owner", 0, origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        (a == b).ShouldBeTrue();
    }

    [Theory]
    [InlineData("N.Other", "N.Owner", 0)]
    [InlineData("N.Attr", "N.Other", 0)]
    [InlineData("N.Attr", "N.Owner", 1)]
    public void AttributeFocus_not_equal_when_breadcrumb_differs(string fqn, string owner, int index)
    {
        var origin = Loc("a.cs", 1);
        var a = new AttributeFocus(null!, "N.Attr", "N.Owner", 0, origin);
        var b = new AttributeFocus(null!, fqn, owner, index, origin);

        a.Equals(b).ShouldBeFalse();
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void AttributeFocus_not_equal_when_origin_differs()
    {
        var a = new AttributeFocus(null!, "N.Attr", "N.Owner", 0, Loc("a.cs", 1));
        var b = new AttributeFocus(null!, "N.Attr", "N.Owner", 0, Loc("b.cs", 1));

        a.Equals(b).ShouldBeFalse();
    }

    // ---------- TypeArgFocus ----------

    [Fact]
    public void TypeArgFocus_equal_when_breadcrumbs_match()
    {
        var parent = Loc("p.cs", 1);
        var origin = Loc("a.cs", 2);
        var a = new TypeArgFocus(null!, "System.String", 0, parent, origin);
        var b = new TypeArgFocus(null!, "System.String", 0, parent, origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void TypeArgFocus_ParentOrigin_disambiguates_same_fqn_and_index()
    {
        var origin = Loc("a.cs", 2);
        var a = new TypeArgFocus(null!, "System.String", 0, Loc("p1.cs", 1), origin);
        var b = new TypeArgFocus(null!, "System.String", 0, Loc("p2.cs", 1), origin);

        a.Equals(b).ShouldBeFalse();
    }

    [Theory]
    [InlineData("System.Other", 0)]
    [InlineData("System.String", 1)]
    public void TypeArgFocus_not_equal_when_breadcrumb_differs(string fqn, int index)
    {
        var parent = Loc("p.cs", 1);
        var origin = Loc("a.cs", 2);
        var a = new TypeArgFocus(null!, "System.String", 0, parent, origin);
        var b = new TypeArgFocus(null!, fqn, index, parent, origin);

        a.Equals(b).ShouldBeFalse();
    }

    // ---------- ConstructorArgFocus<T> ----------

    [Fact]
    public void ConstructorArgFocus_equal_when_breadcrumbs_match()
    {
        var origin = Loc("a.cs", 1);
        var a = new ConstructorArgFocus<int>(default, 42, 0, "N.Owner", origin);
        var b = new ConstructorArgFocus<int>(default, 42, 0, "N.Owner", origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ConstructorArgFocus_not_equal_when_value_differs()
    {
        var origin = Loc("a.cs", 1);
        var a = new ConstructorArgFocus<int>(default, 42, 0, "N.Owner", origin);
        var b = new ConstructorArgFocus<int>(default, 99, 0, "N.Owner", origin);

        a.Equals(b).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, "N.Owner")]
    [InlineData(0, "N.Other")]
    public void ConstructorArgFocus_not_equal_when_breadcrumb_differs(int index, string owner)
    {
        var origin = Loc("a.cs", 1);
        var a = new ConstructorArgFocus<int>(default, 42, 0, "N.Owner", origin);
        var b = new ConstructorArgFocus<int>(default, 42, index, owner, origin);

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void ConstructorArgFocus_supports_null_string_value()
    {
        var origin = Loc("a.cs", 1);
        var a = new ConstructorArgFocus<string>(default, null, 0, "N.Owner", origin);
        var b = new ConstructorArgFocus<string>(default, null, 0, "N.Owner", origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    // ---------- NamedArgFocus<T> ----------

    [Fact]
    public void NamedArgFocus_equal_when_breadcrumbs_match()
    {
        var origin = Loc("a.cs", 1);
        var a = new NamedArgFocus<string>(default, "v", "Prop", "N.Owner", origin);
        var b = new NamedArgFocus<string>(default, "v", "Prop", "N.Owner", origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Theory]
    [InlineData("w", "Prop", "N.Owner")]
    [InlineData("v", "Other", "N.Owner")]
    [InlineData("v", "Prop", "N.Other")]
    public void NamedArgFocus_not_equal_when_breadcrumb_differs(string value, string name, string owner)
    {
        var origin = Loc("a.cs", 1);
        var a = new NamedArgFocus<string>(default, "v", "Prop", "N.Owner", origin);
        var b = new NamedArgFocus<string>(default, value, name, owner, origin);

        a.Equals(b).ShouldBeFalse();
    }

    // ---------- BaseTypeChainFocus ----------

    [Fact]
    public void BaseTypeChainFocus_equal_when_breadcrumbs_match()
    {
        var root = Loc("r.cs", 1);
        var origin = Loc("b.cs", 2);
        var a = new BaseTypeChainFocus(null!, "N.Base", 0, root, origin);
        var b = new BaseTypeChainFocus(null!, "N.Base", 0, root, origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Theory]
    [InlineData("N.Other", 0)]
    [InlineData("N.Base", 1)]
    public void BaseTypeChainFocus_not_equal_when_breadcrumb_differs(string fqn, int depth)
    {
        var root = Loc("r.cs", 1);
        var origin = Loc("b.cs", 2);
        var a = new BaseTypeChainFocus(null!, "N.Base", 0, root, origin);
        var b = new BaseTypeChainFocus(null!, fqn, depth, root, origin);

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void BaseTypeChainFocus_RootOrigin_disambiguates_same_step_from_different_roots()
    {
        var origin = Loc("b.cs", 2);
        var a = new BaseTypeChainFocus(null!, "N.Base", 0, Loc("r1.cs", 1), origin);
        var b = new BaseTypeChainFocus(null!, "N.Base", 0, Loc("r2.cs", 1), origin);

        a.Equals(b).ShouldBeFalse();
    }

    // ---------- MemberFocus ----------

    [Fact]
    public void MemberFocus_equal_when_breadcrumbs_match()
    {
        var origin = Loc("m.cs", 1);
        var a = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, origin);
        var b = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void MemberFocus_Kind_is_not_part_of_equality()
    {
        var origin = Loc("m.cs", 1);
        var a = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, origin);
        var b = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Property, origin);

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Theory]
    [InlineData("N.T.Other", "N.T")]
    [InlineData("N.T.M", "N.Other")]
    public void MemberFocus_not_equal_when_breadcrumb_differs(string member, string owner)
    {
        var origin = Loc("m.cs", 1);
        var a = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, origin);
        var b = new MemberFocus(null!, member, owner, SymbolKind.Method, origin);

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void MemberFocus_not_equal_when_origin_differs()
    {
        var a = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, Loc("a.cs", 1));
        var b = new MemberFocus(null!, "N.T.M", "N.T", SymbolKind.Method, Loc("b.cs", 1));

        a.Equals(b).ShouldBeFalse();
    }
}
