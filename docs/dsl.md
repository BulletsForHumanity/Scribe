# The Shape DSL

**A query language for the C# declaration graph.**

**Audience:** anyone authoring an analyzer, source generator, or code fixer with Scribe. If you want term definitions, read the [Glossary](glossary.md). If you want design rationale and planned primitives, read [design-member-level-shapes.md](design-member-level-shapes.md). If you want the text-emission side (Quill, templates, naming), read [writing-generators.md](writing-generators.md). This document is about the *language* — how the pieces compose into a working query.

---

## What the DSL Is

Scribe's centre of gravity. A declarative language for describing:

- What shape a piece of source code should have.
- Which parts of it matter.
- What must be true at each part.
- What model to extract at the leaf.

The query is authored **once**. Three pipelines consume it:

- **Analyzer** — reports predicate failures as diagnostics.
- **Generator** — emits one output file per surviving row.
- **Ink (fixer)** — rewrites the source for each reported diagnostic.

Same query, three projections. That is the whole promise.

---

## The Central Insight

**The DSL is relational algebra over declarations.** Every piece maps onto a SQL/LINQ concept you already know:

| DSL concept | Relational equivalent |
| --- | --- |
| Shape | Relation (a filtered, projected stream of rows) |
| Focus | The row type at a given stage |
| Lens | Join (one-to-many navigation to a related relation) |
| Predicate | `WHERE` clause |
| Projection | `SELECT` — the terminal model |
| Violation | A constraint failure attached to a row |
| Disjunction (`OneOf`) | `UNION` of predicate trees |
| Quantifier (`All` / `Any` / `None`) | Aggregate predicates over a joined set |

Every framework-authoring problem that looks like *"given a type with attribute X whose type argument T must satisfy Y"* is one navigation + one predicate in this grammar. What used to be hundreds of lines of imperative Roslyn becomes a few lines of composed Shapes.

---

## The Four Building Blocks

Every Shape query is built from four kinds of pieces. They compose in the same grammar at every level of nesting.

### 1. Shape — the query

A filter-and-projection rooted at a specific focus. The entry point is `Stencil.ExposeAnyType()`, `Stencil.ExposeRecord()`, or a similar constructor. Every chained call produces a new Shape; nothing mutates in place.

```csharp
Shape<TypeFocus> s = Stencil.ExposeAnyType().Implementing(KnownFqns.IThing);
```

### 2. Focus — the position

Where the Shape is anchored in the symbol graph. Every Shape has exactly one focus type. Predicates and lenses are **focus-specific** — `MustBePartial` applies to a `TypeFocus`, `MustBeParsable` applies to a `ConstructorArgFocus<INamedTypeSymbol>`.

Seven focus types, covering every position in the C# symbol graph you can squiggle at:

`TypeFocus`, `AttributeFocus`, `TypeArgFocus`, `ConstructorArgFocus<T>`, `NamedArgFocus<T>`, `BaseTypeChainFocus`, `MemberFocus`.

