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
///     Drives <see cref="Relation.Pair{TLeft, TRight}"/> through
///     <see cref="CSharpGeneratorDriver"/> and validates three flows:
///     matched pairs stream, orphan-left diagnostic, orphan-right diagnostic.
///     Grounding scenario is the Hermetic-style Command ↔ Event join: each command
///     declares the event metadata name it raises, each event declares its own name;
///     the pair joins on that shared key.
/// </summary>
public class RelationPairTests
{
    private readonly record struct Command(string Fqn, string RaisesEventName);

    private readonly record struct Event(string Fqn, string Name);

    private sealed class PairGenerator : IIncrementalGenerator
    {
        public static readonly List<ShapedPair<Command, Event>> Pairs = new();
        public static readonly List<Diagnostic> Diagnostics = new();

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var commandShape = Shape.Class()
                .MustHaveAttribute("CommandAttribute")
                .Project<Command>((in ShapeProjectionContext ctx) =>
                    new Command(
                        Fqn: ctx.Fqn,
                        RaisesEventName: ctx.Attribute.Ctor<string>(0) ?? string.Empty));

            var eventShape = Shape.Class()
                .MustHaveAttribute("EventAttribute")
                .Project<Event>((in ShapeProjectionContext ctx) =>
                    new Event(Fqn: ctx.Fqn, Name: ctx.Fqn));

            var pair = Relation.Pair(
                    commandShape.ToProvider(context),
                    eventShape.ToProvider(context),
                    leftKey: c => c.RaisesEventName,
                    rightKey: e => e.Name)
                .RequireLeftHasRight(
                    id: "SCRIBE201",
                    title: "Command raises undeclared event",
                    messageFormat: "Command '{0}' raises undeclared event '{1}'")
                .WarnOnRightUnused(
                    id: "SCRIBE202",
                    title: "Event is declared but never raised",
                    messageFormat: "Event '{0}' is declared but never raised");

            context.RegisterSourceOutput(pair.Matched, (spc, p) =>
            {
                lock (Pairs) { Pairs.Add(p); }
            });

            pair.RegisterDiagnostics(context);
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

    private static (Microsoft.CodeAnalysis.GeneratorDriverRunResult Result, List<ShapedPair<Command, Event>> Pairs) Run(string source)
    {
        PairGenerator.Pairs.Clear();
        PairGenerator.Diagnostics.Clear();
        var driver = CSharpGeneratorDriver.Create(new PairGenerator())
            .RunGenerators(MakeCompilation(source), TestContext.Current.CancellationToken);
        return (driver.GetRunResult(), [..PairGenerator.Pairs]);
    }

    [Fact]
    public void Matches_command_to_event_by_shared_name()
    {
        var (result, pairs) = Run(@"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}

[Command(""DoThingCompleted"")] public class DoThing {}
[Event] public class DoThingCompleted {}
");
        result.Diagnostics.IsEmpty.ShouldBeTrue();
        pairs.Count.ShouldBe(1);
        pairs[0].Left.Fqn.ShouldBe("DoThing");
        pairs[0].Right.Fqn.ShouldBe("DoThingCompleted");
    }

    [Fact]
    public void Emits_SCRIBE201_when_command_raises_undeclared_event()
    {
        var (result, pairs) = Run(@"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}

[Command(""MissingEvent"")] public class DoThing {}
");
        pairs.ShouldBeEmpty();
        var diag = result.Diagnostics.Single(d => d.Id == "SCRIBE201");
        var msg = diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        msg.ShouldContain("DoThing");
        msg.ShouldContain("MissingEvent");
    }

    [Fact]
    public void Emits_SCRIBE202_when_event_has_no_raising_command()
    {
        var (result, _) = Run(@"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}

[Event] public class OrphanEvent {}
");
        var diag = result.Diagnostics.Single(d => d.Id == "SCRIBE202");
        diag.Severity.ShouldBe(DiagnosticSeverity.Warning);
        diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture).ShouldContain("OrphanEvent");
    }

    [Fact]
    public void Matched_and_orphans_coexist_in_the_same_compilation()
    {
        var (result, pairs) = Run(@"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}

[Command(""Matched"")] public class DoMatched {}
[Command(""NotThere"")] public class DoOrphanCommand {}
[Event] public class Matched {}
[Event] public class OrphanEvent {}
");
        pairs.Count.ShouldBe(1);
        pairs[0].Left.Fqn.ShouldBe("DoMatched");
        pairs[0].Right.Fqn.ShouldBe("Matched");

        var ids = result.Diagnostics.Select(d => d.Id).OrderBy(x => x).ToArray();
        ids.ShouldBe(["SCRIBE201", "SCRIBE202"]);
    }

    [Fact]
    public void Silent_default_emits_no_diagnostics_without_policy()
    {
        var silentGen = new InlineGen((context) =>
        {
            var left = Shape.Class()
                .MustHaveAttribute("CommandAttribute")
                .Project<Command>((in ShapeProjectionContext ctx) =>
                    new Command(ctx.Fqn, ctx.Attribute.Ctor<string>(0) ?? string.Empty));
            var right = Shape.Class()
                .MustHaveAttribute("EventAttribute")
                .Project<Event>((in ShapeProjectionContext ctx) => new Event(ctx.Fqn, ctx.Fqn));

            var pair = Relation.Pair(left.ToProvider(context), right.ToProvider(context),
                c => c.RaisesEventName, e => e.Name);

            pair.RegisterDiagnostics(context);
            context.RegisterSourceOutput(pair.Matched, (spc, _) => { });
        });

        var driver = CSharpGeneratorDriver.Create(silentGen).RunGenerators(
            MakeCompilation(@"
using System;
public sealed class CommandAttribute : Attribute { public CommandAttribute(string raises) {} }
public sealed class EventAttribute : Attribute {}

[Command(""Nope"")] public class A {}
[Event] public class B {}
"),
            TestContext.Current.CancellationToken);

        driver.GetRunResult().Diagnostics.IsEmpty.ShouldBeTrue();
    }

    private sealed class InlineGen : IIncrementalGenerator
    {
        private readonly Action<IncrementalGeneratorInitializationContext> _init;
        public InlineGen(Action<IncrementalGeneratorInitializationContext> init) => _init = init;
        public void Initialize(IncrementalGeneratorInitializationContext context) => _init(context);
    }
}
