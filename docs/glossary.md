# Scribe — Glossary

**Canonical definitions of the Shape DSL vocabulary and the text-emission primitives.**

**Audience:** anyone reading or writing Scribe code, Scribe documentation, or a generator/analyzer/fixer built on top of Scribe. If you see a term in a Scribe doc that you're unsure about, this is the dictionary.

**Scope:** purely Scribe vocabulary. No Hermetic or application terms. Examples use generic domains (`Thing`, `Widget`, `Order`).

---

## The Core Vocabulary

The entire Shape DSL rests on a small set of concepts drawn from a coherent metaphor family: **photolithography and optics**. A Stencil is the mask you start with. A Shape is that mask focused onto a specific position in the source code. Lenses and Prisms refocus attention. Smudges on the lens carry the breadcrumb that tells a diagnostic where to squiggle. Every other term in this glossary is either a specialisation of one of these or a supporting primitive.

| Term | Stage | Role |
| --- | --- | --- |
| **Stencil** | Door | Static entry point. Exposes the initial focused Shape (`Stencil.ExposeClass()`, `Stencil.ExposeAnyType()`, ...). Before a Shape exists, the Stencil is the mask. |
| **Shape** | Pattern | What must be true at a Focus. Self-composing. Concrete focused classes: `TypeShape`, `AttributeShape`, `TypeArgShape`, `MemberShape`, ... |
| **Focus** | Pattern / Navigation | Where the Shape is grounded — a position in the symbol graph. Each focused Shape pairs with a Focus row type (`TypeShape` ↔ `TypeFocus`). |
| **Lens** | Navigation | Structural refocus — follows an edge the source already provides. |
| **Prism** | Navigation | Keyed refocus — combines two streams by matching computed keys. |
| **Smudge** | Navigation | The breadcrumb a Lens or Prism carries so a Violation squiggles at the right span after the focus has moved. |
| **Etch** | Seal | Terminal model extraction. Seals the chain into `Shape<TModel>`. Invoked as `.Etch<TModel>(builder)`. |
| **Materialisation** | Output | Transforms a sealed `Shape<TModel>` into analyzer, provider, or fixer. |

Five stages, eight concepts. Each stage is **conceptually distinct** and maps to its own type in code — no single type tries to represent the whole pipeline.

**The chipmaking metaphor, end to end.** A Stencil is exposed through UV onto a wafer (Expose); the latent pattern develops as predicates and lens hops accumulate (Develop — implicit in the authoring chain); the final commitment permanently transfers the pattern into the substrate (Etch); the finished die is then packaged into products (Materialisation: analyzer / provider / ink).

**The lifecycle in one diagram:**

```text
Stencil.ExposeAnyType()            ← EXPOSE: latent pattern on resist
  → TypeShape                      ← focused Shape; predicates accumulate
     .MustBePartial()
     .Attributes(Widget)           ← Lens moves focus (smudge carries the span)
        → AttributeShape           ← new focused Shape
           .GenericTypeArg(0)
              → TypeArgShape
                 .AsTypeShape()
                    → TypeShape    ← Lens re-entry at a new type focus
                       .MustImplement(IWidget)
                       .Etch<TModel>(...)   ← ETCH: permanent commitment
                          → Shape<TModel>   ← sealed query; materialisable
                             .ToProvider(ctx)  ← MATERIALISE
                             .ToAnalyzer()
                             .ToInk(...)
```

**Three code-type families:**

| Family | Examples | Phase |
| --- | --- | --- |
| **Stencil** | `Stencil` (static) | Door — not yet a Shape. |
| **<X>Shape** | `TypeShape`, `AttributeShape`, `TypeArgShape`, `MemberShape`, … | Authoring — focused Shape, predicates accumulating. |
| **Shape<TModel>** | `Shape<ThingModel>` | Sealed — projection applied, ready for consumers. |

A `Shape<TModel>` is not a Shape at focus `TModel` — `TModel` is the etched model, not a focus. "Shape" is an umbrella name; the type system discriminates whether you hold an authoring-phase focused Shape or a sealed etched Shape.

---

## The Chipmaking Metaphor — In Full

