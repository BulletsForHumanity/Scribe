using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Cache;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     Verifies cache correctness of <see cref="Relation.Pair{TLeft, TRight}"/> by
///     running the generator twice with a tracked driver and inspecting
///     <see cref="IncrementalStepRunReason"/>s. An unrelated edit must not cause
///     the matched-pair step to re-execute — otherwise the whole point of projecting
///     into cache-safe models is wasted.
/// </summary>
public class RelationPairIncrementalityTests
{
    private const string MatchedTrackingName = "scribe-test-matched";

    private readonly record struct Command(string Fqn, string RaisesEventName);

    private readonly record struct Event(string Fqn, string Name);

    private sealed class PairGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var commandShape = Shape.Class()
                .MustHaveAttribute("CommandAttribute")
                .Project<Command>((in ShapeProjectionContext ctx) =>
                    new Command(ctx.Fqn, ctx.Attribute.Ctor<string>(0) ?? string.Empty));

            var eventShape = Shape.Class()
                .MustHaveAttribute("EventAttribute")
                .Project<Event>((in ShapeProjectionContext ctx) =>
                    new Event(ctx.Fqn, ctx.Fqn));

            var pair = Relation.Pair(
                commandShape.ToProvider(context),
                eventShape.ToProvider(context),
                c => c.RaisesEventName,
                e => e.Name);

            var tracked = pair.Matched.WithTrackingName(MatchedTrackingName);
            context.RegisterSourceOutput(tracked, (_, _) => { });
        }
    }

    private static CSharpCompilation MakeCompilation(string source) =>
        CSharpCompilation.Create(
            "TestAsm",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static CSharpGeneratorDriver MakeDriver() =>
        CSharpGeneratorDriver.Create(
            generators: [new PairGenerator().AsSourceGenerator()],
            additionalTexts: default,
            parseOptions: null,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    private const string Prelude = @"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}
";

    [Fact]
    public void Unrelated_edit_does_not_re_materialize_matched_pairs()
    {
        var before = Prelude + @"
[Command(""FooEvent"")] public class Foo {}
[Event] public class FooEvent {}
";

        var after = before + "\npublic class Unrelated {}  // add an unrelated type";

        var driver = (CSharpGeneratorDriver)MakeDriver()
            .RunGenerators(MakeCompilation(before), TestContext.Current.CancellationToken);

        driver = (CSharpGeneratorDriver)driver
            .RunGenerators(MakeCompilation(after), TestContext.Current.CancellationToken);

        var reasons = CollectMatchedStepReasons(driver);

        // Every Matched output from the second run must be Cached or Unchanged —
        // no re-projection, no re-emission.
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(r =>
            r == IncrementalStepRunReason.Cached
            || r == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void Adding_a_new_matched_pair_preserves_existing_cached_outputs()
    {
        var before = Prelude + @"
[Command(""FooEvent"")] public class Foo {}
[Event] public class FooEvent {}
";

        var after = Prelude + @"
[Command(""FooEvent"")] public class Foo {}
[Event] public class FooEvent {}
[Command(""BarEvent"")] public class Bar {}
[Event] public class BarEvent {}
";

        var driver = (CSharpGeneratorDriver)MakeDriver()
            .RunGenerators(MakeCompilation(before), TestContext.Current.CancellationToken);

        driver = (CSharpGeneratorDriver)driver
            .RunGenerators(MakeCompilation(after), TestContext.Current.CancellationToken);

        var reasons = CollectMatchedStepReasons(driver);

        // After adding Bar + BarEvent we expect:
        //   - Foo/FooEvent output: Cached or Unchanged (preserved)
        //   - Bar/BarEvent output: New (freshly produced)
        // No existing output should be Modified (that would imply a non-equatable model).
        reasons.ShouldContain(IncrementalStepRunReason.New);
        reasons.ShouldNotContain(IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void Editing_command_attribute_value_re_materializes_only_its_pair()
    {
        var before = Prelude + @"
[Command(""FooEvent"")] public class Foo {}
[Event] public class FooEvent {}
[Command(""BarEvent"")] public class Bar {}
[Event] public class BarEvent {}
";

        // Flip Bar's raised event — Foo/FooEvent pairing is unaffected.
        var after = Prelude + @"
[Command(""FooEvent"")] public class Foo {}
[Event] public class FooEvent {}
[Command(""DifferentEvent"")] public class Bar {}
[Event] public class BarEvent {}
[Event] public class DifferentEvent {}
";

        var driver = (CSharpGeneratorDriver)MakeDriver()
            .RunGenerators(MakeCompilation(before), TestContext.Current.CancellationToken);

        driver = (CSharpGeneratorDriver)driver
            .RunGenerators(MakeCompilation(after), TestContext.Current.CancellationToken);

        var reasons = CollectMatchedStepReasons(driver);

        // Foo's pair must remain cached; Bar's pair is a new output value.
        // At least one Cached/Unchanged output proves we didn't re-run everything.
        var preserved = reasons.Count(r =>
            r == IncrementalStepRunReason.Cached
            || r == IncrementalStepRunReason.Unchanged);
        preserved.ShouldBeGreaterThan(0);
    }

    private static List<IncrementalStepRunReason> CollectMatchedStepReasons(CSharpGeneratorDriver driver)
    {
        var result = driver.GetRunResult();
        var reasons = new List<IncrementalStepRunReason>();
        foreach (var generatorResult in result.Results)
        {
            if (!generatorResult.TrackedSteps.TryGetValue(MatchedTrackingName, out var steps))
            {
                continue;
            }

            foreach (var step in steps)
            {
                foreach (var output in step.Outputs)
                {
                    reasons.Add(output.Reason);
                }
            }
        }

        return reasons;
    }
}
