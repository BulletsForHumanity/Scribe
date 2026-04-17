# Design — Navigable Shape Composition

> Draft. Captures the next major evolution of the Shape DSL: from flat
> predicates on a single type to composable navigation across the C#
> declaration graph, with validation and projection at every hop.
>
> Supersedes the earlier draft on member-level shapes — that phase has
> shipped (see **Status** below).

---

## Status

What the Shape DSL can already do:

| Primitive | Status | Notes |
| --------- | ------ | ----- |
| Type-level predicates (`MustBePartial`, `MustBeSealed`, `MustBeRecord`, `MustBeReadOnly`, `MustBeRecordStruct`, `MustNotBeNested`, `Implementing`, …) | Shipped | Surface: `Stencil.ExposeAnyType() / Stencil.ExposeRecord() / …` → fluent chain → `Project<TModel>`. |
| Projection to equatable model | Shipped | `ShapedSymbol<TModel>` with `Fqn`, `Model`, `Location`, `Violations`. Drives both `ToAnalyzer()` and `ToProvider(context)`. |
| Member-level rules (`ForEachMember`) | Shipped | `MemberCheck`, `MemberDiagnosticSpec`, `MemberSquiggleAt`, `MemberSquiggleLocator`. Emits zero-or-more diagnostics per type, squiggled at the member. |
| Fix kinds | Shipped | Twenty-one built-in `FixKind`s covering modifiers, base list, attributes, visibility, constructors. Plus `FixKind.Custom` + Ink's `WithCustomFix(tag, delegate)` for bespoke rewrites. |
| Stream-to-stream join | Shipped | `Prism.By<TLeft, TRight>` joins two shape streams by string key, surfaces orphan diagnostics (`RequireLeftHasRight`, `WarnOnRightUnused`). |

What the DSL **cannot** yet do, and what this document is about:

- Navigate from a type into its **attributes**, the **type arguments of those
  attributes**, the **constructor arguments of those attributes**, or its
  **base-type chain** — and apply a sub-Shape at the new focus.
- Express cross-type rules like "the `T` in `[ComposedOf<T>]` must implement
  `IKeyPart`" without dropping back to imperative Roslyn.
- Report violations at the **correct source span** for nested navigations
  (today violations fire at the type identifier or member identifier — there
  is no "at the type argument of the second `[Foo<T>]` attribute on type X").

`Prism.By` covers one shape of cross-type work — joining two independent
streams by equal keys. Navigable composition covers the orthogonal shape —
following a symbol's structure (attributes, type args, ancestors, members) to
a related symbol in-place.

---

## The class of problems

Hand-rolled Roslyn analyzers repeatedly solve instances of the same pattern:

> "Given a type matching shape A, for each of its `[Foo<T>]` attributes, `T`
> must satisfy shape B. Report the failure at `T`'s attribute argument."

Same structure, different names, across dozens of frameworks:

- **Hermetic Event Contract** — `Command` with `[RaisesEvent<E>]` ↔ `Event`
  with `[AppliedBy<A>]` ↔ `Aggregate` with `[Applies<E>]`. Bidirectional
  cycle of joins, each with its own predicate. Currently ~400 lines of
  imperative Roslyn.
- **Hermetic HierarchicalKey** — `[ComposedOf<TPart>]` constrained to
  `IKeyPart`, `[Discriminator]` requires `[KeyPart]` ancestor,
  `[Param(name, type)].type` must be parsable. Currently ~400 lines.
- **EF Core** — `[Index(nameof(X))]` — `X` must be a property on the same
  entity. `[ForeignKey(nameof(X))]` — `X` must be a navigation property
  whose type has a keyed property.
- **MediatR / MassTransit** — handler type discovery, `IRequestHandler<TRequest, TResponse>`
  binding, saga state-machine transitions.
- **ASP.NET routing** — `[Route]` templates referencing action parameters by
  name, `[FromServices]` resolved against the DI container shape.
- **FluentValidation** — `RuleFor(x => x.Foo).SetValidator(new FooValidator())`
  — validator type must match property type.

Every one of these is **declarative in intent** ("constrain this attribute
argument to this shape") and **imperative in implementation** (manual symbol
walks, attribute resolution, location bookkeeping, cache-correctness
pitfalls). The Shape DSL's promise is that the intent should match the
implementation.

---

## Core insight — the DSL is relational algebra over declarations

