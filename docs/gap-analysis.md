# Scribe — Gap Analysis

**Measuring the current implementation against the six-word vocabulary (Shape, Focus, Lens, Prism, Projection, Materialisation) and the full Shape DSL vision.**

**Audience:** anyone doing the work, or deciding what to do next. Pair this with the [glossary](glossary.md) (the vocabulary) and [dsl.md](dsl.md) (the authored surface) and [design-member-level-shapes.md](design-member-level-shapes.md) (the architectural rationale).

---

## Scope

Two independent gap analyses:

- **Part A — Refactoring gaps.** Changes needed to align the *existing* code and docs to the new vocabulary. Mostly renames, extractions, and clarifications. No new capability. Keeps the surface coherent so the feature work can land on a clean foundation.
- **Part B — Feature gaps.** Capabilities missing today that the vision requires. Navigation, disjunction, quantifiers, cross-focus predicates, diagnostic breadcrumbs. The meat.

Build order matters. Part A unblocks Part B — trying to add `Lens<TSource, TTarget>` before extracting `TypeFocus` would bake the implicit-focus assumption into the new abstraction.

---

## Design Principle — Escape Hatches as First-Class Citizens

Some constraints won't fit the general DSL cleanly. That's not a failure mode — it's expected. Any declarative surface that covers every shape of query the framework-authoring world can dream up would be unusable. What matters is **how the escape hatch fits in**.

The rule: **every custom/bespoke path must be a natural entry point into the DSL, not a bolted-on appendage.** The author should feel they're reaching for the same fluent surface — just at a position where the built-in vocabulary runs out.

Scribe already has one exemplar of this done well, and one implicit one:

- **`FixKind.Custom` + `Ink.WithCustomFix(tag, delegate)`.** Twenty built-in fix kinds plus one slot for bespoke rewrites, dispatched via the *same* enum, keyed by a stable tag, registered through the same Ink surface. A custom fix doesn't look or feel different from a built-in fix at the call site — the escape hatch is flush with the wall.
- **`Project<TModel>(builder)`.** The terminal projection is an arbitrary user lambda, but it sits at the natural end of every Shape chain. Nobody thinks of it as an escape hatch even though it is one — which is the point.

New primitives added in Parts A and B should follow the same discipline. Every DSL built-in ships with (or explicitly justifies the absence of) a custom counterpart at the same fluent position. Three places where this is already load-bearing:

- **V.5 set aggregations.** Built-ins are primary: `Unique()`, `UniqueBy(key)`, `CardinalityAtMost(n)`, `CardinalityExactly(n)` cover the common cases and must exist as first-class verbs. Nested escape hatch: `.SatisfyingSet(customPredicate, diagnosticSpec)` for the long tail — takes a `Func<IReadOnlyList<TFocus>, IEnumerable<SetViolation>>`, lives at the same fluent position, uses the same diagnostic-dispatch path.
- **Leaf predicates on any focus.** Built-ins primary: the `MustBeX` catalogue per focus (`MustBePartial`, `MustImplement`, `MustBeParsable`, `MustNotBeEmpty`, …). Nested escape hatch: `.Satisfy(customPredicate, diagnosticSpec)` alongside them, so authors can bolt on custom checks without dropping to imperative Roslyn. One verb family, two depths of customisation.
- **V.4 `MustSatisfy(shape)` as a composition seam — not an escape hatch.** `MustSatisfy` is a built-in primitive, same as `MustBePartial`. The fact that the embedded Shape can contain arbitrary author logic doesn't make `MustSatisfy` an escape hatch — it makes it a seam, like `Project<T>(lambda)` or `OneOf(A, B, …)`. Seams accept user code at a well-defined position; escape hatches bypass the vocabulary. They look similar but they're different things.

**What this forbids.** A separate `ScribeBespoke` namespace, a `RawRoslyn()` escape from the fluent chain, a set of standalone helpers living outside the Shape surface. If someone reaches for a custom predicate and has to leave the DSL to write it, we've failed.

**Where this lives in the plan.** Not a separate work item — a reviewer's checklist. Every primitive added in Part A or Part B must ship with (or explicitly justify the absence of) a custom counterpart that feels native. Document the escape hatch in the [glossary](glossary.md) and [dsl.md](dsl.md) next to the built-ins.

---

## Part A — Refactoring Gaps

Six tasks. Ordered by how much they unblock downstream work.

### A.1 Rename `Relation.Pair` → `Prism`

**Status (April 2026). ✅ Shipped.** `Scribe/Shapes/Prism.cs`, `Scribe/Shapes/ShapedPrism.cs` are the current surface. `Relation` / `PairBuilder` / `ShapedPair` no longer exist in the tree. No call sites in downstream consumers reference the old names.

### A.2 Extract `TypeFocus` as an explicit type

**Why.** Today `TypeShape` *is* the type focus — the focus is implicit in the builder. Every other focus type (Attribute, TypeArg, ConstructorArg, NamedArg, BaseTypeChain) will be a first-class equatable wrapper. If `TypeFocus` stays implicit, the new foci will live in a parallel universe and the focus-parametric predicate plan falls apart.

**What exists.** `TypeShape` in `Scribe/Shapes/TypeShape.cs` — accumulates predicates, carries the `INamedTypeSymbol`, projects to `Shape<TModel>` via `Project<TModel>(...)`.

**Target shape.**

- New `TypeFocus` record/struct wrapping `INamedTypeSymbol` + origin breadcrumb (syntax span for cache stability).
- `TypeShape` becomes `Shape<TypeFocus>` — the fluent builder parameterised by focus.
- Every predicate method moves to an extension on `Shape<TypeFocus>` (see A.3).
- `Project<TModel>(...)` remains terminal, unchanged on the surface, but internally takes the focus as input.

**Effort.** ~2 days. Care needed to keep existing authored Shapes source-compatible (same method names, same chain shape).

