# Design — Quill Using Analyzer

> Draft. Specifies a new author-side analyzer that warns when a generator
> emits a `global::Ns.Type` reference into a Quill builder without registering
> `Ns` via `q.Using(...)` somewhere reachable on the same Quill instance.

---

## Status

| Concern | State |
| --- | --- |
| Quill API surface for usings + global:: resolution | Mapped (see [Quill Reference](quill-reference.md)) |
| Existing analyzer patterns in `Scribe.Ink/Shapes/` | Surveyed |
| Real-world Quill flow shapes across consumer generators | Surveyed |
| Diagnostic ID range allocation | Proposed: `SCRIBE3xx` (Quill correctness family) |
| Implementation | Not started |

---

## Goal

When a Quill consumer writes:

```csharp
var q = new Quill();
q.Line("var list = new global::System.Collections.Generic.List<string>();");
return q.Inscribe();
```

Quill silently leaves `global::System.Collections.Generic.List` in the output
because no `Using("System.Collections.Generic")` was ever registered.
`ResolveGlobalReferences()` shortens *only* prefixes that match a registered
namespace; unregistered ones survive into the emitted file as ugly fully-
qualified references.

The author rarely *wants* this. It's almost always an oversight. The
analyzer should catch it at edit time.

**Diagnostic SCRIBE300** — *"global:: reference is not covered by a registered using; Quill will not shorten it"*.

---

## Why this is non-trivial — the data flow problem

The naive design — flag any `global::` literal in a Quill content call when
no `q.Using(...)` precedes it in the same method — is wrong. Real generators
split Quill work across many methods. A snapshot of one survey case:

```csharp
// EndpointGenerator.Render(EndpointModel model)
var q = new Quill().FileNamespace(generatedNamespace);
q.Usings("System", "System.Threading", "System.Threading.Tasks");

EmitEndpointSharedHelpers(q);              // mutates q
EmitRawStringDescription(q, model.Desc);   // mutates q
EmitEndpointBody(q, model, …);             // mutates q (emits global::System.X)

return q.Inscribe();
```

The `EmitEndpointBody` helper emits `global::System.Threading.Tasks.Task` in
its body. A method-local check would fire SCRIBE300 there because `Using` is
not visible in `EmitEndpointBody`'s syntactic scope. False positive — the
namespace **is** registered, just on the caller's frame.

This is the dominant pattern. ~60% of surveyed Quill usage threads a single
instance through a chain of helper methods on the same class.

---

## Survey of Quill flow shapes

From a sweep of consumer generators (Scribe internals, Hermetic.Logos, etc.):

| Shape | Prevalence | Description |
| --- | --- | --- |
| **(a) Local in single method** | ~20% | `var q = new Quill(); … q.Inscribe();` all in one method |
| **(b) Field/property on generator class** | ~1% | `private readonly Quill _q = …;` shared across instance methods |
| **(c) Parameter threaded through helpers on same type** | ~60% | Outer method creates `q`, passes to `static void EmitX(Quill q, …)` siblings |
| **(d) Renderer class with Quill-returning method** | ~15% | `internal sealed class Renderer { public string Render() { var q = new Quill(); … return q.Inscribe(); } }` |
| **(e) Quill stored in collections / captured in escaping delegates / passed as generic `T`** | <5% | The hard cases |

**Key observation:** in shapes (a)–(d), the Quill instance never escapes a
single containing type. The creation site, the `Using` calls, and the
content emissions are all syntactic descendants of one `INamedTypeSymbol`.

This collapses the analysis from "interprocedural points-to over the whole
compilation" to "gather facts within one type, then check."

---

## Approach — type-scoped analysis

**Scope of analysis:** one `INamedTypeSymbol` at a time.

**For each type T in the compilation:**

1. **Discover Quill creation.** Walk every method body in T, looking for
   `new Quill()` (or any expression of type `Quill` that originates inside
   T). If T has zero local Quill creation sites, **skip T entirely** — Quill
   came in from outside; we can't reason about its registered usings.

2. **Collect registered namespaces.** Walk every method body in T. For each
   invocation of `Quill.Using(string)` or `Quill.Usings(params string[])`
   where the receiver is statically of type `Quill`, extract the literal
   namespace string(s). Aggregate into `RegisteredUsings : ISet<string>`.

3. **Collect emitted globals.** Walk every method body in T. For each
   invocation of a Quill content method (`Line`, `Lines`, `LinesFor`,
   `AppendRaw`, …), extract every `global::Ns.X[.Y…]` literal pattern from
   string-literal and interpolated-string arguments. Record
   `(emittedNamespace, location)` pairs.