The existing fluent Shape API describes **filters and projections** over a
single relation (types-in-compilation). What's missing is **joins**: ways to
relate that stream to other streams (attributes, type arguments, members,
ancestors) by declarative navigation.

Each navigation is a lens — a `SelectMany` over the symbol graph:

```text
Shape<TSource>
  .SelectMany(src → IEnumerable<TTarget>)   // the lens — one-to-many projection
  .Shape(target → Shape<TTarget>)            // sub-predicate at focus
```

Three things ride along every hop:

1. **Predicate** — pass / fail becomes a violation.
2. **Projection** — contributes to the aggregate model at the leaf.
3. **Location** — propagates the source span so the violation squiggles at
   the correct node (the attribute argument, the type-arg, the member, the
   base-list entry), not the root type.

Arbitrary nesting depth, same grammar at every level. A Shape doesn't know or
care whether it's at depth 1 or depth 5.

Equivalently, in LINQ query-comprehension form:

```csharp
from type    in Stencil.ExposeAnyType().Implementing(ICommandLaw)
from raises  in type.Attributes(RaisesEvent)
from evt     in raises.GenericTypeArg(0).AsTypeShape()
from applied in evt.Attributes(AppliedBy)
from agg     in applied.GenericTypeArg(0).AsTypeShape()
where agg.HasMember(m => m.HasAttribute(Applies, tArg => tArg == evt))
select new CommandEventContract(type, evt, agg);
```

Every `from` is a join. Every `where` is a join predicate. Every `select` is
the model projection at the leaf. The diagnostic position is carried by
whichever step's constraint fails.

The DSL becomes: **Shapes are relations. Lenses are joins. Predicates are
filters. Projections are the SELECT. Violations are constraint failures.**

---

## What Scribe is becoming

The framing above reframes the whole project. Scribe is **not a generator
toolkit** — a generator toolkit helps you emit source text (Quill, Template,
Naming still do that, and still matter). Scribe's centre of gravity is
shifting to something larger: **a query language for symbol graphs**, with
two consumers.

- **The analyzer consumer** materialises the query as diagnostics — "report
  missing rows and broken predicates." Every failed join, every unsatisfied
  predicate, every unique-key violation becomes a squiggle at the correct
  span.
- **The generator consumer** materialises the query as source — "for each
  row in the result set, project it into a file." The `ShapedSymbol<TModel>`
  stream feeds `RegisterSourceOutput`; the model carries exactly the
  information the emitter needs.

Same query, two projections. The analyzer proves the code's shape is
correct; the generator transmutes that shape into infrastructure. That's
the whole pipeline — from declaration to diagnostic and from declaration to
emitted code — driven by one declarative artefact.

This is the primitive the framework authoring community has been missing.
Every attribute-driven framework (EF Core, MediatR, MassTransit, ASP.NET
routing, FluentValidation, Hermetic, dozens more) reimplements bits of this
query language by hand, poorly, with no shared vocabulary. Scribe + this DSL
names the pattern and makes it reusable.

What Scribe ships, stated precisely:

| Layer | Role |
| ----- | ---- |
| **Quill / Template / Naming / XmlDoc** | Text-emission primitives. Unchanged. |
| **Shape DSL** | Query language over the C# declaration graph. Filters, joins, projections, violations. |
| **`ToAnalyzer()`** | Materialises a Shape as a `DiagnosticAnalyzer`. |
| **`ToProvider(context)`** | Materialises a Shape as an `IncrementalValuesProvider<ShapedSymbol<TModel>>` for source generation. |
| **Ink** | Fixer infrastructure — the third consumer: "for each diagnostic row, produce a patch." |

Three consumers, one query. That's the shape of the project.

---

## Foci — first-class navigation targets

The current DSL has exactly one focus: `INamedTypeSymbol`. Navigation
requires new focus types, each one an equatable wrapper over a symbol plus a
breadcrumb back to its origin (for location reporting and cache stability).

| Focus | Wraps | Location source |
| ----- | ----- | --------------- |
| `TypeFocus` | `INamedTypeSymbol` | identifier / keyword / attribute / full decl |
| `AttributeFocus` | `AttributeData` | `ApplicationSyntaxReference` |
| `TypeArgFocus` | `ITypeSymbol` + position in a generic parameter list | the `TypeSyntax` at that position in the attribute / base-list / method signature |
| `ConstructorArgFocus` | `TypedConstant` + index | the argument expression syntax |
| `NamedArgFocus` | `TypedConstant` + name | the `name = value` syntax |
| `BaseTypeChainFocus` | sequence of `INamedTypeSymbol` | the base-list entry for each step |
| `MemberFocus` | `ISymbol` (field/property/method/event) | identifier / full decl / type annotation / first attribute (already implemented by `MemberSquiggleLocator`) |