**Status (April 2026).** ⏳ **Deferred** per maintainer direction. A minimal-invasion
retrofit has shipped: `ShapeCheck` delegates now take `TypeFocus`, and `TypeShape`'s
central `AddCheck` / `Check(...)` wrap `INamedTypeSymbol` lambdas into
`TypeFocus` lambdas at the boundary so every existing `MustBe*` predicate continues
to authorise unchanged. Full extraction (moving 40+ `MustBe*` members to extensions
on `Shape<TypeFocus>`) remains on the backlog; the retrofit makes it a pure move
with zero callsite blast radius when the decision is made.

### A.3 Make the predicate catalogue focus-parametric

**Why.** Today every `MustBeX` lives on `TypeShape`. When `AttributeFocus` and friends arrive (Part B), they need their own predicate catalogues (`MustHaveConstructorArg`, `MustBeParsable`, …). The discipline is: **predicates are focus-specific**, enforced by the type system.

**What exists.** Fifteen-plus predicates on `TypeShape`: `MustBePartial`, `MustBeSealed`, `MustBeAbstract`, `MustBeStatic`, `MustBeNamed`, `MustBeGeneric`, `MustBePublic/Internal/Private`, `Implementing`, `MustHaveAttribute`, `MustExtend`, `MustBeInNamespace`, negations.

**Target shape.**

- Move all type-level predicates to extensions on `Shape<TypeFocus>` (or a `TypeFocusPredicates` static class used as extensions).
- The predicate implementation (`Func<INamedTypeSymbol, Compilation, CancellationToken, bool>`) becomes `Func<TypeFocus, Compilation, CancellationToken, bool>`.
- Namespace the static predicate class so IntelliSense on `Shape<AttributeFocus>` *can't* offer `MustBePartial`. Compiler-enforced focus-specificity.

**Effort.** ~1 day after A.2. Mostly mechanical — each predicate is a thin wrapper.

### A.4 Rename / generalise `ForEachMember` to `Members` lens

**Why.** `ForEachMember(match, spec)` is the existing member navigation — but it bundles navigation, predicate, and diagnostic spec into one imperative call. In the new world, `Members(filter?)` is a **Lens** (`TypeFocus → MemberFocus`), and any predicate that follows runs at the new focus with its own diagnostic.

**What exists.** `TypeShape.ForEachMember(Func<ISymbol, bool> match, MemberDiagnosticSpec)` — evaluates the match, reports the diagnostic per member. Paired with `MemberCheck`, `MemberDiagnosticSpec`, `MemberSquiggleAt`, `MemberSquiggleLocator`.

**Target shape.**

- Add `Members(filter?)` as a lens on `Shape<TypeFocus>` returning `Shape<MemberFocus>`.
- Keep `MemberFocus` wrapping `ISymbol` + `MemberSquiggleLocator` for span resolution.
- `MemberDiagnosticSpec` becomes redundant in the lens form — diagnostics come from predicates on the member focus, not a baked-in spec.
- Leave `ForEachMember` as a deprecated alias *only* if real consumers depend on it today (check Hermetic.Logos). Otherwise remove.

**Effort.** ~2 days (includes wiring at least one `MemberFocus` predicate so the replacement has teeth).

**Status (April 2026).** ⏳ **Deferred pending design call.** `Members(...)` lens and
`FocusShape<MemberFocus>` leaf predicates (B.4) are already live and functionally
subsume `ForEachMember`. The hold-up is whether `FocusCheck<TFocus>` should grow
squiggle-target / auto-fix metadata to fully replace the `MemberSquiggleAt` /
`MemberSquiggleLocator` channel that `ForEachMember` uses today — or whether member-level
fixes continue to route through a separate path. Not blocking downstream consumers.

### A.5 Update `docs/dsl.md` to match the new vocabulary

**Status (April 2026). ✅ Shipped.** `dsl.md` was scrubbed during the Prism rename; the remaining `Relation` mentions are SQL relational-algebra terminology (the glossary analogy), not the removed `Relation.Pair` primitive. Non-Goals already references Prism directly.

---

## Part B — Feature Gaps

Nine capabilities missing today. Ordered by dependency. B.1–B.4 are the minimum viable core. B.5–B.7 are the features that let real consumers (HKey, Event Contract, EF/MediatR/ASP.NET routing analyzers) drop to zero imperative code. B.8–B.9 are polish.

### B.1 `Lens<TSource, TTarget>` abstraction

**What.** A `Func<TSource, IEnumerable<TTarget>>` plus a location-propagation function. The single foundation every navigation below is an instance of.

**Surface.**

```csharp
public sealed record Lens<TSource, TTarget>(
    Func<TSource, IEnumerable<TTarget>> Navigate,
    Func<TTarget, Location?> LocateTarget);
```

**Depends on.** A.2 (TypeFocus extracted) — so the first consumer can be `Shape<TypeFocus>.Attributes(fqn)`.

**Effort.** ~1 day. Small type; the care is in the equality semantics of the outputs it produces.

### B.2 New Focus types

Each is an equatable wrapper over a Roslyn symbol plus a breadcrumb for cache stability and diagnostic span resolution.

| Focus | Wraps | Span source |
| --- | --- | --- |
| `AttributeFocus` | `AttributeData` + owning symbol Fqn | `ApplicationSyntaxReference` |
| `TypeArgFocus` | `ITypeSymbol` + index | `TypeSyntax` at the generic position |
| `ConstructorArgFocus<T>` | `TypedConstant` + index | argument expression syntax |
| `NamedArgFocus<T>` | `TypedConstant` + name | `name = value` syntax |
| `BaseTypeChainFocus` | sequence of `INamedTypeSymbol` | base-list entry per step |
| `MemberFocus` | `ISymbol` (field/property/method/event) | already covered by `MemberSquiggleLocator` |

**Depends on.** A.2 (TypeFocus model to copy).

**Effort.** ~3 days. Equality + location resolvers + unit tests for each. `MemberFocus` is mostly already built (see A.4).

### B.3 Built-in lenses

Each is an extension method on a focus, returning a `Shape<NewFocus>`.

