using System.Collections.Generic;
using System.Collections.Immutable;
using Scribe.Cache;

namespace Scribe.Tests.Cache;

public class EquatableArrayTests
{
    // ───────────────────────────────────────────────────────────────
    //  default / empty normalisation
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsEmpty()
    {
        var @default = default(EquatableArray<int>);
        @default.IsEmpty.ShouldBeTrue();
        @default.Count.ShouldBe(0);
    }

    [Fact]
    public void Default_Equals_Empty()
    {
        var @default = default(EquatableArray<int>);
        @default.ShouldBe(EquatableArray<int>.Empty);
        @default.GetHashCode().ShouldBe(EquatableArray<int>.Empty.GetHashCode());
    }

    [Fact]
    public void Empty_FromImmutableArray_Equals_Default()
    {
        var wrapped = new EquatableArray<int>(ImmutableArray<int>.Empty);
        wrapped.ShouldBe(default(EquatableArray<int>));
    }

    [Fact]
    public void Default_AsSpan_DoesNotThrow()
    {
        var @default = default(EquatableArray<int>);
        var span = @default.AsSpan();
        span.Length.ShouldBe(0);
    }

    // ───────────────────────────────────────────────────────────────
    //  value equality
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Equal_Contents_Are_Equal()
    {
        var a = EquatableArray.Create(1, 2, 3);
        var b = EquatableArray.Create(1, 2, 3);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_Contents_Are_Not_Equal()
    {
        var a = EquatableArray.Create(1, 2, 3);
        var b = EquatableArray.Create(1, 2, 4);
        a.Equals(b).ShouldBeFalse();
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Different_Length_Not_Equal()
    {
        var a = EquatableArray.Create(1, 2, 3);
        var b = EquatableArray.Create(1, 2);
        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void String_Contents_Equality_Works()
    {
        var a = EquatableArray.Create("foo", "bar");
        var b = EquatableArray.Create("foo", "bar");
        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ImmutableArray_Built_Different_Ways_Are_Equal()
    {
        // Different underlying T[] references, same contents — must be equal.
        int[] source = [10, 20, 30];
        var a = new EquatableArray<int>(ImmutableArray.Create(source));
        var b = new EquatableArray<int>(ImmutableArray.Create(10, 20, 30));
        a.Equals(b).ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────
    //  indexer, span, enumeration
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Indexer_ReturnsElement()
    {
        var a = EquatableArray.Create(10, 20, 30);
        a[0].ShouldBe(10);
        a[1].ShouldBe(20);
        a[2].ShouldBe(30);
    }

    [Fact]
    public void Count_ReturnsLength()
    {
        EquatableArray.Create(1, 2, 3, 4, 5).Count.ShouldBe(5);
    }

    [Fact]
    public void AsSpan_ReturnsElements()
    {
        var a = EquatableArray.Create(10, 20, 30);
        var span = a.AsSpan();
        span.Length.ShouldBe(3);
        span[0].ShouldBe(10);
        span[2].ShouldBe(30);
    }

    [Fact]
    public void Enumeration_YieldsAllElements()
    {
        var a = EquatableArray.Create(1, 2, 3);
        var collected = new List<int>();
        foreach (var item in a)
        {
            collected.Add(item);
        }

        collected.Count.ShouldBe(3);
        collected[0].ShouldBe(1);
        collected[1].ShouldBe(2);
        collected[2].ShouldBe(3);
    }

    [Fact]
    public void Enumeration_OverDefault_YieldsNothing()
    {
        var a = default(EquatableArray<int>);
        var collected = new List<int>();
        foreach (var item in a)
        {
            collected.Add(item);
        }

        collected.Count.ShouldBe(0);
    }

    // ───────────────────────────────────────────────────────────────
    //  factory methods
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_FromParams_Works()
    {
        var a = EquatableArray.Create(1, 2, 3);
        a.Count.ShouldBe(3);
    }

    [Fact]
    public void Create_FromEmptyParams_ReturnsEmpty()
    {
        var a = EquatableArray.Create<int>();
        a.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void From_Enumerable_Works()
    {
        var source = new[] { 1, 2, 3 };
        var a = EquatableArray.From<int>(source);
        a.Count.ShouldBe(3);
        a[1].ShouldBe(2);
    }

    [Fact]
    public void From_NullEnumerable_ReturnsEmpty()
    {
        var a = EquatableArray.From<int>(null!);
        a.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ImplicitConversion_FromImmutableArray()
    {
        EquatableArray<int> a = ImmutableArray.Create(1, 2, 3);
        a.Count.ShouldBe(3);
    }

    // ───────────────────────────────────────────────────────────────
    //  nested equatable array (composition)
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void Nested_EquatableArray_Equals_By_Value()
    {
        var inner1 = EquatableArray.Create(1, 2);
        var inner2 = EquatableArray.Create(3, 4);
        var outerA = EquatableArray.Create(inner1, inner2);

        var inner1Copy = EquatableArray.Create(1, 2);
        var inner2Copy = EquatableArray.Create(3, 4);
        var outerB = EquatableArray.Create(inner1Copy, inner2Copy);

        outerA.Equals(outerB).ShouldBeTrue();
        outerA.GetHashCode().ShouldBe(outerB.GetHashCode());
    }
}