Every focus is equatable, cache-safe, and knows how to resolve its own
squiggle location. The existing `TypeFocus` equivalent today is implicit in
`TypeShape` — it would be extracted as an explicit type.

---

## Primitives to add

Ordered by layering dependency. Each row assumes the ones above it.

1. **`Lens<TSource, TTarget>`** — `Func<TSource, IEnumerable<TTarget>>` plus a
   location-propagation function. The foundation. Every navigation below is
   an instance.
2. **`Shape<TTarget> Shape<TSource>.SelectMany(Lens<TSource, TTarget>)`** —
   the core DSL method. Produces a new Shape rooted at `TTarget`, whose
   violations bubble back up through the lens with correct locations.
3. **Built-in lenses** as extension methods, each returning
   `Shape<NewFocus>`:
   - `Attributes(fqn)` — `TypeFocus → AttributeFocus`. With optional
     `min`/`max` for presence-count constraints.
   - `GenericTypeArg(index)` — `AttributeFocus → TypeArgFocus`.
   - `ConstructorArg<T>(index)` — `AttributeFocus → ConstructorArgFocus<T>`.
   - `NamedArg<T>(name)` — `AttributeFocus → NamedArgFocus<T>`.
   - `BaseTypeChain()` — `TypeFocus → BaseTypeChainFocus`.
   - `AsTypeShape()` — `TypeArgFocus → TypeFocus` (re-enter a type shape on
     a navigated type).
   - `Members(filter?)` — `TypeFocus → MemberFocus` (generalises the existing
     `ForEachMember` match).