| Lens | From | To | Presence params |
| --- | --- | --- | --- |
| `Attributes(fqn)` | `TypeFocus` / `MemberFocus` | `AttributeFocus` | `min`, `max` |
| `GenericTypeArg(index)` | `AttributeFocus` | `TypeArgFocus` | — |
| `ConstructorArg<T>(index)` | `AttributeFocus` | `ConstructorArgFocus<T>` | — |
| `NamedArg<T>(name)` | `AttributeFocus` | `NamedArgFocus<T>` | — |
| `BaseTypeChain()` | `TypeFocus` | `BaseTypeChainFocus` | — |
| `AsTypeShape()` | `TypeArgFocus` | `TypeFocus` | — |
| `Members(filter?)` | `TypeFocus` | `MemberFocus` | `min`, `max` (new) |

**Depends on.** B.1 (Lens), B.2 (Focus types).

**Effort.** ~3 days. Seven lenses × ~50 LOC + tests. Presence-constraint semantics (`min`/`max`) need tight test coverage.

### B.4 Focus-specific leaf predicates

Once B.3 lands, every navigated focus needs its own predicate catalogue.

| Focus | Predicates | v1 status |
| --- | --- | --- |
| `AttributeFocus` | (Existence is gated via `min`/`max` on the lens; no predicates needed at v1) | ✅ — lens-level |
| `TypeArgFocus` | `MustImplement(fqn)`, `MustDeriveFrom(fqn)`, `MustBeParsable()`, re-use via `AsTypeShape()` | ✅ `MustImplement`, `MustDeriveFrom`, `AsTypeShape`; ⏳ `MustBeParsable` deferred |
| `ConstructorArgFocus<T>` | `MustBe(value)`, `MustSatisfy(pred)`, `MustNotBeEmpty()` | ✅ `MustBe`, `MustNotBeEmpty`; `Satisfy(pred, id, title, …)` available via the generic escape hatch |
| `NamedArgFocus<T>` | same as `ConstructorArgFocus<T>` | ✅ same status |
| `BaseTypeChainFocus` | quantifier-only (see B.6) | ⏳ B.6 |
| `MemberFocus` | `MustHaveAttribute(fqn)`, `MustBePublic`, `MustBeReadOnly`, `MustBeStatic`, … | ✅ `MustHaveAttribute`, `MustBePublic`, `MustBeStatic`, `MustBeReadOnly`; broader catalog (`MustBeVirtual`, `MustReturnTask`, accessibility matchers) deferred |

**Depends on.** A.3 (predicates made focus-parametric), B.2 (foci exist).

**Effort.** ~3 days. Each predicate is small; the volume adds up.

**v1 scope landed (April 2026).** Representative subset of each focus's catalog plus a generic
`Satisfy<TFocus>` escape hatch (two overloads: simple predicate, and
`Compilation`+`CancellationToken`) living on every `FocusShape<TFocus>` via
[`FocusShapeEscapeHatches`](../Scribe/Shapes/FocusShapeEscapeHatches.cs). Diagnostic IDs
reserved: `SCRIBE060`–`SCRIBE069` member-level, `SCRIBE070`–`SCRIBE079` type-arg-level,
`SCRIBE080`–`SCRIBE084` constructor-arg-level, `SCRIBE085`–`SCRIBE089` named-arg-level.

**Deferred — architectural, not sugar.**

- Auto-fix metadata on `FocusCheck<TFocus>` (`Target` / `Fix`) — violations on nested
  foci currently land on the parent lens's smudge anchor. The code-fix layer is
  root-only in v1; lift when a consumer demonstrates a lens-level fix that can't be
  reformulated as a root fix.

Everything else (typed `MustSatisfy`, `MustBeParsable`, expanded `MemberFocus` catalog) is sugar over the generic `Satisfy<TFocus>` escape hatch and is removed from the backlog. Ship on demand if a real consumer shows up.

### B.5 `Shape.OneOf` disjunction

**Status (April 2026). ✅ Shipped** (v1 subset).

**What.** Two entry points into a disjunction carrier (`OneOfTypeShape`) that
composes two or more fully-authored `TypeShape` alternatives:

- **`Stencil.OneOf(params TypeShape[] alternatives)`** — reads as an authoring
  verb; best for new disjunctions written inline.
- **`TypeShape.OneOf(params TypeShape[] others)`** — instance form; best when
  you already have one alternative in hand and want to compose siblings.

Both produce the same `OneOfTypeShape`, which exposes only `.Etch<TModel>(...)` —
the disjunction is terminal: it cannot accumulate further lenses since those
would be ambiguous across branches.

```csharp
var readonlyStruct = Stencil.ExposeRecordStruct()
    .MustBeReadOnly()
    .MustBePartial();

var abstractRecord = Stencil.ExposeRecord()
    .MustBeAbstract()
    .MustBePartial();

var shape = Stencil.OneOf(readonlyStruct, abstractRecord)
    .Etch<KeyPartModel>(ctx => new KeyPartModel(ctx.Fqn));
```

**Semantics.** For each symbol whose kind matches ≥1 alternative (and whose
primary attribute/interface matches, if declared):

1. Evaluate each alternative's checks + lens branches into a private scratch.
2. If any scratch is empty → overall silent.
3. Otherwise emit a single fused diagnostic at the symbol's origin listing the
   unsatisfied check IDs per branch, separated by `—OR—`.

**Reserved IDs (shipped).**

| Purpose                              | ID         |
| ------------------------------------ | ---------- |
| Fused "no alternative passed"        | SCRIBE100  |
| Branch kind-mismatch marker          | SCRIBE101  |

Authors override the fusion diagnostic via `fusionSpec: new DiagnosticSpec(...)`.

**v1 restrictions.** Enforced at authoring time with `ArgumentException`:

- All alternatives must share the same primary attribute driver (or none).
- All alternatives must share the same primary interface driver (or none).
- Alternatives cannot declare member checks (`MemberChecks` collection must be
  empty). Members declared via a `Members(...)` lens on an alternative are fine —
  those live inside `LensBranches` and run through the normal branch evaluator.