The DSL's named concepts (Stencil, Expose, Shape, Focus, Lens, Prism, Smudge, Etch, Materialisation) only pick up the **active verbs and nouns** of photolithography. The rest of the chipmaking process has clean analogues too. None of these appear in the code — they're purely mental scaffolding for reasoning about the pipeline.

| Chipmaking | Scribe equivalent | Code name? |
| --- | --- | --- |
| **Wafer** — raw silicon substrate the fab works on | The Roslyn `Compilation`. The stream of declarations every Shape is scanning. | No — it's "the Compilation" in code. |
| **Photomask / Stencil** — the pattern template | `Stencil` — the static door. | ✅ `Stencil` |
| **Exposure** — UV projects mask onto photoresist | `Stencil.Expose*()` — creates the initial focused Shape. | ✅ `ExposeClass`, `ExposeAnyType`, … |
| **Photoresist** — reactive layer that records the pattern | The focused Shape's accumulating predicates, lens hops, and prism joins during authoring. The latent image of the pattern. | No — it's just "the authoring chain." |
| **Development** — chemicals reveal the latent image | Implicit in the fluent chain. Each `MustBeX` / `Lens` call sharpens the image that's already on the photoresist. | No — there is no `Develop` call. |
| **Etch** — pattern is permanently transferred into substrate | `.Etch<TModel>(...)` — seals the chain into `Shape<TModel>`. | ✅ `Etch` |
| **Die** — one individual chip's worth of pattern on the wafer | `ShapedSymbol<TModel>` — one matched declaration, carrying its model, violations, and source location. One die per surviving row. | ✅ `ShapedSymbol<TModel>` |
| **Dicing** — cutting the wafer into individual dies | Implicit in the `IncrementalValuesProvider<ShapedSymbol<TModel>>` — Roslyn yields one row at a time. | No — handled by Roslyn's incremental pipeline. |
| **Packaging** — fitting a die into DIP / QFN / BGA | `ToAnalyzer()` / `ToProvider(ctx)` / `ToInk(...)`. Same die, three packages. | ✅ Materialisation. |
| **Chip / IC** — the finished, shipped product | The analyzer / generator / fixer DLL that the Scribe consumer actually references. Scribe vanishes into the thing it produces. | No — it's just your analyzer. |
| **Fab** — the photolithography facility itself | Roslyn. Scribe hands design rules to a fab that already exists. | No — Roslyn is Roslyn. |
| **Design Rule Check (DRC)** — pre-fab static verification | What `ToAnalyzer()` does, at the level of the Shape DSL. | No — it's called `Analyzer`. |

**The full arc, in one sentence.** The Compilation is a wafer flowing through Roslyn's fab; the Stencil exposes a latent pattern into the photoresist of the authoring chain; accumulated predicates develop the image; `.Etch<TModel>(...)` commits each match as a die (`ShapedSymbol<TModel>`); `ToAnalyzer` / `ToProvider` / `ToInk` package each die into the chip — analyzer, generator, or fixer — that the consumer drops into their project.

**Why this matters.** Scribe's naming is deliberately sparse — we don't need a `Develop()` method, because development is already what fluent authoring *is*. The metaphor exists in full in the user's head; the code surfaces only the moments where a name earns its weight.

---

## Stencil

The door. A static class whose factory methods expose the initial focused Shape. Before a Shape exists, the Stencil is the mask — an unfocused template of "I'm going to match *something* of this general kind."

Factory methods all begin with `Expose`, echoing the lithography step where UV light projects the mask pattern onto the photoresist and the latent image first appears on the wafer.

```csharp
TypeShape anyType   = Stencil.ExposeAnyType();
TypeShape classOnly = Stencil.ExposeClass();
TypeShape records   = Stencil.ExposeRecord();
TypeShape structs   = Stencil.ExposeStruct();
TypeShape ifaces    = Stencil.ExposeInterface();
```

The object returned by `Expose*` is a focused Shape. From that moment on, every predicate sharpens specificity and every Lens moves focus to a new position. The Stencil itself is never mutated — it's a stateless door.

---

## Shape

The pattern — what must be true at a Focus. A Shape is a specification: "a type that implements `IThing`, is partial, is sealed, and has at least one `[Widget<>]` attribute."

