using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using Scribe.Cache;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Exercises <see cref="Lens{TSource, TTarget}"/> — the structural refocus
///     primitive that every built-in lens will be an instance of. Uses
///     lightweight equatable record structs in place of real Roslyn focus rows
///     so the tests assert the primitive's contract (Navigate, Smudge, Then)
///     without depending on B.2 focus types.
/// </summary>
public class LensTests
{
    private readonly record struct Parent(string Name, IReadOnlyList<Child> Children);

    private readonly record struct Child(string Name);

    private readonly record struct Grand(string Name);

    [Fact]
    public void Navigate_yields_every_target_for_a_given_source()
    {
        var lens = new Lens<Parent, Child>(
            navigate: p => p.Children,
            smudge: _ => null);

        var parent = new Parent("p", [new Child("a"), new Child("b")]);

        var children = lens.Navigate(parent).ToArray();

        children.ShouldBe([new Child("a"), new Child("b")]);
    }

    [Fact]
    public void Navigate_returning_empty_enumerable_means_lens_does_not_apply()
    {
        var lens = new Lens<Parent, Child>(
            navigate: _ => [],
            smudge: _ => null);

        lens.Navigate(new Parent("p", [])).ShouldBeEmpty();
    }

    [Fact]
    public void Smudge_resolves_the_target_focus_source_span()
    {
        var location = new LocationInfo(
            "test.cs",
            new TextSpan(100, 10),
            new LinePositionSpan(new LinePosition(5, 10), new LinePosition(5, 20)));
        var lens = new Lens<Parent, Child>(
            navigate: p => p.Children,
            smudge: _ => location);

        lens.Smudge(new Child("a")).ShouldBe(location);
    }

    [Fact]
    public void Then_composes_two_lenses_into_a_deeper_hop()
    {
        var parentToChild = new Lens<Parent, Child>(
            navigate: p => p.Children,
            smudge: _ => null);
        var childToGrand = new Lens<Child, Grand>(
            navigate: c => [new Grand(c.Name + "-1"), new Grand(c.Name + "-2")],
            smudge: _ => null);

        var composed = parentToChild.Then(childToGrand);

        var parent = new Parent("p", [new Child("a"), new Child("b")]);
        var grandchildren = composed.Navigate(parent).ToArray();

        grandchildren.ShouldBe([
            new Grand("a-1"),
            new Grand("a-2"),
            new Grand("b-1"),
            new Grand("b-2"),
        ]);
    }

    [Fact]
    public void Then_forwards_smudge_to_the_deepest_target()
    {
        var childLocation = new LocationInfo(
            "child.cs",
            new TextSpan(0, 5),
            new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 5)));
        var grandLocation = new LocationInfo(
            "grand.cs",
            new TextSpan(10, 5),
            new LinePositionSpan(new LinePosition(2, 0), new LinePosition(2, 5)));

        var parentToChild = new Lens<Parent, Child>(
            navigate: p => p.Children,
            smudge: _ => childLocation);
        var childToGrand = new Lens<Child, Grand>(
            navigate: c => [new Grand(c.Name)],
            smudge: _ => grandLocation);

        var composed = parentToChild.Then(childToGrand);

        composed.Smudge(new Grand("x")).ShouldBe(grandLocation);
    }

    [Fact]
    public void Constructor_rejects_null_navigate()
    {
        Should.Throw<ArgumentNullException>(
            () => new Lens<Parent, Child>(navigate: null!, smudge: _ => null));
    }

    [Fact]
    public void Constructor_rejects_null_smudge()
    {
        Should.Throw<ArgumentNullException>(
            () => new Lens<Parent, Child>(navigate: p => p.Children, smudge: null!));
    }

    [Fact]
    public void Then_rejects_null_next_lens()
    {
        var lens = new Lens<Parent, Child>(
            navigate: p => p.Children,
            smudge: _ => null);

        Should.Throw<ArgumentNullException>(() => lens.Then<Grand>(null!));
    }
}