4. **Match.** For each `(ns, loc)` in emitted globals, apply Quill's own
   matching rule (longest namespace prefix wins, alias entries skipped). If
   no registered namespace matches, emit `SCRIBE300` at `loc`.

That's the entire algorithm.

### Why type-scoping is enough

| Shape | Handled by type-scoping? |
| --- | --- |
| (a) Local in single method | Yes — single method ⊂ type |
| (b) Field on generator class | Yes — all access ⊂ type |
| (c) Parameter through same-type helpers | Yes — all helpers ⊂ type |
| (d) Renderer class | Yes — Quill is local to the renderer's `Render()` method, all helpers ⊂ renderer type |
| (e) Cross-type / escaping flow | Skipped (no creation site in T → no warning) |

The only residual false-negative class is shape (e), which is rare and
where the user has already opted into something we can't statically track.
No false positives.

---

## Why not interprocedural?

We considered building a points-to / union-find analysis over the whole
compilation, treating Quill flow points (locals, parameters, fields,
returns) as nodes and callsite parameter mappings as edges. That would
correctly handle shape (e) and any future exotic flow.

The cost:

- **New shared compilation state.** Existing Scribe analyzers are per-symbol
  or per-operation; none aggregate across methods. A new pattern.
- **Alias analysis is genuinely subtle.** Edge cases around generics,
  delegates, collection storage, lambdas-that-escape — each is a paragraph.
- **Test surface explodes.** Every flow shape needs its own test class.

The benefit, given the survey: catching <5% of cases that are rare and
typically intentional ("I'm building a Quill-composing helper library").