Shape is **self-composing.** A Shape can contain sub-Shapes reached through Lenses or Prisms, and each sub-Shape is verified at its own Focus. There is no separate "query" concept — a composition of Shapes is itself a Shape. Every example in the docs, no matter how deeply nested, is a single Shape.

```csharp
Shape<ThingModel> shape = Stencil.ExposeAnyType()
    .Implementing(KnownFqns.IThing)
    .MustBePartial()
    .MustBeSealed()
    .Attributes(KnownFqns.Widget, min: 1)
        .GenericTypeArg(0)
            .MustImplement(KnownFqns.IWidget)
    .Etch<ThingModel>(t => new ThingModel(t.Fqn));
```

**Two code-level forms.** The focused authoring forms are concrete classes named `<X>Shape` (`TypeShape`, `AttributeShape`, `TypeArgShape`, `MemberShape`, ...). Each carries the predicates accumulated so far and the lens/prism chain that led there. Once `.Etch<TModel>(...)` is called, the chain seals into `Shape<TModel>` — the terminal, equatable, materialisable form. Authoring and sealed forms are distinct types; the compiler prevents calling predicates on a sealed Shape.

---

## Focus

Where the Shape is grounded. A Focus is a **Shape focused at a specific position** in the C# symbol graph — exactly the sense of "focus" one uses when speaking of where attention rests.

Every Focus is an equatable wrapper over a Roslyn symbol plus a breadcrumb to its origin (for cache stability and correct diagnostic squiggle location). Roslyn's raw symbols are not stable across compilations; Focus types derive identity from fully-qualified names plus syntax spans so the incremental pipeline can cache on Focus equality.

Seven Focus types cover every position in the graph a diagnostic can squiggle at:

| Focus | Wraps | Where it points |
| --- | --- | --- |
| `TypeFocus` | `INamedTypeSymbol` | A type declaration. |
| `AttributeFocus` | `AttributeData` | An attribute usage on a type, member, or parameter. |
| `TypeArgFocus` | `ITypeSymbol` + index | A single type argument inside a generic attribute, base-list entry, or method signature. |
| `ConstructorArgFocus<T>` | `TypedConstant` + index | A positional argument passed to an attribute's constructor. |
| `NamedArgFocus<T>` | `TypedConstant` + name | A named argument on an attribute (`[Foo(Bar = 42)]`). |
| `BaseTypeChainFocus` | sequence of `INamedTypeSymbol` | The inheritance chain of a type, step by step. |
| `MemberFocus` | `ISymbol` (field/property/method/event) | A member inside a type declaration. |

Predicates are **focus-specific** — `MustBePartial` applies to a `TypeFocus`; `MustBeParsable` applies to a `ConstructorArgFocus<INamedTypeSymbol>`. The compiler enforces this: you can't call a predicate at a Focus where it doesn't apply.

---

## Lens

Structural refocus. A Lens redirects attention from one Focus to another along an edge the symbol graph already provides — attributes, type arguments, constructor arguments, members, base types. Pre-ground optics: Roslyn gives you these paths, the Lens exposes them declaratively.

Written `Lens<TSource, TTarget>`. Internally: a `Func<TSource, IEnumerable<TTarget>>` plus a location-propagation function that carries the destination's source span back to any violation reported there.

A Lens is a `SelectMany`. Lenses chain to reach deep positions; each refocuses the view. A query like *"for every type implementing X, for every `[Foo<T>]` attribute, `T` must implement Y"* is three Lens hops.

Built-in lenses:

| Lens | From | To | What it does |
| --- | --- | --- | --- |
| `Attributes(fqn)` | `TypeFocus` / `MemberFocus` | `AttributeFocus` | All attributes of the given FQN applied to the focus. Optional `min` / `max` for presence-count constraints. |
| `GenericTypeArg(index)` | `AttributeFocus` | `TypeArgFocus` | The type argument at the given generic position. |
| `ConstructorArg<T>(index)` | `AttributeFocus` | `ConstructorArgFocus<T>` | The positional argument at the given index. |
| `NamedArg<T>(name)` | `AttributeFocus` | `NamedArgFocus<T>` | The named argument with the given name. |
| `BaseTypeChain()` | `TypeFocus` | `BaseTypeChainFocus` | The full inheritance chain, ordered from the type up to `object`. |
| `AsTypeShape()` | `TypeArgFocus` | `TypeFocus` | Re-enter a type-level Shape on a navigated type. Enables predicates like `MustImplement` on a generic argument. |
| `Members(filter?)` | `TypeFocus` | `MemberFocus` | All members matching an optional filter. |