**Deferred.** Formatted branch messages in the fusion output (currently lists
IDs only — requires an Id→MessageFormat lookup harvested from every
alternative's check tree); per-alternative member checks; `FocusShape<TFocus>`-
level `OneOf` for same-focus disjunction.

### B.6 `All` / `Any` / `None` quantifiers

**Status (April 2026). ✅ Shipped** (v1 subset).

**What.** Higher-order predicates applied across a lens's output set. Surfaces as a
`Quantifier quantifier = Quantifier.All` parameter on each lens entry point rather
than a trailing `.Any(...)` / `.None(...)` method, so the aggregation intent sits
next to the lens it governs.

```csharp
// All (default) — per-child violation at each offender's smudge anchor.
typeFocus.Members(kind: SymbolKind.Property, configure: m => m.MustBePublic());

// Any — silent if at least one child passes; one aggregate diagnostic at the
// parent's origin otherwise.
typeFocus.Members(
    kind: SymbolKind.Property,
    configure: m => m.MustBePublic(),
    quantifier: Quantifier.Any);

// None — silent if every child fails; per-child aggregate at each offender
// whenever one passes.
typeFocus.Members(
    kind: SymbolKind.Method,
    configure: m => m.MustHaveAttribute("System.ObsoleteAttribute"),
    quantifier: Quantifier.None);
```

`All` is the default; `Any` / `None` require a paired `quantifierSpec` override
(or fall back to the reserved SCRIBE09x IDs below). A child "passes" iff every
nested leaf predicate holds **and** every nested sub-branch produces zero
violations — sub-branch diagnostics are folded into the pass/fail decision and
suppressed from the outer diagnostic stream.

**Reserved IDs (shipped).**

| Lens           | `Any` aggregate | `None` aggregate |
| -------------- | --------------- | ---------------- |
| `Members`      | SCRIBE090       | SCRIBE091        |
| `Attributes`   | SCRIBE092       | SCRIBE093        |
| `BaseTypeChain`| SCRIBE094       | SCRIBE095        |

Authors override via `quantifierSpec: new DiagnosticSpec(Id, Title, Message, Severity)`.

**Depends on.** B.1, B.2, B.3.

**Deferred.** Fluent `.Any(...)` / `.None(...)` trailing-form sugar (would wrap the
existing parameter surface). Only worth surfacing if the param form proves
awkward in practice.

### B.7 Cross-focus predicates

**Status (April 2026). ✅ Shipped** (v1 — static helpers + focus extension methods). The public `FocusSymbols` class in `Scribe.Shapes` now exposes cross-focus comparison helpers that callers wire into `Satisfy` predicates or lens-configure callbacks:

| API | Compares |
| --- | --- |
| `FocusSymbols.SymbolEquals(ISymbol?, ISymbol?)` | Strict `SymbolEqualityComparer.Default` — closed-generic `List<int>` vs. `List<string>` are unequal. |
| `FocusSymbols.SameOriginalDefinition(ISymbol?, ISymbol?)` | Via `ISymbol.OriginalDefinition` — open/closed-generic instantiations of the same definition compare equal. |
| `this TypeFocus.SameOriginalDefinition(TypeFocus / TypeArgFocus)` | Cross-focus wrapper over the underlying symbols. |
| `this TypeArgFocus.SameOriginalDefinition(TypeFocus / TypeArgFocus)` | Mirror of the above. |

Both helpers return `false` when either side is null. Symbol references flow only through in-flight analyzer / generator passes — never into cached pipeline state — so this is cache-safe.

**Canonical use.** Event Contract cycle check: capture the outer `TypeFocus` via closure, then assert a navigated `TypeArgFocus` refers to the same underlying type.

```csharp
TypeFocus? outer = null;
Stencil.ExposeClass()
    .Attributes("AppliedByAttribute", configure: attr =>
        attr.GenericTypeArg(0, configure: arg =>
            arg.Satisfy(
                predicate: (TypeArgFocus focus) => outer is { } o && o.SameOriginalDefinition(focus),
                id: "EVT001", title: "...", message: "...")))
    .Etch(...)
```

**Deferred.** Full query-comprehension `SelectMany` multi-from form (tracked with B.9's deferred item). For v1, cross-focus assertions are expressed via closure capture inside nested lens-configure callbacks, which matches the Event Contract use case without requiring additional desugaring infrastructure.

### B.8 `DiagnosticInfo.FocusPath`

**Status (April 2026). ✅ Shipped** (v1 — option 1: structured `FocusPath` on `DiagnosticInfo`, rendered at materialisation time).

**What shipped.**

- `DiagnosticInfo` gained a 5th positional field `string? FocusPath = null` — cache-safe, included in equality, backward-compatible default.
- Every built-in lens entry (`Attributes(fqn)`, `Members(kind)`, `BaseTypeChain`) now carries a human-readable `HopDescription` that is composed onto the parent path with a `→` separator and stamped on every `DiagnosticInfo` the branch produces.
- At fire time, `DiagnosticInfo.Materialize` prefixes the rendered message with `"[path] "` when `FocusPath` is non-null. A shallow wrapped descriptor is built per diagnostic — accepted because firing is rare relative to equality checks.

**Examples.**

| Source | Rendered prefix |
| --- | --- |
| Top-level `MustBePartial()` violation | *(no prefix)* |
| `Attributes("System.ObsoleteAttribute", min: 1)` presence breach | `[Attributes("System.ObsoleteAttribute")] ...` |
| `Attributes(X).ConstructorArg<string>(0, min: 1)` presence breach | `[Attributes("X")] ...` (sub-lens hop appended when its own hop description is set) |

**Deferred.** Per-sub-lens hop descriptions (`ConstructorArg<T>(0)`, `GenericTypeArg(0)`, `NamedArg<T>("Foo")`) — left null in v1, so their violations still inherit the parent lens's path. Adding those is mechanical; defer until a concrete use case asks for the extra granularity.

### B.9 LINQ query-comprehension parity

**What.** Ensure the fluent API's method names match LINQ's `SelectMany` / `Where` / `Select` so `from / where / select` desugars cleanly.

**Status (April 2026). ✅ Shipped** (single-focus comprehension). `TypeShape` now exposes LINQ pass-throughs:

| Method | Desugars from | Delegates to |
| --- | --- | --- |
| `Select<TModel>(Func<TypeFocus, TModel>)` | `select` clause | `Etch<TModel>` — wraps the focus-shaped selector into an `EtchDelegate`. |
| `Where(Func<TypeFocus, bool>)` | `where` clause | `Check(...)` with a default `SCRIBE200` diagnostic id — the single-arg overload needed for comprehension desugaring. |
| `Where(Func<TypeFocus, bool>, DiagnosticSpec)` | — (fluent only) | `Check(...)` — use when you want a stable custom id. |

Both forms are now equivalent:

```csharp
// Query-comprehension form
Shape<Collected> q =
    from t in Stencil.ExposeClass().MustBePartial()
    where t.Fqn.Length > 0
    select new Collected(t.Fqn);

// Fluent form
Shape<Collected> f = Stencil.ExposeClass()
    .MustBePartial()
    .Where(t => t.Fqn.Length > 0)
    .Select(t => new Collected(t.Fqn));
```

**Deferred.** Multi-`from` (`SelectMany`) that joins two focus streams into a multi-variable comprehension (as drafted in [dsl.md](dsl.md#query-comprehension-form)). For v1, multi-focus composition is expressed via the lens-entry callbacks (`Attributes(...)`, `Members(...)`, `BaseTypeChain(...)`) which already accept a nested `FocusShape<TFocus>` configure callback.

**Reserved IDs.** `SCRIBE200` — default diagnostic id for single-arg `Where(predicate)` when no explicit spec is supplied.

---

### B.10 Conditional Shapes — `When(cond, sub)`

**What.** Apply a sub-Shape only when a compilation-wide condition holds. Gated predicates/lenses that switch on/off based on environmental facts (referenced assemblies, available types, target framework, etc.).

**Worked example — Marten-gated constraint.**

```csharp
Stencil.ExposeClass()
    .MustBePublic()
    .When(Env.References("Marten"),
          s => s.Check(
              t => SingleGettablePublicProperty(t),
              spec: DiagnosticSpec.Error("MARTEN001",
                  "Type must expose exactly one gettable public property when Marten is referenced.")))
    .Etch<ThingModel>(t => new ThingModel(t.Fqn));
```

When Marten is referenced → the `Check` fires. When Marten is absent → the sub-Shape is skipped entirely; the type passes with only `MustBePublic`.

**Condition catalogue (proposed).**

| `Env.References(assemblyName)` | Compilation references the named assembly. |
| `Env.HasType(fqn)` | Compilation can resolve a type by fully-qualified name. |
| `Env.HasSymbol<T>()` | Compilation can resolve a .NET type. |
| `Env.Target(tfm)` | Target framework matches. |
| `Env.Custom(Func<Compilation, bool>)` | Escape hatch. |

**Cache correctness.** Compilation-wide facts must flow as `IncrementalValueProvider<bool>` (singular, not `Values`) so each fact is computed once per compilation and combined with per-declaration providers via `.Combine(...)`. The authoring API hides this — `Env.References(...)` returns an opaque `EnvCondition` the pipeline composes correctly.

**Relation to existing primitives.** `OneOf` gives *unguarded alternatives*; `When` gives a *guarded branch*. Complementary, not overlapping.

**Metaphor fit.** The lithography "process option" — the same mask with conditional DRC rules depending on the target fab process. The rules aren't different masks; they're conditional applications of rules to one mask.

**Effort.** ~3–5 days: the `Env` condition type + `IncrementalValueProvider<bool>` plumbing + `.When(...)` on each focused Shape + tests + glossary entry.

**Status (April 2026). ⏳ Deferred** per maintainer direction. Post-v1 work.

---

## Suggested Sequence

Phased for parallelism where possible. Each phase leaves the build green.

### Phase 1 — Vocabulary alignment (Part A, 1 week)

- A.1 `Relation.Pair` → `Prism`
- A.5 `dsl.md` scrub
- A.2 Extract `TypeFocus`
- A.3 Make predicates focus-parametric
- A.6 `ToFixProvider` → `ToInk` (if time)

*Ship gate:* all existing consumers (Hermetic.Logos) pass tests. No behavioural change. Glossary / dsl / code all speak the same six words.

### Phase 2 — Navigation core (B.1–B.4, 2 weeks)

- B.1 `Lens<TSource, TTarget>`
- B.2 New focus types (start with AttributeFocus + TypeArgFocus; others can land incrementally)
- B.3 Built-in lenses (Attributes, GenericTypeArg, AsTypeShape first; then the rest)
- B.4 Focus-specific leaf predicates
- A.4 `ForEachMember` → `Members` lens (lands naturally here once B.1 exists)

*Ship gate:* the HKey worked example from [design-member-level-shapes.md](design-member-level-shapes.md) compiles and runs end-to-end. That example alone validates ~70% of Phase 2.

### Phase 3 — Composition primitives (B.5–B.7, 1 week)

- B.5 `Shape.OneOf`
- B.6 Quantifiers (`All` / `Any` / `None`)
- B.7 Cross-focus predicates

*Ship gate:* the Event Contract worked example compiles and runs — the cycle (`Command → Event → Aggregate → Event`) is the acid test for cross-focus equality + quantifiers.

### Phase 4 — Polish (B.8–B.9, 0.5 week)

- B.8 `FocusPath` (message-only variant)
- B.9 LINQ query-comprehension parity verified against worked examples

*Ship gate:* both fluent and query-comprehension forms of the Event Contract example produce identical trees; error messages render the focus breadcrumb.

**Total rough cost:** 4.5 weeks of focused work. Phases 2 and 3 are the ones that determine whether Scribe becomes the primitive the framework authoring community adopts. Phase 1 is cheap but unblocks everything.

---

## Validation — HierarchicalKey (the full stress test)

The analysis above is the abstract plan. Here it is stress-tested against the hardest real consumer we need to ship: Hermetic's **HierarchicalKey (HKey) family**. HKey is a small zoo of interlocking declarations — composed keys, leaf parts, union bases, union variants, discriminators, parametrised constructors — all validated today by ~400 lines of imperative Roslyn. If we can express it as a handful of related Shapes with every diagnostic auto-squiggling at the right span, the DSL has earned its keep. Every assumption in Parts A and B is on the hook.

The union-keys sub-case below is the keystone. It is also the canonical example of how Scribe lets you define a compile-time-safe discriminated union *today* — one that can drop-in-replace when native .NET 11 unions land, because the information captured at design time is a superset of what the native feature needs.

### The pattern

A union key is two structurally distinct declarations working together:

- **Base** — `abstract partial record MyUnion : IHierarchicalKey`. One per union. Carries the union's identity.
- **Variants** — `readonly partial record struct Alpha(...) : MyUnion`, `readonly partial record struct Beta(...) : MyUnion`, … N per union. Each variant has its own discriminator and parameter list.

The **final shape** we need to emit and validate is `Shape<UnionModel>` where `UnionModel` carries the base's metadata together with the ordered list of its variants. **One row per union — not per variant.**

### Two navigations, two primitives

Expressing this requires navigation in *both* directions, and they are **not the same kind of navigation**.

| Direction | Mechanism | Primitive |
| --- | --- | --- |
| Variant → Base | `BaseTypeChain().AsTypeShape()` — walk the base-type list to the declared parent. Structural: the edge is in the symbol graph. | **Lens** |
| Base → Variants | Scan all types in the compilation, match by "this type's base-chain contains our Fqn." No graph edge exists from `INamedTypeSymbol` to "my derived types." | **Prism** |

That this single example needs *both* confirms the vocabulary split: Lens (structural) and Prism (keyed) are genuinely different operations. One primitive wouldn't cover it.

### Mapping to the DSL

The authored surface — assuming Parts A and B have shipped — looks something like:

```csharp
// Shape 1 — the base of any union.
public static readonly Shape<UnionBaseModel> BaseShape =
    Stencil.ExposeAnyType()
        .Implementing(KnownFqns.IHierarchicalKey)
        .MustBeAbstract()
        .MustBeRecord()
        .MustBePartial()
        .WithAttribute(KnownFqns.UnionBase)
        .Etch<UnionBaseModel>(t => new UnionBaseModel(t.Fqn));

// Shape 2 — one variant of some union.
public static readonly Shape<UnionVariantModel> VariantShape =
    Stencil.ExposeAnyType()
        .MustBeRecordStruct()
        .MustBeReadOnly()
        .MustBePartial()
        .BaseTypeChain()
            .Any(t => t.AsTypeShape().Implementing(KnownFqns.IHierarchicalKey))
        .Attributes(KnownFqns.Discriminator, min: 1, max: 1)
            .ConstructorArg<string>(0)
                .MustNotBeEmpty()
        .Etch<UnionVariantModel>(v => new UnionVariantModel(v.Fqn, v.BaseFqn, v.Discriminator, v.Params));

// Composed — the union as a whole.
public static readonly Shape<UnionModel> UnionShape =
    Prism.By(
        left:  BaseShape.Select(b => (key: b.Fqn, b)),
        right: VariantShape.Select(v => (key: v.BaseFqn, v)),
        mode:  PrismMode.RequireLeftHasRight)
        .Aggregate<UnionModel>((b, variants) => new UnionModel(b, variants.ToImmutableArray()));
```

Three things are happening:

1. **Two independent Shapes** validate the base and the variants in isolation. Each ships useful diagnostics on its own.
2. A **Prism** joins them by Fqn ↔ BaseFqn. This is cross-type composition Roslyn does not provide structurally.
3. The Prism produces a **single composed `Shape<UnionModel>`** that downstream consumers (analyzer, generator, fixer) treat exactly like any other Shape.

### Gaps this case exposes

Six design-space gaps the current Part B plan understates. V.1–V.3 fall out of the union pattern specifically. V.4–V.6 fall out of HKey's broader type hierarchy — composed keys referencing parts, discriminators with cross-variant uniqueness, location routing through embedded Shapes.

#### Gap V.1 — Prism needs a fan-in terminal projection

**What.** Today's `Relation.Pair` (future Prism) emits pair-rows: one row per matched left/right combination. A base with three variants produces three pair-rows. The union case needs **fan-in**: one `UnionModel` per base with its variants collected.

**Target surface.** `PrismBuilder<TLeft, TRight>.Aggregate<TModel>(Func<TLeft, IEnumerable<TRight>, TModel>)` — groups right by key, applies the aggregator once per left.

**Why it matters.** Without fan-in, the author has to collect the pair-stream, group downstream, and re-wrap into an equatable `UnionModel` themselves. Incremental caching gets shaky across the manual grouping. It also reads badly — the DSL should express "union = base + its variants" in one line.

**Effort.** ~1 day on top of the A.1 rename. Equality on the grouped output needs care (`ImmutableArray<TRight>` with deterministic ordering).

**Where this slots in Part B.** Promote this to **B.10 — Prism fan-in projection**, depends on A.1.

#### Gap V.2 — Prism should produce a composable `Shape<TModel>`, not just a materialisation-side artefact

**What.** Today the `PairBuilder` has `.Matched` (a stream) and `.Diagnostics` — consumers feed these into `RegisterSourceOutput` or the analyzer context themselves. There is no `Shape<TModel>` coming out the other side. For the union case we want the `UnionShape` above to be a first-class Shape, so it can in turn be the left or right input to *another* Prism, or feed into `.ToAnalyzer()` / `.ToProvider(ctx)` / Ink like any other Shape.

**Target.** `Prism.By(...).Aggregate<TModel>(...)` returns `Shape<TModel>`. That Shape carries violations (orphan-left under `RequireLeftHasRight`, orphan-right under `WarnOnRightUnused`) plus the projected model. All three Materialisation consumers work on it without special-casing Prism output.

**Effort.** ~2 days. The violation-plumbing has to thread from the Prism machinery into the shared `ShapedSymbol<TModel>.Violations` array. Design decision: what is the "root" Focus of a Prism-produced Shape? Proposal: the left row's Focus, since the union is "owned by" the base. Materialising at the left span gives sensible diagnostics.

**Where this slots in Part B.** Promote this to **B.11 — Prism as Shape combinator**, depends on V.1.

#### Gap V.3 — `AsTypeShape` needs a `BaseTypeChainFocus` entry point

**What.** Part B.3 lists `AsTypeShape()` as a `TypeArgFocus → TypeFocus` lens. The union case needs the same thing from `BaseTypeChainFocus` elements — "take a step in the base chain and re-enter a type-level shape at that step." Same operation, different source.

**Target.** Either (a) make `AsTypeShape()` polymorphic across any focus that wraps an `INamedTypeSymbol`, or (b) ship separate `AsTypeShape()` extensions on each such focus type. Go with (a) — one method, one mental model.

**Effort.** Trivial (~1 hour) once B.2 foci exist. Worth calling out only so it doesn't get missed.

**Where this slots in Part B.** Fold into B.3 as a clarification.

#### Gap V.4 — Shape-as-predicate (sub-Shape references)

**What.** The HKey composed-key analyzer needs to say *"the generic type argument of `[ComposedOf<T>]` must itself be a valid KeyPart"* — which means re-applying `PartShape` at the navigated type. Without a primitive for this, `PartShape`'s logic gets re-inlined (or worse, duplicated) at every call site that wants to validate a target-is-a-part.

The user-facing promise — *"a couple of related shapes"* — only holds if Shapes compose by **reference**, not by copy-paste.

**Target surface.**

```csharp
typeFocus.Attributes(KnownFqns.ComposedOf, min: 1)
    .GenericTypeArg(0)
        .AsTypeShape()
        .MustSatisfy(PartShape);     // embed one Shape inside another as a predicate
```

`MustSatisfy(Shape<TModel> sub)` runs `sub` at the current focus. If `sub` fails, its violations bubble up — re-routed to the *caller's* span by default (see V.6). The sub-Shape's projection is ignored (this is a predicate use, not a composition use; for composition, use the Prism from V.1/V.2).

**Why it matters.** Without V.4, a type that should satisfy three separate Shapes (e.g. `KeyShape` + `PartShape` + `DiscShape`) can only be expressed by inlining all three predicate trees into one giant monolithic Shape. With V.4, each Shape is authored once, tested in isolation, and composed at call sites.

**Depends on.** A.2 (TypeFocus), B.1 (Lens), B.2 (foci), ideally V.6 (location re-routing).

**Effort.** ~2 days. The mechanics are straightforward (run sub-Shape, merge violations). The care is in the violation-routing semantics — see V.6.

**Where this slots in Part B.** New **B.12 — Shape-as-predicate (`MustSatisfy`)**, lands in Phase 3.

#### Gap V.5 — Aggregate constraints (the GROUP BY / HAVING leg of the algebra)

**What.** A whole family of constraints that assert a **collective property over a set of elements** — not per-element. In relational-algebra terms this is `GROUP BY` + `HAVING`: the third primitive leg alongside per-row filters (Predicates) and row-joins (Lenses/Prisms). Neither a per-focus predicate (*"this one must X"*) nor a quantifier (*"every / at least one / none must X"*) covers it. Quantifiers iterate per-element; aggregates consume the whole set.

The family is general. Canonical cases:

| Sub-family | Constraint | Example |
| --- | --- | --- |
| **Distinctness** | No two elements share a value | Discriminator strings unique among union variants |
| **Derived distinctness** | No two elements share a projected key | Parameter names unique within a part |
| **Cardinality** | Set size is `=N`, `≤N`, `≥N` | Exactly one `[Discriminator]` per union base |
| **Coverage** | Every element of some reference set is represented | Every declared event has at least one handler |
| **Ordering** | Elements satisfy an order property | `[Param]` indexes are sequential from 0 |
| **Agreement** | All elements share a common value | All variants of a union live in the same namespace |

Every framework-authoring codebase reimplements at least half of these by hand. The DSL today has no primitive for any of them. The design doc's fictional `MustBeUniqueWithinAttributeSet` is one case of the family; it deserves a general home.

**Target surface.** Aggregates attach to a lens's output set, same fluent position as Quantifiers (`All` / `Any` / `None`) but operating on the collection rather than per-element:

```csharp
typeFocus.Attributes(KnownFqns.Discriminator)
    .ConstructorArg<string>(0)
        .Unique();                                   // distinctness

typeFocus.Attributes(KnownFqns.Param)
    .UniqueBy(a => a.ConstructorArg<string>(0));     // derived distinctness

typeFocus.Attributes(KnownFqns.Primary)
    .CardinalityAtMost(1);                           // cardinality

typeFocus.BaseTypeChain()
    .AgreeOn(t => t.Namespace);                      // all elements share a value

UnionBaseShape.Select(b => b.Fqn)
    .CoveredBy(UnionVariantShape.Select(v => v.BaseFqn));   // coverage across streams
```

**Why it matters.** This isn't a niche pattern — it's one of the three general constraint families the DSL has to carry if it wants to claim "relational algebra over declarations." Without aggregates, *every* consumer ends up writing the same five-to-ten-line group-by-then-scan code for uniqueness, cardinality, and coverage. That imperative sprawl is exactly what we're eliminating.

**Design care required.**

- **Diagnostic fusion.** When three variants share a discriminator, who gets squiggled? Proposal: squiggle *every duplicate after the first*, referencing the first in the message (*"discriminator 'X' already used by `Alpha` at line 42"*). Mirrors how the C# compiler reports duplicate type members.
- **Per-group vs whole-set.** Uniqueness within a single type's attributes (per-group) vs uniqueness across all types in the compilation (whole-set) are different operations. Both are needed; the surface has to make the scope explicit. Proposal: aggregates default to per-group scoped by the enclosing Shape; use `.Across(stream)` or similar to widen scope when needed.
- **Escape hatch.** Built-ins cover the canonical cases above. Long-tail aggregates get `.SatisfyingSet(customPredicate, diagnosticSpec)` at the same fluent position — same diagnostic-dispatch path, same verb family.

**Depends on.** B.3 (lenses), B.4 (leaf predicates on the right foci).

**Effort.** ~4 days (up from 3 now that the family is properly scoped). Six-ish concrete primitives (`Unique`, `UniqueBy`, `CardinalityAtMost`, `CardinalityExactly`, `AgreeOn`, `CoveredBy`) plus the `SatisfyingSet` escape hatch, each with its own diagnostic-fusion rules.

**Glossary follow-up.** Aggregate deserves a Supporting Vocabulary entry in [glossary.md](glossary.md) alongside Quantifier. Add when V.5 lands, with the sub-family table above.

**Where this slots in Part B.** New **B.13 — Aggregate constraints**, lands in Phase 3 alongside V.4.

#### Gap V.6 — Location routing through embedded Shapes

**What.** V.4 raises a question the current plan doesn't address: when `typeFocus.GenericTypeArg(0).AsTypeShape().MustSatisfy(PartShape)` fails because `PartShape` says *"this type must be a readonly partial record struct"* — **where does the squiggle go?**

Two possible semantics:

| Mode | Squiggle lands at | When to use |
| --- | --- | --- |
| **At-target** | The failing part's declaration (`readonly partial record struct Foo { … }`) | The target is authored by the same hand — the user can fix it there. |
| **At-call-site** | The `[ComposedOf<Foo>]` generic argument that brought us here | The target is authored by someone else (another assembly, library code, a type you didn't write) — the user's only actionable fix is the reference. |

Both are useful. The right default is **at-call-site**, because that's where the user's own source lives for attribute-argument navigations. At-target is the override for cases where the navigation and declaration are co-owned.

**Target surface.** `MustSatisfy(sub, LocationMode mode = LocationMode.AtCallSite)`.

**Why it matters.** This *is* the "diagnostics automatically appear in the right places" promise. If V.4 ships without V.6, every embedded Shape fires its diagnostics at the target's declaration, and authors lose the auto-routing magic precisely at the most composable layer of the DSL.

**Depends on.** V.4, and the location-propagation function already in Lens (B.1).

**Effort.** ~1 day on top of V.4. The mechanism exists in Lens; V.6 just wires it into `MustSatisfy`.

**Where this slots in Part B.** Fold into **B.12** (part of the same primitive).

### What the HKey case confirms

Beyond the gaps, the walkthrough confirms four design choices are right:

1. **Two navigation primitives, not one.** Lens (structural) and Prism (keyed) are complementary and both necessary. The union case uses both in a single composition.
2. **Prism belongs in the Shape DSL, not off to the side.** The current placement as a post-materialisation combinator was correct for cross-assembly orphan checks but too narrow. Elevating it to a Shape combinator unifies the surface.
3. **Aggregating projection is the common case.** Most real uses of Prism are "one unit composed of N parts" (union + variants, command + handlers, event + subscribers). Fan-in is the default; raw pair-streams are the escape hatch for power users.
4. **Shapes compose by reference, not by inlining.** The six-word vocabulary names Shape as self-composing, and `MustSatisfy(otherShape)` is the primitive that actually delivers on that. Without it, "a couple of related shapes" collapses back into one giant monolithic Shape.

### Plan deltas

Adding to Part B:

- **B.10 — Prism fan-in projection (`Aggregate<TModel>(...)`)** — ~1 day, depends on A.1.
- **B.11 — Prism as Shape combinator (returns `Shape<TModel>`)** — ~2 days, depends on B.10.
- **B.12 — Shape-as-predicate (`MustSatisfy`) with configurable location routing** — ~3 days, depends on B.1 + B.2. Includes V.6 (location mode).
- **B.13 — Set-level aggregations (`Unique`, `UniqueBy`, `CardinalityAtMost`, `CardinalityExactly`)** — ~3 days, depends on B.3 + B.4.
- B.3 — note that `AsTypeShape()` applies uniformly to any focus wrapping an `INamedTypeSymbol`, not just `TypeArgFocus`.

Sequence implication: B.10–B.13 all belong in **Phase 3** (composition primitives), landing alongside `OneOf` and quantifiers. They share the same design concern (multi-source Shape composition + cross-element assertions) and the same ship gate: the full HKey analyzer — with KeyShape, PartShape, UnionBaseShape, UnionVariantShape, and DiscShape each authored independently and composed via `MustSatisfy`, Prism, and set predicates — compiles and runs, and every diagnostic lands at the right span without the author doing any location bookkeeping.

**Revised total cost:** ~6 weeks (up from 4.5). Phase 3 grows from 1 week to ~2.5 weeks to absorb B.10–B.13.

### The .NET 11 unions angle

The `UnionModel` produced above captures strictly more information than native .NET 11 discriminated unions will expose: not just the variant type list, but every variant's discriminator string, parameter list, and the base's own attribute metadata. When .NET 11 unions ship, the *generator* that consumes `UnionShape` can switch its emit strategy from "hand-rolled visitor + pattern-match helpers" to "native union syntax" without touching the Shape.

The Shape is the contract; the generator is the implementation. That's the point of keeping the declaration-query stage pure — it makes the emission side freely replaceable.

---

## Out of Scope

Explicitly not in this gap analysis:

- **Operation / expression-level rules.** The DSL describes declaration shape, not method body behaviour. Belongs in a separate operation-walking layer if ever.
- **Navigated-focus fix catalogue.** `FixKind.Custom` is enough for v1 bespoke fixes on attribute-argument / type-arg violations. A built-in catalogue (e.g. `RemoveAttribute` targeting a navigated `AttributeFocus`) is a later concern.
- **Cross-compilation / MSBuild integration changes.** The engine is stable; no plumbing changes required for Parts A and B.