**Recommendation:** ship type-scoped as v1. Promote to interprocedural only
if we observe real-world false negatives that motivate the cost. v2 sketch
in [Future direction](#future-direction-v2-interprocedural) below.

---

## Quill API surface for the analyzer

Three categories of methods drive the analyzer.

**Registration** — collect literal namespace args into `RegisteredUsings`:

| Method | Source | Args |
| --- | --- | --- |
| `Quill.Using(string)` | `Quill.Usings.cs:10` | `ns` |
| `Quill.Usings(params string[])` | `Quill.Usings.cs:17` | each element |

`Quill.Alias(...)` is **not** a registration. `ResolveGlobalReferences`
skips alias-formatted entries (`"X = Y"`) at `Quill.Usings.cs:120`; the
analyzer mirrors that filter.

**Content emitters on `Quill`** — scan literal-string args for `global::`
patterns:

| Method | Source | Args to scan |
| --- | --- | --- |
| `Line(string)` | `Quill.cs:107` | `text` |
| `Lines(params string[])` | `Quill.cs:118` | each |
| `Lines(string)` (multi-line raw) | `Quill.cs:145` | `multiLineText` |
| `AppendRaw(string)` | `Quill.cs:182` | `text` |
| `Comment(string)` | `Quill.cs:192` | `text` |
| `Header(string?)` | `Quill.cs:58` | `header` |
| `Block(string header)` | `Quill.Scoping.cs:44` | `header` |
| `SwitchExpr(string header)` | `Quill.Scoping.cs:83` | `header` |
| `Case(string expression)` | `Quill.Scoping.cs:99` | `expression` |
| `Region(string name)` | `Quill.Scoping.cs:111` | `name` |
| `ListInit<T>(target, type, items, selector)` | `Quill.Scoping.cs:14` | `target`, `type` (selector skipped — see below) |
| `Summary(string)` | `Quill.XmlDoc.cs:15` | `text` |
| `Remarks(string)` | `Quill.XmlDoc.cs:24` | `text` |
| `Param(string, string)` | `Quill.XmlDoc.cs:33` | `text` |
| `Returns(string)` | `Quill.XmlDoc.cs:77` | `description` |

`FileNamespace(string)` is registration-adjacent (sets the file's emitted
namespace) and is intentionally not scanned — `global::` doesn't appear
in `namespace X;` declarations.

**Content emitters on `BlockScope`** — scan as well; `BlockScope` is
returned from `Block(string)` and is the most likely site for
`global::` references inside `Attribute(...)` arguments:

| Method | Source | Args to scan |
| --- | --- | --- |
| `Summary(string)` | `Quill.Scoping.cs:189` | `text` |
| `Remarks(string)` | `Quill.Scoping.cs:196` | `text` |
| `Param(string, string)` | `Quill.Scoping.cs:203` | `description` |
| `TypeParam(string, string)` | `Quill.Scoping.cs:211` | `description` |
| `Returns(string)` | `Quill.Scoping.cs:221` | `description` |
| `InheritDoc(string?)` | `Quill.Scoping.cs:229` | `cref` |
| `Example(string)` | `Quill.Scoping.cs:240` | `text` |
| `Exception(string, string)` | `Quill.Scoping.cs:247` | `cref`, `description` |
| `SeeAlso(string)` | `Quill.Scoping.cs:257` | `cref` |
| `Attribute(string, string?)` | `Quill.Scoping.cs:265` | `name`, `args` |

**Skipped delegate-arg methods (v1):**

- `Quill.LinesFor<T>(items, Func<T, string> selector)` — selector returns
  dynamic strings; v2 could walk the lambda body
- `Quill.ListInit<T>(... selector)` — same

The survey shows these rare enough not to motivate the cost.

---

## Algorithm in detail

### Phase 1 — Discovery (compilation start)

```csharp
context.RegisterCompilationStartAction(start =>
{
    var quillType = start.Compilation.GetTypeByMetadataName("Scribe.Quill");
    if (quillType is null) return; // Scribe not referenced

    var usingMethods = quillType.GetMembers("Using").OfType<IMethodSymbol>()
        .Concat(quillType.GetMembers("Usings").OfType<IMethodSymbol>())
        .ToImmutableHashSet(SymbolEqualityComparer.Default);

    var contentMethods = ImmutableHashSet.Create<ISymbol>(
        SymbolEqualityComparer.Default,
        quillType.GetMembers("Line").Concat(
        quillType.GetMembers("Lines")).Concat(
        quillType.GetMembers("LinesFor")).Concat(
        quillType.GetMembers("AppendRaw")).ToArray());

    start.RegisterSymbolStartAction(symbolStart =>
    {
        if (symbolStart.Symbol is not INamedTypeSymbol type) return;
        var collector = new TypeQuillFacts(type);

        symbolStart.RegisterOperationAction(op =>
            collector.OnInvocation(op, quillType, usingMethods, contentMethods),
            OperationKind.Invocation);

        symbolStart.RegisterOperationAction(op =>
            collector.OnObjectCreation(op, quillType),
            OperationKind.ObjectCreation);

        symbolStart.RegisterSymbolEndAction(end =>
            collector.Report(end));
    }, SymbolKind.NamedType);
});
```

`TypeQuillFacts` is the per-type accumulator: registered usings, emitted
globals, creation-site count.

### Phase 2 — Per-operation collection

For each `IInvocationOperation` whose receiver is a `Quill`:

```
if invocation.TargetMethod ∈ usingMethods:
    for each literal-string argument:
        registeredUsings.Add(literal)
elif invocation.TargetMethod ∈ contentMethods:
    for each string-literal or interpolated-string argument:
        for each global::Ns.X[.Y…] match in literal text portions:
            emittedGlobals.Add((Ns, sourceLocation))
```

For each `IObjectCreationOperation` of type `Quill`:

```
hasLocalCreation = true
```

**Interpolated strings:** walk `IInterpolatedStringOperation` children;
collect `IInterpolatedStringTextOperation` text portions only. Skip
`IInterpolationOperation` (the `{expr}` parts) — those are dynamic and we
don't analyze them.

**Literal extraction regex:** `global::([A-Za-z_][\w.]*)\.[A-Za-z_]\w*`
captures the namespace part. The trailing dot-separated segment is treated
as the type name (or first member; subsequent dots may be member access —
unimportant for the prefix-match check, since Quill's own resolution
strips the namespace prefix and leaves whatever follows alone).

### Phase 3 — Match (symbol end)

```
if not hasLocalCreation: return  // skip — Quill came from outside

// Apply Quill's own matching: sort registered usings longest-first
sorted = registeredUsings.OrderByDescending(s => s.Count('.'))
                         .ThenByDescending(s => s.Length)
                         .ToList()

for (ns, loc) in emittedGlobals:
    matched = sorted.Any(reg => ns == reg || ns.StartsWith(reg + "."))
    if not matched:
        report Diagnostic SCRIBE300 at loc, args: ns
```

The match rule mirrors `Quill.ResolveGlobalReferences` exactly — same
sorting, same prefix semantics. This guarantees:

- No false positives from a partial-match registered namespace that *would*
  match in real resolution
- No false negatives from a longer registered prefix that would shadow a
  shorter one

---

## Diagnostic specification

| Field | Value |
| --- | --- |
| ID | `SCRIBE300` |
| Title | `global:: reference will not be shortened by Quill` |
| Message | `'global::{0}' is not covered by any registered Using on this Quill instance. Quill will emit it verbatim. Add q.Using("{0}") to the builder.` |
| Category | `Scribe.Quill.Correctness` |
| Severity | `Info` |
| Default | `Enabled` |
| Help link | `https://github.com/BulletsForHumanity/Scribe/blob/master/docs/design-quill-using-analyzer.md` (until docs land) |

`{0}` is the **namespace portion** of the matched literal, not the full
type. The author can paste it directly into a `Using(...)` call.

### Message variants

When the literal is `global::System` (no further segments), the namespace
*is* `System`, suggesting `Using("System")`.

When the literal contains generic syntax like
`global::System.Collections.Generic.List<global::Foo.Bar.Baz>`, both the
outer (`System.Collections.Generic`) and inner (`Foo.Bar`) namespaces are
checked independently; each missing one fires its own diagnostic.

---

## Code fixer (SCRIBE300.Fix)

A code fix provider in `Scribe.Ink` (peer to existing `ShapeCodeFixProvider`)
offers a single fix: **"Add Using to Quill builder."**

The fixer's job is to insert `q.Using("Foo.Bar")` (or
`q.Usings("Foo.Bar", …)`) at a sensible site. Heuristic:

1. If the type has an existing `Using` / `Usings` invocation on a Quill
   instance, append the new namespace as a sibling call.
2. Otherwise, insert immediately after the first `new Quill()` expression
   in the type, on the next statement.

If the fixer can't find a clean insertion site (creation in a separate
method from the violation, etc.), offer no fix — the warning still fires;
the author resolves manually.

The fix-all provider can batch multiple SCRIBE300 hits in the same type
into a single `q.Usings("A", "B", "C")` call when the existing site has
no preceding `Using` call.

---

## Limits & escape hatches

| Case | Behavior |
| --- | --- |
| Type has no `new Quill()` anywhere | Skip type (no diagnostics) |
| Quill stored in field of unanalyzable type (collection, dictionary) | Skip type — flow escapes |
| Quill captured by lambda that escapes (assigned to a returned delegate, stored as field) | Best-effort — flag if we can't statically resolve, otherwise skip. v1: skip on any captured Quill in a non-immediately-invoked lambda. |
| `q.Using` argument is not a string literal (e.g. computed) | Treat as "registers an unknown namespace" — pollute the type's registered set with a sentinel that matches everything. (Conservative: silence rather than false-positive.) |
| `q.Line` argument is fully dynamic (no literal text portion) | Cannot extract globals — no diagnostic. |
| Generated code (auto-generated marker) | Skip per `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` |

The "computed Using argument" case deserves a callout — it's the v1
soundness escape valve. If anyone ever calls `q.Using(SomeString())` we
can't see what was registered; we silence rather than risk false positives.

---

## New patterns introduced

The existing analyzers in `Scribe.Ink/Shapes/` are **per-symbol** or
**per-operation** with no cross-method state. SCRIBE300 introduces:

### Pattern A — Per-type fact accumulator

`SymbolStartAction` on `NamedType` + per-method `RegisterOperationAction` +
`SymbolEndAction` to drain. Not exotic, but new to Scribe.

```csharp
context.RegisterSymbolStartAction(symbolStart =>
{
    var facts = new TypeFacts();
    symbolStart.RegisterOperationAction(op => facts.Observe(op), …);
    symbolStart.RegisterSymbolEndAction(end => facts.Report(end));
}, SymbolKind.NamedType);
```

### Recommendation: do **not** generalize prematurely

It's tempting to extract `TypeQuillFacts` into a generic
`IPerTypeAccumulator<TFacts>` utility. **Don't, yet.** We have one consumer.
If a second analyzer (say, a future Quill alias-collision check) needs the
same shape, generalize then.

The existing `Shape<T>.ToAnalyzer()` factory in
`Shape_T.ToAnalyzer.cs` already does substantial work to avoid hand-rolled
`DiagnosticAnalyzer` boilerplate, but its model — predicate-over-symbol — is
a bad fit for cross-method aggregation. SCRIBE300 sits outside the Shape
DSL; that's fine.

---

## File layout

| File | Purpose |
| --- | --- |
| `Scribe.Ink/Quill/QuillUsingsAnalyzer.cs` | The `DiagnosticAnalyzer`, including `TypeQuillFacts` as a private nested type |
| `Scribe.Ink/Quill/QuillUsingsCodeFixProvider.cs` | The code fix |
| `Scribe.Tests/Quill/QuillUsingsAnalyzerTests.cs` | Test cases (table below) |
| (no new descriptor file) | Descriptor lives as a static field on the analyzer, like `CacheCorrectnessAnalyzer.Rule` |

A new top-level folder `Scribe.Ink/Quill/` is reasonable — `Shapes/` is for
the Shape DSL, but Quill correctness is a separate concern. Alternative:
keep it under `Shapes/` if we want one folder for "all author-side rules";
that's a stylistic call.