**Presence constraints live on the Lens.** `min` and `max` on a Lens express "I expect at least / at most N of these." Without them, a zero-hit Lens silently passes (sub-Shape applies to zero rows — trivially true). This is the only way to constrain presence.

---

## Prism

Keyed refocus. A Prism combines two independent Shape streams by matching on a computed key. Custom-cut optics for cross-stream joins where no structural edge exists.

In physics, a prism combines or separates light by wavelength. In the DSL, a Prism combines or separates streams by key. Same metaphor family as Lens — both are optical elements of the instrument — but where a Lens follows a pre-existing path, a Prism **matches on a property you grind into it**.

```csharp
// Every left row must have a matching right row; orphans become errors.
Prism.By(
    left:  widgets.Select(w => (key: w.EventFqn, w)),
    right: handlers.Select(h => (key: h.HandlesFqn, h)),
    mode:  PrismMode.RequireLeftHasRight);
```

Prism modes:

| Mode | Semantic |
| --- | --- |
| `RequireLeftHasRight` | Every left row must have at least one matching right row. Orphans on the left become errors. |
| `WarnOnRightUnused` | Right rows without a matching left row raise warnings. |

**When to reach for Prism instead of Lens.** A Lens works when the target is reachable from the source via the symbol graph's existing structure. A Prism works when two streams are genuinely independent and you need to relate them by a key you compute — cross-assembly orphan checks, string-keyed registrations, synthesised identity matches.

---

## Smudge

The breadcrumb a Lens or Prism carries so a Violation surfaces at the right source span after the focus has moved.

When a Lens hops from an `AttributeFocus` into a `TypeArgFocus`, the destination's syntax span has to travel with it — otherwise a `MustImplement` failure three hops deep would squiggle on the root type instead of the attribute's type argument. Each Lens and Prism carries a small location-propagation function; together the chain of these functions forms a trail of smudges back to the starting Focus.

A smudge is always a `LocationInfo?` — cache-stable, equatable, and scoped to one hop. Most authors never touch one directly; Ink and the analyzer pipeline consume them when building the final `Diagnostic`.

**Etymology.** The squiggle the compiler draws on the reader's code is, in a sense, the compiler's own smudge — a mark that says *look here, something is off*. In Scribe, every lens and prism hop leaves a smudge of its own so the final squiggle lands on the right character.

---

## Etch

Terminal commitment. Written `.Etch<TModel>(builder)`. Takes the current Focus and produces an equatable `TModel`, sealing the authoring-form Shape into `Shape<TModel>`. The stream of `ShapedSymbol<TModel>` values is what every Materialisation consumer reads.

Etch is the **`SELECT`** of the relational-algebra framing: after all the patterns and navigations, this is what each surviving row looks like. It is also the lithography step where the developed pattern is permanently transferred into the substrate — after etching, no more predicates or lenses can be applied; the die is committed.

`TModel` **must be equatable.** Prefer records; the incremental pipeline uses equality to skip downstream work when nothing has changed. If `TModel` is not equatable, every compilation re-runs every generator stage.

---

## Materialisation

Transforms a composed Shape into one of three output pipelines. Each consumer reads a different part of the same Shape; the Shape itself is authored once.

| Transform | Produces | Reads | Typical use |
| --- | --- | --- | --- |
| `ToAnalyzer()` | `DiagnosticAnalyzer` | `ShapedSymbol.Violations` | *"Prove the code has the expected shape."* |
| `ToProvider(context)` | `IncrementalValuesProvider<ShapedSymbol<TModel>>` | Clean `ShapedSymbol<TModel>` rows | *"For each surviving row, emit code."* |
| Ink | `CodeFixProvider` | Diagnostics from the analyzer | *"For each diagnostic, produce a patch."* |

