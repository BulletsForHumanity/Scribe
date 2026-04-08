# How Quill Works

Internal architecture of the Quill source builder. Read this if you're contributing to Scribe or want to understand what happens under the hood.

For the public API, see [Quill Feature Reference](quill-reference.md).

---

## Source Organization

Quill is a `sealed partial class` split across five files:

| File | Responsibility |
| ------ | --------------- |
| `Quill.cs` | Fields, constants, `FileNamespace()`, core content methods (`Line`, `Lines`, `LinesFor`, `AppendRaw`, `Comment`), `Inscribe()` |
| `Quill.Usings.cs` | `Using()`, `Usings()`, `Alias()`, alias name generation, `ResolveGlobalReferences()`, `DisambiguateAliases()` |
| `Quill.XmlDoc.cs` | Standalone XML doc methods (`Summary`, `Remarks`, `Param`, etc.), `Attribute()`, `BuildXmlDocString()` |
| `Quill.Scoping.cs` | `Block`, `Scope`, `SwitchExpr`, `Case`, `Region`, `Indent`, `ListInit`, and all nested scope structs |
| `Quill.Helpers.cs` | `EnsureBlankLineSeparation`, `NeedsBlankLineSeparationAt`, `DedentRawString`, `EmitDedentedLines`, `TrimTrailingBlankLines` |

---

## Internal State

```csharp
internal readonly StringBuilder _body;           // The accumulated source body
private readonly HashSet<string> _usings;         // Deduplicated using directives
private readonly Dictionary<string, string> _aliases;      // alias -> "Ns.Type"
private readonly Dictionary<string, string> _aliasLookup;  // "Ns.Type" -> alias
private int _indent;                              // Current indentation level (0-based)
private string? _namespace;                       // File-scoped namespace
private bool _built;                              // Prevents double Inscribe()
```

All content methods append to `_body`. The `_usings` set is populated both explicitly (via `Using()`) and implicitly (via `global::` resolution at `Inscribe()` time). Indentation is tracked as a level count; each level emits 4 spaces.

---

## Type Reference Resolution

The most complex part of Quill. Runs as a post-processing step during `Inscribe()`.

### Algorithm

1. **Scan** the body for all `global::Namespace.Type` occurrences using string search.
2. **Extract** the namespace and type name from each reference.
3. **Detect method calls:** If `(` follows immediately and the reference isn't preceded by `new` or `typeof(`, peel back the last segment (it's a method, not a type).
4. **Group** by short type name to detect conflicts.
5. **Resolve:**
   - **No conflict** (one type per short name): Add `using Namespace;`, replace `global::Namespace.Type` with `Type`.
   - **Conflict** (multiple types with same short name): Generate disambiguated aliases by walking up namespace segments until unique. E.g. `Foo.Bar.Widget` and `Baz.Qux.Widget` become `BarWidget` and `QuxWidget`.
6. **Apply** replacements (longest match first to prevent partial replacements).

### Alias Disambiguation

When two types share a short name, the algorithm walks backwards through namespace segments with increasing depth until all aliases are unique:

- Depth 1: `Bar` + `Widget` = `BarWidget`, `Qux` + `Widget` = `QuxWidget` — unique, done.
- If still conflicting at depth 1, try depth 2: `FooBar` + `Widget`, `BazQux` + `Widget`.
- Fallback: full namespace collapsed (e.g. `FooBazWidget`).

### Explicit Aliases

`Alias(ns, typeName)` pre-registers an alias. The auto-generated name follows the same namespace-segment algorithm. `Alias(ns, typeName, aliasName)` uses the explicit name directly.

All aliases are added as `using` directives (`using AliasName = Ns.Type;`) before `ResolveGlobalReferences()` runs, so they appear in the sorted output.

---

## Indentation

Indentation is a simple integer counter (`_indent`). Each content-emitting method prepends `_indent * 4` spaces:

```csharp
_body.Append(' ', _indent * 4).Append(text).Append(NewLine);
```

Scope types (`BlockScope`, `IndentScope`, etc.) increment `_indent` on creation and decrement on `Dispose()`.

---

## Raw String Dedentation

`Lines(string multiLineText)` and the multi-line `Block(string header)` overload use `DedentRawString()`:

1. Split on `\n`
2. Trim leading/trailing blank lines
3. Find minimum leading whitespace across all non-empty lines
4. Strip that many leading spaces from each line

The result is then re-indented to the current Quill level by `EmitDedentedLines()`.

---

## Block Scoping and XML Doc Insertion

`BlockScope` is the most complex scope type. It supports **post-decoration**: XML docs and attributes are inserted *before* the block header, not appended.

### How Post-Decoration Works

1. When `Block(header)` is called, it records `_headerPos` (the StringBuilder position before the header).
2. XML doc methods (`.Summary()`, `.Param()`, etc.) build the doc string and insert it at `_headerPos + _insertLen`.
3. `_insertLen` tracks total inserted bytes so subsequent insertions land in the right position.
4. On first insertion, a blank-line separator may be added before the docs (if the previous content needs it).

This design lets you write the fluent chain naturally:

```csharp
using (q.Block("public void Foo()").Summary("Does foo.").Param("x", "The arg."))
```

The summary and param are inserted before `public void Foo()` even though they're called after.

### Block Closing

On `Dispose()`, `BlockScope` calls `TrimTrailingBlankLines()` to remove any trailing blank lines inside the block, then decrements indent and emits `}`.

---

## Blank Line Management

Quill automatically manages blank-line separation in two contexts:

### `EnsureBlankLineSeparation()`

Called by `Comment()`. Checks the last line in the body — if it's neither empty, an opening `{`, nor a comment, inserts a blank line. This prevents comments from sticking to the previous member.

### `NeedsBlankLineSeparationAt(int pos)`

Used by `ContentResult.Padded()` and `BlockScope` post-decoration. Same logic but at an arbitrary position rather than the end of the body.

### `TrimTrailingBlankLines()`

Called before closing any block scope (`BlockScope`, `SwitchExprScope`). Removes consecutive trailing newlines so that the closing `}` sits right after the last content line.

---

## Newline Handling

All newlines are hardcoded `\n` (LF). This is enforced via `Quill.NewLine` (char) and `Quill.NewLineString` (string). `StringBuilder.AppendLine()` is never used because it emits `Environment.NewLine` which is platform-dependent and will likely be flagged by RS1035 in future Roslyn SDK versions.

---

## Inscribe Output Format

The final output structure produced by `Inscribe()`:

```csharp
// <auto-generated/>
//  (Scribe banner)
#nullable enable

using Namespace1;
using Namespace2;
using Alias = Namespace3.Type;

namespace MyApp.Generated;

// body content...
```

The banner is always emitted. Usings are sorted ordinally. The namespace is emitted as a file-scoped declaration. `#nullable enable` is always present.