4. **Leaf predicates on non-type foci**:
   - `AttributeFocus.Exists()` (gated via min/max on the lens)
   - `TypeArgFocus.MustImplement(fqn)`, `.MustExtend(fqn)`,
     `.MustBeParsable()`, `.MustBeSealed()` — all reusing the type-level
     predicate catalogue lifted to apply at a navigated focus.
   - `ConstructorArgFocus<T>.MustBe(value)` / `.MustSatisfy(pred)`.
   - `MemberFocus.MustHaveAttribute(fqn)` — nested navigation (a member
     `SelectMany`'s into its own attributes).
5. **Disjunction** — `Shape.OneOf(shape1, shape2, …)`. Passes when any
   alternative passes; reports a fused diagnostic when none do. Needed for
   `[KeyPart]`'s "readonly partial record struct OR abstract partial record"
   rule.
6. **Quantifiers** — `All(lens, sub)` (every navigated focus must satisfy),
   `Any(lens, sub)` (at least one must), `None(lens, sub)` (built atop
   `ForEachMember`'s current "must not declare" idiom but generalised to any
   lens). Most `.ForEachX` sugar collapses to `All`.
7. **Cross-focus predicates** — `a == b` comparisons lifted to the DSL
   (`SymbolEquals`, `SameOriginalDefinition`, …). Needed for the cycle in
   Event Contract (the `[Applies<E>]` on the aggregate must reference the
   same `evt` we navigated from).
8. **Violation path** — `DiagnosticInfo.FocusPath` — a breadcrumb of lens
   hops so richer reporting (`"on type X, attribute [ComposedOf<TPart>] #2, type argument TPart: does not implement IKeyPart"`)
   can be rendered without losing the root diagnostic's message format.
9. **Query-comprehension desugaring** — nothing formal in the language; just
   ensure the fluent API's method names match LINQ's `SelectMany` / `Where` /
   `Select` so `from / where / select` desugars cleanly. Already true today
   for the shipped pieces; needs to be preserved deliberately.

Items 1–4 are the minimum viable core. Items 5–7 are the features that let
HKey / Event Contract / most frameworks drop back to zero imperative code.
Items 8–9 are quality-of-life on top of a working system.

---

## Worked examples

### HierarchicalKey (fully declarative)

```csharp
public static readonly Shape<KeyModel> Shape =
    Stencil.ExposeAnyType()
        .Implementing(KnownFqns.IHierarchicalKey)
        .MustBeRecordStruct()                                        // SCRIBE032
        .MustBePartial()                                             // SCRIBE001
        .MustBeReadOnly()                                            // SCRIBE033
        .Attributes(KnownFqns.ComposedOf, min: 1)                    // WORD1301
            .GenericTypeArg(0)
                .MustImplementOrHaveAttribute(
                    KnownFqns.IKeyPart, KnownFqns.KeyPart)           // WORD1302
        .Etch<KeyModel>(…);

public static readonly Shape<KeyPartModel> PartShape =
    Stencil.ExposeAnyType()
        .WithAttribute(KnownFqns.KeyPart)
        .OneOf(                                                      // WORD1303
            s => s.MustBeRecordStruct().MustBeReadOnly().MustBePartial(),
            s => s.MustBeAbstract().MustBeRecord().MustBePartial())
        .Attributes(KnownFqns.Discriminator)
            .ConstructorArg<string>(0)
                .MustBeUniqueWithinAttributeSet()                    // WORD1306
        .Attributes(KnownFqns.Param)
            .ConstructorArg<INamedTypeSymbol>(1)
                .MustBeParsable()                                    // WORD1305
        .Etch<KeyPartModel>(…);

public static readonly Shape<DiscriminatorModel> DiscShape =
    Stencil.ExposeAnyType()
        .WithAttribute(KnownFqns.Discriminator)
        .BaseTypeChain()
            .Any(t => t.HasAttribute(KnownFqns.KeyPart))             // WORD1304
        .Etch<DiscriminatorModel>(…);
```

The current analyzer is ~400 lines of imperative walking. The target above is
under thirty. Every `WORD13xx` ID surfaces at the correct span (the
`[ComposedOf<>]` attribute argument for WORD1302, the `[Discriminator("…")]`
argument for WORD1306, the base-list entry for WORD1304).

### Event Contract (the cycle)

```csharp
from cmd     in Stencil.ExposeAnyType().Implementing(KnownFqns.ICommandLaw)
from raises  in cmd.Attributes(KnownFqns.RaisesEvent)
from evt     in raises.GenericTypeArg(0).AsTypeShape()
                  .MustImplement(KnownFqns.IEventLaw)
from applied in evt.Attributes(KnownFqns.AppliedBy)
from agg     in applied.GenericTypeArg(0).AsTypeShape()
where agg.Members(m => m is IMethodSymbol)
         .Any(m => m.Attributes(KnownFqns.Applies)
                    .Any(a => a.GenericTypeArg(0).SymbolEquals(evt)))
select new CommandEventContract(cmd.Model, evt.Model, agg.Model);
```

One query. Violations surface at the exact link in the chain that fails:
missing `[RaisesEvent<T>]` on the command, missing `[AppliedBy<A>]` on the
event, missing `[Applies<E>]` method on the aggregate, or a mismatch in the
cycle.

---

## Hard parts (where to spend the design care)

1. **Fluent vs query-comprehension parity.** The fluent form reads as nested
   `SelectMany`. The query form reads as flat `from / where / select`. Both
   should be first-class and produce identical trees. That constrains method
   naming — `SelectMany` can't be wrapped under `Navigate` or
   `ForEachAttribute` without breaking query comprehension. The DSL's verbs
   should be the LINQ verbs, with sugar extensions on top.

2. **Location propagation.** Today violations carry a single `Location`.
   Nested navigations need a stack. Options: (a) a full breadcrumb
   (`FocusPath`) stored in `DiagnosticInfo`, rendered into the message only
   on materialisation; (b) the innermost focus's location overrides outer
   ones, with the breadcrumb only in the message format. (a) is richer but
   changes the wire format; (b) is minimal. Lean toward (b) for v1 — the
   breadcrumb is redundant with a well-composed `messageFormat`.

3. **Cache correctness at each hop.** Every lens must produce an
   equatable value. `AttributeData` is not equatable out of the box — need
   an `AttributeFocus` wrapper with a stable identity (owning symbol Fqn +
   attribute FQN + syntax reference span). Same for `TypeArgFocus` and the
   constructor-arg foci. Each focus type owns its equality implementation;
   tests enforce it.

4. **Zero-hit semantics.** When a lens returns empty (`.Attributes(X)` on a
   type with no such attribute), is that a violation, a silent pass, or
   filter-out? Depends on the intent. `.Attributes(fqn, min: 1)` makes the
   constraint explicit. Without `min`, the sub-shape applies to zero elements
   — trivially true, filter-out. Make `min`/`max` the only way to constrain
   presence.

5. **Disjunction diagnostics.** `OneOf(A, B)` passes when any alternative
   passes. What does the failure message say? The fused diagnostic should
   list both expectations ("expected readonly partial record struct OR
   abstract partial record — was neither"). Needs a message-format
   convention baked into the `OneOf` primitive.

6. **Cross-focus equality.** `a.SymbolEquals(b)` inside a query
   comprehension requires both `a` and `b` to be in scope and equatable.
   The foci need a `.Symbol` accessor that returns a wrapped-but-equatable
   identity for comparison without leaking raw `ISymbol`s (which aren't
   cache-safe).

7. **Generator vs analyzer consumption.** A single Shape tree feeds both:
   `ToAnalyzer()` reads only the violations; `ToProvider(context)` emits
   `ShapedSymbol<TModel>` where `Model` is the `select` projection. The
   leaf `Project<TModel>` call must be reachable from every navigation path
   — today it lives on `TypeShape` (the type-focus builder). It needs to
   lift to every focus type, or (more likely) remain only at the top level,
   with navigations feeding the model constructor via capture.

---

## What's actually missing to ship this

Measured against the shipped code in `Scribe/Shapes/`:

| Piece | State | Work |
| ----- | ----- | ---- |
| `Lens<TSource, TTarget>` abstraction | Does not exist | New core type. ~100 LOC with docs + tests. |
| `TypeFocus`, `AttributeFocus`, `TypeArgFocus`, `ConstructorArgFocus<T>`, `BaseTypeChainFocus` | Partial — `TypeShape` is an implicit `TypeFocus` | Extract the implicit, add the four new ones. ~400 LOC incl. equality + locators. |
| Built-in lenses (`Attributes`, `GenericTypeArg`, `ConstructorArg`, `NamedArg`, `BaseTypeChain`, `AsTypeShape`) | Do not exist | ~300 LOC + tests. |
| Leaf predicates on non-type foci (`MustImplement`, `MustBeParsable`, `MustBe`, `SymbolEquals`) | Partial — type-level predicates exist but are bound to `TypeShape` | Refactor to be focus-parametric. Mostly mechanical. |
| `Shape.OneOf` | Does not exist | Shape combinator. ~80 LOC + careful diagnostic fusion. |
| `All` / `Any` / `None` quantifiers | `None`-equivalent exists via `ForEachMember`'s "must not declare" pattern | Generalise to all lenses. ~100 LOC. |
| `DiagnosticInfo.FocusPath` (or breadcrumb-in-message) | Does not exist | Decide (2) above first. Probably message-only for v1. |
| Query-comprehension support | Naming is already close (`Project` = `Select`, `ForEachMember` ~= `SelectMany`) | Rename for exact LINQ compat, or add pass-through methods. |
| Fix catalog on navigated foci | N/A for v1 | Deferred. Fixes today operate on type declaration; navigated-focus fixes (e.g. "remove this `[ComposedOf<T>]` attribute") are a Phase 13 concern. |
| Docs + cookbook | Does not exist for this DSL | New page under `docs/`. Replaces this design doc once stable. |

Rough total: **~1000 LOC new code**, ~200 LOC refactored,
~400 LOC of tests. Two focused weeks if the API design is locked up front;
four if the API has to be iterated against real consumers.

The **hard work is the API**, not the engine. The engine is Roslyn's
`IncrementalValuesProvider` plus equatable-value plumbing — both already
working. The engine scales to the design. The design is what decides whether
developers adopt this or fall back to `context.RegisterSyntaxNodeAction`.

---

## Non-goals

- **No support for operation-level or expression-level rules.** Those belong
  in operation-walking analyzers, not Shape. (Unchanged from prior draft.)
- **No attempt to subsume the existing `Prism.By` stream-join.** That
  primitive is correct and complementary — it handles the "two independent
  streams joined by string key" shape that navigation can't express cheaply
  (cross-assembly, orphan-diagnostic semantics). Navigation (Lens) is for
  walking a single symbol's structure; `Prism.By` is for joining two shape
  streams. Both coexist.
- **No autogenerated fixers for navigated-focus violations.** Fix delegates
  (`FixKind.Custom`) already exist; authoring custom fixes for
  attribute-argument or type-arg violations is possible today. A declarative
  navigated-focus fix catalog can come later (Phase 13+) once real consumers
  demonstrate the patterns worth baking in.