See the [Glossary](glossary.md#focus) for what each one wraps.

### 3. Lens — the navigation

A one-to-many projection from one focus to another. `Lens<TSource, TTarget>` is a `Func<TSource, IEnumerable<TTarget>>` plus a location-propagation function that carries the destination's source span back to any violation reported there.

A lens is a `SelectMany`. You can chain any number of them. A deeply-nested query like *"for every type implementing X, for every `[Foo<T>]` attribute, `T` must implement Y"* is three lens hops.

### 4. Predicate & Projection — the leaves

Predicates are tests (`MustBePartial`, `MustImplement(fqn)`, `MustBeParsable`, `SymbolEquals`…). A failing predicate becomes a **violation** with a diagnostic ID, message, and squiggle location.

Projection is the terminal `.Etch<TModel>(builder)` that produces the equatable `TModel` attached to the surviving row. The stream of `ShapedSymbol<TModel>` is what the consumer reads.

---

## Two Forms, One Tree

The DSL is authored in either of two forms. They produce identical trees; pick whichever reads better for the problem.

### Fluent form

Reads like a method chain. Good for linear validations where each step narrows the previous.

```csharp
Shape<ThingModel> thingShape =
    Stencil.ExposeAnyType()
        .Implementing(KnownFqns.IThing)
        .MustBePartial()
        .MustBeSealed()
        .Attributes(KnownFqns.Widget, min: 1)
            .GenericTypeArg(0)
                .MustImplement(KnownFqns.IWidget)
        .Etch<ThingModel>(t => new ThingModel(t.Fqn));
```

### Query-comprehension form

Reads like LINQ. Good for multi-join queries where several foci need to be in scope at the same time (for cross-focus equality, or for building a model from multiple navigated positions).

```csharp
Shape<ThingWidgetContract> contract =
    from thing  in Stencil.ExposeAnyType().Implementing(KnownFqns.IThing)
    from widget in thing.Attributes(KnownFqns.Widget)
    from w      in widget.GenericTypeArg(0).AsTypeShape()
                     .MustImplement(KnownFqns.IWidget)
    from handler in w.Attributes(KnownFqns.HandledBy)
    from h      in handler.GenericTypeArg(0).AsTypeShape()
    where h.Members(m => m is IMethodSymbol)
           .Any(m => m.Attributes(KnownFqns.Handles)
                      .Any(a => a.GenericTypeArg(0).SymbolEquals(w)))
    select new ThingWidgetContract(thing.Model, w.Model, h.Model);
```

Every `from` is a lens. Every `where` is a predicate. Every `select` is a projection. The DSL's verbs are the LINQ verbs exactly — `SelectMany`, `Where`, `Select` — so this desugars without wrappers.

---

## Composition Rules

A small set of rules governs how the pieces fit together.

### Rule 1 — Navigation changes focus.

A lens applied to a `Shape<A>` produces a `Shape<B>`. All subsequent predicates and lenses are interpreted at the new focus. The `AsTypeShape()` lens is the re-entry point that lifts a `TypeArgFocus` back to a `TypeFocus`, enabling type-level predicates on a navigated type argument.

### Rule 2 — Presence constraints live on the lens.

Lenses that can return zero or more targets take optional `min` / `max` parameters:

```csharp
.Attributes(KnownFqns.Widget, min: 1, max: 3)
```

Without a `min`, a zero-hit lens silently passes (sub-Shape applies to zero rows — trivially true). With `min: 1`, an empty result becomes a violation. This is the *only* way to constrain presence — never bake it into predicates.

### Rule 3 — Disjunction wraps alternative Shapes.

`Shape.OneOf(A, B, …)` passes if any alternative passes. When all fail, it reports one fused diagnostic that lists every unsatisfied expectation. Use this when a declaration may legitimately take one of several forms.

```csharp
Shape<Foo> s = Stencil.ExposeAnyType()
    .WithAttribute(KnownFqns.Foo)
    .OneOf(
        x => x.MustBeRecordStruct().MustBeReadOnly(),
        x => x.MustBeAbstract().MustBeRecord());
```

### Rule 4 — Quantifiers express intent over lens output.

`All(lens, sub)` is the default and is usually implicit in a chain. `Any(lens, sub)` and `None(lens, sub)` make "at least one" and "none of" explicit. Reach for them when *which* sub-focus matches matters, or when you're testing for the absence of something.

### Rule 5 — Cross-focus predicates need two foci in scope.

Comparisons between two navigated positions (e.g. *"the `[Handles<E>]` type argument on the handler must be the same event we came from"*) require the query-comprehension form so both foci are captured as named variables, then a `SymbolEquals(a, b)` predicate in the `where` clause.

### Rule 6 — The terminal is always a projection.

Every Shape ends with `.Etch<TModel>(…)`. Without a projection, the Shape has no leaf — no equatable output for the incremental pipeline to cache. `TModel` must be equatable (prefer records). Incremental caching depends on this.

---

## Materialisation — Three Consumers

A single Shape is handed to whichever consumer you need. Each consumer reads different parts of the same tree.

### `ToAnalyzer()`

Produces a `DiagnosticAnalyzer`. Reads every `ShapedSymbol.Violations` and reports each as a diagnostic at the focus's squiggle location. The projection is unused.

### `ToProvider(context)`

Produces an `IncrementalValuesProvider<ShapedSymbol<TModel>>` for use inside an `IIncrementalGenerator`. Typically filters to violation-free rows, then feeds `RegisterSourceOutput`. The violations are unused.

### Ink

Produces a `CodeFixProvider`. For each diagnostic the analyzer reports, Ink applies a fix — either a built-in `FixKind` (twenty-one catalogued kinds: `MakePartial`, `AddAttribute`, `RemoveModifier`, etc.) or a `FixKind.Custom` paired with a registered rewrite delegate.

### The three are independent.

You can ship any subset. An analyzer without fixers, a generator without an analyzer, or all three from one Shape. The query is authored once either way.

---

## What a Full Query Looks Like

Here is the shape of a representative end-to-end analyzer + generator, written against a generic `IThing` / `[Widget<T>]` domain.

```csharp
public static class ThingShape
{
    public static readonly Shape<ThingModel> Shape =
        from thing   in Stencil.ExposeAnyType()
                            .Implementing(KnownFqns.IThing)
                            .MustBePartial()                                // SCRIBE001
                            .MustBeSealed()                                 // SCRIBE002
        from widget  in thing.Attributes(KnownFqns.Widget, min: 1)          // WIDGET101
        from arg     in widget.GenericTypeArg(0).AsTypeShape()
                            .MustImplement(KnownFqns.IWidget)               // WIDGET102
        from ctor    in widget.ConstructorArg<string>(0)
                            .MustSatisfy(s => !string.IsNullOrEmpty(s))     // WIDGET103
        select new ThingModel(thing.Fqn, arg.Fqn, ctor.Value);
}

// Analyzer
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThingAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext ctx) => ThingShape.Shape.ToAnalyzer().Register(ctx);
    // Supported descriptors derived from the Shape
}

// Generator
[Generator]
public sealed class ThingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        IncrementalValuesProvider<ShapedSymbol<ThingModel>> things = ThingShape.Shape.ToProvider(ctx);
        ctx.RegisterSourceOutput(things, (spc, thing) =>
        {
            Quill quill = Quill.Begin($"global::{thing.Model.Fqn}Dispatcher");
            // …emit code using thing.Model…
            spc.AddSource(thing.Model.Fqn + ".g.cs", quill.Inscribe());
        });
    }
}
```

One Shape, one model, two consumers. Every diagnostic surfaces at the right span (the `[Widget<>]` argument for WIDGET102, the constructor argument for WIDGET103). The generator emits code only for rows whose Shape passed.

---

## Why It Works

Three properties make the DSL composable at any depth.

1. **Every focus is equatable.** Roslyn symbols are not stable across compilations; foci wrap them with identity derived from fully-qualified names plus syntax spans. The incremental pipeline can cache on focus equality.
2. **Every lens carries a location-propagation function.** Violations reported at a deeply-nested focus squiggle at the right source span without the query author having to pass locations manually.
3. **Every Shape is pure and immutable.** No hidden state, no order dependency between chained calls. Shapes can be composed, stored in `static readonly` fields, and shared across analyzers/generators.

The engine underneath is Roslyn's `IncrementalValuesProvider` plus equatable-value plumbing. The engine is already stable; the DSL is the surface that lets you author against it declaratively.

---

## Non-Goals

- **Operation-level and expression-level rules.** Shape is a language for *declaration* shape, not for validating the body of a method. Those belong in operation-walking analyzers.
- **Subsuming Prism under Lens.** Lens (structural) and Prism (keyed) are complementary, not redundant. A Lens walks a single symbol's structure along edges Roslyn already provides. A Prism joins two independent Shape streams by computed key — used for cross-assembly orphan diagnostics and cross-type composition (union bases ↔ variants, commands ↔ events) where no structural edge exists. Both coexist as first-class navigation primitives. See [Glossary: Prism](glossary.md#prism).
- **Hiding Roslyn.** Shape is a thin declarative layer over Roslyn, not a replacement. A `.Etch<TModel>(t => …)` builder has full access to the underlying `INamedTypeSymbol` when it needs to read something the DSL hasn't exposed yet.

---

## Related Documents

- [Glossary](glossary.md) — Term-by-term definitions of every concept used above.
- [design-member-level-shapes.md](design-member-level-shapes.md) — Design rationale, planned primitives, worked HierarchicalKey and Event Contract examples.
- [writing-generators.md](writing-generators.md) — The Transform → Register → Render pattern and Quill usage once you have the projected models.
- [quill-reference.md](quill-reference.md) — Quill API reference for the text-emission half of a generator.
