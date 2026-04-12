# Design — Member-Level Shape Rules (the "WORD1005 problem")

> Draft. Captures the design space for extending the Shape DSL from type-level-only
> checks to rules that iterate declared members and emit per-member diagnostics,
> paired with fixers that operate on the specific member syntax.

---

## Why

The Shape DSL today is strictly type-level:

- `ShapeCheck.Predicate(INamedTypeSymbol, Compilation, CancellationToken) -> bool` — one verdict per type.
- `SquiggleAt` locates on the type (identifier / keyword / attribute / full decl).
- `MessageArgs(INamedTypeSymbol) -> EquatableArray<string>` — one message per type.
- `IShapeFix.FixAsync` receives the `TypeDeclarationSyntax`, not a specific member.

That's sufficient for the seventeen current `FixKind`s — all of them modify the
type declaration itself (modifiers, name, base list, attributes, containing
namespace). But a recurring real-world rule shape does not fit:

> "Types matching this shape must not contain any declared member satisfying
> predicate P. Report one diagnostic per offending member, squiggled at that
> member, with the member name in the message. Each violation can be fixed
> independently."

The canonical instance in the wild is Hermetic's **WORD1005** — `IIdentifier`
implementations must not declare instance properties or fields beyond `Value`.
The existing fixer ([IdentifierExtraMembersFix.cs][extra-fixer]) rewrites each
offender into a static extension method on a generated `implicit extension`
type. More will follow (e.g. Hermetic's planned "aggregate roots must not
expose public mutable state" rule).

Without a DSL path, these rules fall back to hand-written
`DiagnosticAnalyzer` + `CodeFixProvider` pairs, which is exactly the friction
Shape was built to eliminate.

[extra-fixer]: ../../Hermetic/Hermetic.Logos.Fixes/Essence/IdentifierExtraMembersFix.cs

---

## Problem statement

A single Shape must be able to declare a **member-level check** that:

1. Iterates over declared members of the matched type (filterable by symbol kind,
   accessibility, static-ness, name, attribute, etc.).
2. Emits **zero or more** `DiagnosticInfo` per type — one per matching member.
3. Squiggles at the **member** location, not the type.
4. Carries **per-member** message arguments (typically the member name).
5. Is paired with a fixer that receives the offending `MemberDeclarationSyntax`
   (or at minimum the member's `ISymbol`) so it can rewrite that specific node
   — not the whole type.

All of the above while preserving Shape's invariants:

- Incremental-generator-safe equality on projection models.
- Fluent, declarative call-site ergonomics (no raw Roslyn in user code for the
  common case).
- One `Shape<TModel>` → one analyzer + one fix provider (no parallel class
  hierarchies for "member rules").

---

## Design axes

### 1. DSL surface — how the user declares the rule

Three candidate shapes, in order of increasing flexibility:

**A. Sugar primitives** — bake specific member patterns into dedicated builders.

```csharp
Shape.RecordStruct()
    .MustImplement("Hermetic.IIdentifier")
    .MustNotDeclareInstancePropertiesOrFieldsExcept("Value",
        spec: new DiagnosticSpec("WORD1005", ...));
```

Pros: zero generality cost, totally declarative, easy to read.
Cons: every new use case demands a new primitive; won't scale past ~3 rules
before the builder surface becomes a zoo.

**B. General `ForEachMember` iterator** — one primitive covering the whole space.

```csharp
Shape.RecordStruct()
    .MustImplement("Hermetic.IIdentifier")
    .ForEachMember(
        match: m => m is IPropertySymbol or IFieldSymbol
                 && !m.IsStatic
                 && m.Name != "Value",
        report: new MemberDiagnosticSpec(
            id: "WORD1005",
            severity: DiagnosticSeverity.Error,
            messageFormat: "Record struct '{0}' has extra instance member '{1}'",
            messageArgs: (type, member) => [type.Name, member.Name],
            squiggleAt: MemberSquiggleAt.Identifier,
            fixKind: FixKind.CustomMemberFix));
```

Pros: one primitive, composes (`ForEachMember(...).ForEachMember(...)`), easy
to grow with new selectors. Cons: the user writes a predicate lambda per rule.
That's fine — the existing type-level primitives also accept predicates
internally, they just wrap common patterns.

**C. Both** — sugar primitives are implemented on top of `ForEachMember`.

This is almost certainly where we land. `MustNotDeclareField`,
`MustNotDeclareMutableProperty`, etc. collapse to `ForEachMember` calls. The
question is which sugar is worth adding up front; answer: none, until a
second WORD1005-shaped rule appears.

**Recommendation:** ship **B** first. Sugar is additive and non-breaking.

### 2. Check execution — from `bool` to `IEnumerable<Violation>`

Today `ShapeCheck` is:

```csharp
internal sealed record ShapeCheck(
    string Id, ..., Func<INamedTypeSymbol, Compilation, CancellationToken, bool> Predicate,
    Func<INamedTypeSymbol, EquatableArray<string>> MessageArgs, ...);
```

Two refactors on the table:

**A. Two check kinds** — keep `ShapeCheck` (type-level), add `MemberCheck`:

```csharp
internal abstract record CheckBase;

internal sealed record TypeCheck(
    string Id, ...,
    Func<INamedTypeSymbol, Compilation, CancellationToken, bool> Predicate,
    Func<INamedTypeSymbol, EquatableArray<string>> MessageArgs) : CheckBase;

internal sealed record MemberCheck(
    string Id, ...,
    Func<ISymbol, bool> MemberMatch,          // runs per declared member
    Func<INamedTypeSymbol, ISymbol, EquatableArray<string>> MessageArgs,
    MemberSquiggleAt SquiggleAt) : CheckBase;
```

`Shape<T>.RunChecks` dispatches on check kind. Clean, open for more kinds
(e.g. a future "cross-symbol relation" check).

**B. Unified `IEnumerable<Violation>` return** — one primitive:

```csharp
internal sealed record ShapeCheck(
    string Id, ...,
    Func<INamedTypeSymbol, Compilation, CancellationToken, IEnumerable<ViolationSite>> Evaluate,
    ...);
```

`ViolationSite` carries the squiggle location + per-violation message args.
A type-level check yields zero or one site; a member-level check yields N.

Pros: truly one primitive; good orthogonality.
Cons: every existing type-level predicate grows a yielding wrapper. Allocation
overhead per check call — today the hot path is `bool`, no allocation.

**Recommendation:** **A**. The two-check-kinds refactor preserves the
zero-alloc hot path for type-level rules (which will always be the majority)
and cleanly encapsulates the iteration + multi-emit logic in one place.

### 3. Squiggle location — where the red underline appears

Current `SquiggleAt`: `Identifier | Keyword | Attribute | FullDeclaration`.
All resolve relative to the `TypeDeclarationSyntax`.

Member-level rules need an analogous enum resolved against the
`MemberDeclarationSyntax`:

```csharp
public enum MemberSquiggleAt
{
    Identifier,            // property/method/field identifier token
    FullDeclaration,       // the whole member node
    TypeAnnotation,        // the return/field type syntax
    FirstAttribute,        // first attribute list
}
```

`SquiggleLocator` (core) gets a parallel `MemberSquiggleLocator` that knows how
to extract these from the concrete member syntax kinds
(`PropertyDeclarationSyntax`, `FieldDeclarationSyntax`,
`MethodDeclarationSyntax`, `EventDeclarationSyntax`, ...).

The type-level and member-level enums are distinct on purpose — mixing them
hides category errors.

### 4. Member discovery — symbol-based, not syntax-based

The check should iterate `INamedTypeSymbol.GetMembers()` rather than the
syntax tree directly. Reasons:

- Works across `partial` declarations without per-declaration deduplication.
- Gives the match predicate full symbol semantics (`IsStatic`,
  `IsImplicitlyDeclared`, `DeclaredAccessibility`, `AttributeData`).
- Matches how the existing type-level predicates already consume symbols.

Locations are derived from `ISymbol.DeclaringSyntaxReferences` → resolve each
reference to a `MemberDeclarationSyntax` at squiggle time.

Implicit / compiler-generated members (record primary constructor parameters,
value-equality backing, synthesized property accessors) must be filtered —
emitting on them would be noise. Filter before handing to the user predicate:
`IsImplicitlyDeclared == false` and syntax reference count > 0.

### 5. Fixer interface — giving the fix access to the member

`IShapeFix.FixAsync` today:

```csharp
Task<Solution> FixAsync(
    Document document, TypeDeclarationSyntax typeDecl,
    Diagnostic diagnostic, CancellationToken ct);
```

A member-level fix needs the member node. Options:

**A. Second interface** — `IMemberShapeFix : FixAsync(..., MemberDeclarationSyntax, ...)`.
The dispatcher (`ShapeCodeFixProvider`) picks based on `FixKind` category.

**B. One interface, diagnostic carries location** — keep `IShapeFix`; the fix
calls `root.FindNode(diagnostic.Location.SourceSpan)` to get the member itself.
This is already how the generic resolver locates the type.

**C. Split the base** — `IShapeFix` provides the diagnostic + document, and
two narrow helper delegates (`ResolveType` / `ResolveMember`) hang off it as
extension methods.

**Recommendation:** **B** with a helper. The existing dispatcher already does
`root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()`.
A parallel `AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault()`
is the member equivalent — no second interface needed. The `IShapeFix`
contract stays one interface; individual fix implementations resolve the node
at whatever granularity they need.

### 6. Custom-fix escape hatch — for WORD1005 itself

Even with member-level checks in place, the specific WORD1005 fix
(rewrite instance member → static extension method on a generated
`implicit extension` type) is too bespoke to live as a `FixKind` primitive.

Two paths:

**A. `FixKind.Custom` + delegate-backed fix** — Shape DSL allows attaching a
user delegate at declaration time:

```csharp
.ForEachMember(
    match: ...,
    report: new MemberDiagnosticSpec(id: "WORD1005", ...),
    fix: (document, memberNode, diagnostic, ct) => ConvertToExtensionAsync(...));
```

The delegate serialises into a `CustomFix` registry keyed off the diagnostic
ID. `ShapeCodeFixProvider` looks up the delegate instead of resolving via
`FixResolver`.

**B. Hand-written sidecar fixer** — Shape emits the analyzer with
`fixKind=None`; a conventional `CodeFixProvider` in the consumer project
handles those IDs. This is the status quo for the existing WORD1005 fixer.

**Recommendation:** **A**, long-term. **B** is acceptable in the interim —
it's what's shipping now, and it doesn't block the member-level analyzer
work. The analyzer-side and the delegate-fix surface can ship in separate
phases.

### 7. Projection stability — `ShapedSymbol<TModel>.Violations`

Today `Violations` is `EquatableArray<DiagnosticInfo>`. `DiagnosticInfo`
carries `Id, Severity, MessageArgs, Location`. This already supports one
diagnostic per check → N violations per type trivially (the collection grows).

No schema change needed. The incremental cache's equality over
`EquatableArray<DiagnosticInfo>` just needs `DiagnosticInfo`'s location +
args to be stable — which they are, since `LocationInfo` already uses
file-path + span integers.

The only subtlety: the ordering of emitted violations must be deterministic
across compiler invocations, or `EquatableArray.Equals` will churn. Iterate
members in source order (or stable-sort by syntax span) before evaluating.

---

## Open questions

1. **Multi-target member check** — should a single `ForEachMember` ever emit
   different IDs based on the member (e.g. "fields are error, properties are
   warning")? Or do we require the user to call `ForEachMember` twice with
   narrower predicates? The second is cleaner and avoids a per-violation ID
   field.

2. **Attribute-driven exclusion** — should there be first-class support for
   `[AllowExtraMember]`-style opt-outs on the offending member? Probably a
   helper on top of the match predicate:
   `member.GetAttributes().Any(a => a.AttributeClass?.Name == "AllowExtraMemberAttribute")`.
   Not a DSL primitive until a second use case appears.

3. **Fix-all semantics** — the existing `ShapeFixAllProvider` iterates
   documents + groups by type. Member-level fixes can emit multiple
   diagnostics per type whose fixes may edit overlapping spans (removing
   multiple properties from one record). The per-type annotation strategy
   still works, but the inner loop must re-locate the *member* after each
   edit, not just the type. Needs a thinking pass — probably per-member
   annotations, same pattern, one level deeper.

4. **Is the right abstraction "member" or "declaration"?** Some future rules
   may want to iterate over e.g. base-list entries, type parameters, or
   constraint clauses. `ForEachMember` is narrow. A more general
   `ForEach<TSyntax>(selector, match, report)` is tempting but probably
   overkill until we have two distinct use sites.

---

## Phased rollout

| Phase | Scope |
| ----- | ----- |
| **11a** | `ForEachMember` primitive + `MemberCheck` + `MemberSquiggleAt` + member-resolving dispatch. Enables member-level *diagnostics only*. WORD1005's existing hand-written fixer stays in place. |
| **11b** | `FixKind.Custom` + delegate-backed fix registration. Enables migrating `IdentifierExtraMembersFix` into the Shape surface. |
| **11c** | Sugar primitives on top of `ForEachMember` if a second real use case appears. Otherwise skip. |

Phase 11a is independently valuable: Hermetic's analyzer migration can drop
the hand-written `IdentifierWord` entirely, and WORD1005 continues to work
with its existing sidecar fixer during the transition.

---

## Non-goals

- **No** support for cross-type rules ("type A must be referenced by type B").
  That's a relation-level concern, solved via `ShapeBuilder`'s `Relation`
  projections and out of scope here.
- **No** support for statement-level or expression-level rules. Those belong
  in operation-walking analyzers, not Shape.
- **No** attempt to express the `IdentifierExtraMembersFix` rewrite as a
  declarative Shape fix. It's legitimately custom Roslyn work; the DSL's
  contribution is the delegate escape hatch, not a new transform grammar.