---

## Testing strategy

Tests follow the existing pattern in `CacheCorrectnessAnalyzerTests.cs` —
inline source, run analyzer, assert diagnostic count and ID.

| Test case | Expected |
| --- | --- |
| Single method, registered using, matching global | 0 diagnostics |
| Single method, no using, unregistered global | 1 × SCRIBE300 |
| Helper method on same type, using on caller, global on callee | 0 diagnostics (type-scoping) |
| Helper method on **different** type, no creation in that type | 0 diagnostics (skipped) |
| Renderer class with `Render()` method, using and global both inside | 0 |
| Renderer class with using missing | 1 × SCRIBE300 |
| `Usings("A", "B", "C")` covers `global::A.X`, `global::B.Y` | 0 |
| `Usings(...)` with computed arg | All globals silenced (sentinel) |
| Interpolated string with `global::` in text portion | 1 × SCRIBE300 |
| Interpolated string with `global::` only inside `{expr}` | 0 (dynamic; not analyzed) |
| Two registered namespaces with prefix overlap (`System` vs `System.Text`) | Longer wins, no diagnostic |
| `global::` literal that's actually inside a quoted string within the emitted code (e.g. `q.Line("var x = \"global::foo\"")`) | False-positive risk — accepted in v1; document it |
| Quill stored in field, used across methods | 0 (registered usings collected from all methods) |
| Quill received as parameter, type has no `new Quill()` | Type skipped |

The "string-within-string" false positive is worth flagging — the analyzer
doesn't parse the C# inside Quill literals, just regex-matches `global::`.
A literal `q.Line("Console.WriteLine(\"global::Foo.Bar\");")` would
trigger SCRIBE300 even though the `global::` is inside a runtime string.
Acceptable cost in v1; if it bites, add a heuristic that ignores `global::`
preceded by `\"`.

---

## Documentation sync

Per [Documentation Sync Checklist](../.claude/CLAUDE.md#documentation-sync-checklist):

- [ ] **README.md** — add SCRIBE300 to any analyzer summary table
- [ ] **`docs/quill-reference.md`** — add a "Tooling" section noting the analyzer; explain that registered usings drive `global::` shortening
- [ ] **`docs/writing-generators.md`** — when introducing `q.Using(...)`, mention SCRIBE300 catches mismatches
- [ ] **This file's `.claude/CLAUDE.md` terminology table** — add `QuillUsingsAnalyzer` row

---

## Future direction (v2: interprocedural)

If shape (e) usage grows or a real false negative materializes, promote
to a points-to analysis:

1. **Nodes:** flow points = `(symbol, kind)` where kind ∈ {local, parameter, field, return-of-method}.
2. **Edges:** assignment, parameter passing at call sites, field access on shared receiver.
3. **Algorithm:** union-find over flow points, rooted at `new Quill()`
   creation sites. Each component aggregates registered usings and emitted
   globals. Match component-wise.
4. **Pollution:** mark a component "tainted" if any flow point passes
   through generics, delegates that escape, collection storage, or
   non-source-available method boundaries. Tainted components produce no
   diagnostics.

This is real work — probably 1–2 weeks of focused implementation plus
proportional test coverage. Not v1.

---

## Resolved decisions

The four open questions in earlier drafts are now closed:

1. **Severity:** `Info`. Quieter default; promote later if false-positive
   feedback pushes back.
2. **Content method scope:** all string-accepting content methods,
   including the scope/block/body family. Full enumeration in
   [Quill API surface for the analyzer](#quill-api-surface-for-the-analyzer)
   below.
3. **`Alias(...)` interaction:** alias entries skipped when collecting
   registered usings, mirroring `ResolveGlobalReferences`'s own filter at
   `Quill.Usings.cs:120`.
4. **Sibling SCRIBE301 (dead-using detection):** deferred. The same
   `TypeQuillFacts` accumulator could power it, but out of scope for v1.

---

## Summary

| | |
| --- | --- |
| **What** | Author-side analyzer that catches unregistered `global::` references in Quill content |
| **How** | Per-type fact accumulation (registered usings + emitted globals), no cross-method dataflow needed for v1 |
| **Why this works** | ~95% of Quill usage is single-type-scoped per real-world survey |
| **Why not interprocedural** | 5% gain at 10× implementation cost; revisit if real cases emerge |
| **New pattern** | `SymbolStartAction(NamedType)` + per-operation collection + `SymbolEndAction` drain — modest, not generalized in v1 |
| **Diagnostic** | `SCRIBE300` Info, with code fix that inserts `q.Using(...)` |