The three are independent. Ship any subset. A Shape can back an analyzer alone, a generator alone, all three, or any combination.

---

## Supporting Vocabulary

These primitives live underneath the core vocabulary. They appear in the DSL's surface but are specialisations or extensions of the main concepts.

### ShapedSymbol

The equatable unit of output from a Shape. Written `ShapedSymbol<TModel>`. Carries:

| Field | What it is |
| --- | --- |
| `Fqn` | The fully-qualified name of the root symbol. Cache key. |
| `Model` | The projected model (type `TModel`). |
| `Location` | Source span for the root. Used for diagnostics. |
| `Violations` | Any predicate failures accumulated during evaluation. |

Every `ShapedSymbol` is immutable, equatable, and safe to flow through `IncrementalValuesProvider`.

### Predicate

A test applied at a Focus. Pass = silent. Fail = **Violation**.

Predicates are focus-specific. Type-level predicate catalogue (shipped): `MustBePartial`, `MustBeSealed`, `MustBeRecord`, `MustBeReadOnly`, `MustBeRecordStruct`, `MustNotBeNested`, `Implementing(fqn)`, and peers.

### Violation

A Predicate failure expressed as a structured record carrying the diagnostic ID, message, and source span. Violations accumulate per `ShapedSymbol`. `ToAnalyzer()` reports them; `ToProvider()` typically filters them out (generation proceeds only for clean Shapes).

A Shape with any Violations is not *invalid* — it is a Shape whose consumer must decide what to do with the failures.

### Disjunction (`OneOf`)

`Stencil.OneOf(shape1, shape2, …)`. Passes when any alternative passes. When all fail, reports a **fused diagnostic** listing every unsatisfied expectation.

Used when a symbol may legitimately take one of several valid forms (e.g. "must be a readonly partial record struct OR an abstract partial record").

### Quantifier

Higher-order predicates that apply a sub-Shape across a Lens's output set.

| Quantifier | Passes when |
| --- | --- |
| `All(lens, sub)` | Every navigated Focus satisfies `sub`. |
| `Any(lens, sub)` | At least one navigated Focus satisfies `sub`. |
| `None(lens, sub)` | No navigated Focus satisfies `sub`. |

`All` is the common case and usually implicit in a Lens chain. `Any` and `None` make "at least one" and "none of" explicit.

### Cross-focus Predicate

A Predicate that compares two Foci reached by different navigation paths in the same Shape. Written with accessors like `SymbolEquals(a, b)` or `SameOriginalDefinition(a, b)`.

Requires the query-comprehension form of the DSL so both Foci are captured as named variables, then a `SymbolEquals` predicate in the `where` clause. Needed for cyclic verifications — for example, confirming that a method's `[Handles<E>]` type argument matches the event type the outer chain navigated to.

### FocusPath *(planned)*

A breadcrumb of every Lens / Prism hop that led to the current Focus. Written into `DiagnosticInfo.FocusPath`. Enables rich diagnostic messages like *"on type X, attribute `[Foo<T>]` #2, type argument `T`: does not implement `IBar`"* without losing the root diagnostic's message format.

---

## Ink — The Fixer Pipeline

The third Materialisation consumer. Ink takes the diagnostics a Shape's analyzer produces and emits code fixes.

### Ink

The fixer infrastructure. Materialises a diagnostic stream as `CodeFixProvider`s. Shares an equivalence key per `FixKind` so fixers support "Fix All in Solution".

### FixKind

A built-in category of fix that Ink knows how to apply. Twenty-one shipped kinds cover modifiers (`MakePartial`, `MakeSealed`, …), base lists, attributes, visibility, and constructors. Each has a stable equivalence key.

### `FixKind.Custom`

Escape hatch for bespoke rewrites that don't fit a built-in kind. Paired with `Ink.WithCustomFix(tag, delegate)` to register the rewrite logic against a stable tag.

---

## Member-Level Rules (shipped subset)

The portion of the Shape DSL that navigates from a type to its members. The member-level primitives ship today; navigation to attributes / type arguments / base-type chains is the evolution described in [design-member-level-shapes.md](design-member-level-shapes.md).

| Term | Definition |
| --- | --- |
| `MemberCheck` | A Predicate applied to each member returned by `Members(filter?)` (today: `ForEachMember`). |
| `MemberDiagnosticSpec` | The diagnostic descriptor (ID, message, severity) a `MemberCheck` reports when it fails. |
| `MemberSquiggleAt` | An enum that names the source span a member-level diagnostic targets: the identifier, the full declaration, the type annotation, or the first attribute. |
| `MemberSquiggleLocator` | The resolver that translates a `MemberSquiggleAt` into a concrete `Location`. |

---

## Text Emission

The pre-Shape-DSL half of Scribe. These primitives remain central for anything that generates source code — the Shape DSL tells you *what* to generate; these primitives are *how* you emit it.

### Quill

The fluent source builder at the heart of Scribe. Handles indentation, blank-line separation, using-directive collection, namespace resolution, and XML documentation.

```csharp
Quill quill = Quill.Begin("global::MyNamespace.Generated");
quill.Using("System")
     .Blank()
     .Line($"public sealed class {name}")
     .Block(body => body.Line("..."));
string source = quill.Inscribe();
```

The `Inscribe()` method finalises the builder and returns the generated source. Every output ends with `// I HAVE SPOKEN` — the generative act is complete.

### Template

A structured output scaffold used when the shape of the generated file is fixed but the contents vary. Cuts repetition when multiple generators emit near-identical file structures.

### Naming

Naming-convention helpers for generated identifiers. Converts between cases (`PascalCase`, `camelCase`, `snake_case`), derives generated type names from source symbols, and escapes C# keywords.

### XmlDoc

XML documentation extraction and generation utilities. Reads doc comments off Roslyn symbols and re-emits them (escaped, aligned, wrapped) onto generated members.

### WellKnownFqns

Constants for fully-qualified type names of BCL types (`System.String`, `System.Collections.Generic.IEnumerable<T>`, etc.). Using constants avoids typos that silently break symbol lookups at compile time.

### SyntaxPredicates

Reusable predicate helpers for `ForAttributeWithMetadataName` and other syntax-level filters used inside `IncrementalGenerator` pipelines. Stable equality, cheap to evaluate.

### NamespaceWalker

Utility for walking namespace hierarchies in syntax trees. Resolves `using` directives, emits them with `global::` prefixes, and short-circuits `using` collapse where safe.

### ScribeHeader

Assembly-level attribute (`[assembly: ScribeHeader("…")]`) that brands every generated file with a decorative page header. Quill auto-discovers it via `GetCallingAssembly()`.

---

## Build and Tooling

### Stubs

`netstandard2.0` polyfill types (`init`, `record`, `required`, nullable annotations) in `Stubs.cs`, guarded by `#if !NET5_0_OR_GREATER`. Analyzers must target `netstandard2.0` because the Roslyn compiler hosts them on .NET Framework-equivalent surfaces — these stubs let modern C# language features compile against that target.

### LocalDev

Scribe's build infrastructure for multi-repo local NuGet package development. A sentinel file activates props/targets that auto-pack on build, apply `-dev.<timestamp>` version suffixes, and generate a local NuGet source registration so sibling repos resolve newly-built packages without NuGet cache poisoning.

### BulletsForHumanity.Scribe.Sdk

The MSBuild SDK that configures analyzer/generator projects with zero boilerplate — sets the correct target framework, adds Roslyn references, enforces analyzer rules, and wires `EnforceExtendedAnalyzerRules`.

---

## Cross-Reference

| Vocabulary family | Home document |
| --- | --- |
| Stencil, Shape, Focus, Lens, Prism, Smudge, Etch, Materialisation | This glossary + [dsl.md](dsl.md) |
| Design rationale, planned primitives | [design-member-level-shapes.md](design-member-level-shapes.md) |
| Quill API details | [quill-reference.md](quill-reference.md), [architecture-quill.md](architecture-quill.md) |
| Transform → Register → Render pipeline | [writing-generators.md](writing-generators.md) |
| Project configuration, LocalDev | [project-setup.md](project-setup.md) |
| Build infrastructure internals | [architecture-infrastructure.md](architecture-infrastructure.md) |
