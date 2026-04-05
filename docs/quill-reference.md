# Quill Feature Reference

Complete API reference for the `Quill` fluent source builder. Quill manages indentation, using directives, namespace declarations, XML documentation, attributes, and `global::` type reference resolution.

For usage patterns and architecture guidance, see [Writing Generators with Quill](writing-generators.md).

---

## Lifecycle

```csharp
var q = new Quill();          // 1. Create
q.FileNamespace("MyApp");     // 2. Configure
q.Using("System.Linq");       //    (usings can be added at any point)
// ... build content ...       // 3. Build
string source = q.Inscribe(); // 4. Finalize (one-shot)
```

`Inscribe()` can only be called once. It resolves all `global::` references, prepends the file header, sorted usings, and namespace, then returns the complete source string.

---

## Namespace

```csharp
q.FileNamespace("MyApp.Generated");
```

Sets the file-scoped namespace (`namespace MyApp.Generated;`). Call at most once.

---

## Using Directives

### `Using(string ns)`

Add a single using directive. Duplicates are ignored. Returns `Quill` for fluent chaining.

```csharp
q.Using("System.Linq");
q.Using("System.Collections.Generic");
```

### `Usings(params string[] namespaces)`

Add multiple using directives at once.

```csharp
q.Usings("System", "System.Linq", "System.Collections.Generic");
```

### Automatic `global::` Resolution

Any `global::Namespace.Type` reference in the body content is automatically resolved at `Inscribe()` time:

- If the short name is unique, Quill adds `using Namespace;` and replaces `global::Namespace.Type` with `Type`.
- If two types share the same short name, Quill creates disambiguated `using` aliases (e.g. `using FooWidget = Foo.Widget;`).

```csharp
q.Line("var x = new global::System.ArgumentException(\"bad\");");
// After Inscribe():  var x = new ArgumentException("bad");
// Added:             using System;
```

**Static member access:** `global::` resolution treats the last dotted segment as the type name. For patterns like `StringComparer.Ordinal`, use `Using()` + short names instead:

```csharp
q.Using("System");
q.Line("var cmp = StringComparer.Ordinal;");
```

### `Alias(string ns, string typeName)`

Register a `using` alias with an auto-generated name. Returns the alias name for use in interpolated strings.

```csharp
var fw = q.Alias("Foo.Bar", "Widget");   // returns "BarWidget"
var bw = q.Alias("Baz.Qux", "Widget");   // returns "QuxWidget"
q.Line($"{fw} a = default;");
q.Line($"{bw} b = default;");
// Inscribe emits:
// using BarWidget = Foo.Bar.Widget;
// using QuxWidget = Baz.Qux.Widget;
```

### `Alias(string ns, string typeName, string aliasName)`

Register a `using` alias with an explicit name.

```csharp
var w = q.Alias("Foo.Bar", "Widget", "FooWidget");
q.Line($"{w} x = default;");
// Inscribe emits:  using FooWidget = Foo.Bar.Widget;
```

---

## Content Methods

### `Line(string text = "")`

Append a single line at the current indentation level. Empty string emits a blank line.

```csharp
q.Line("public int X { get; }");
q.Line();  // blank line
q.Line("public int Y { get; }");
```

### `Lines(params string[] lines)`

Append multiple individual lines.

```csharp
q.Lines(
    "public int X { get; }",
    "",
    "public int Y { get; }"
);
```

### `Lines(string multiLineText)` -> `ContentResult`

Append a multi-line string (typically a raw string literal). The text is dedented — common leading whitespace is stripped and the builder's current indentation is applied. Leading/trailing blank lines are trimmed.

```csharp
q.Lines("""
    if (value == default)
        throw new ArgumentException("bad");
    return new Widget(value);
    """);
```

Returns a `ContentResult` that supports `.Padded()` (see below).

### `LinesFor<T>(IEnumerable<T> items, Func<T, string> selector)` -> `ContentResult`

Emit one line per item from a collection.

```csharp
q.LinesFor(fields, f => $"public {f.Type} {f.Name} {{ get; }}")
 .Padded();
```

### `AppendRaw(string text)`

Append pre-formatted text verbatim with no indentation applied. Use for content that already has correct formatting.

```csharp
q.AppendRaw("#if NET9_0_OR_GREATER" + Quill.NewLineString);
```

### `Comment(string text)`

Emit a `// comment` at the current indentation. Automatically inserts a blank line before the comment (unless at the start of a block or already separated).

```csharp
q.Comment("Factory methods");
q.Line("public static Widget Create(int value) => new(value);");
```

### `ContentResult.Padded()`

Wrap just-emitted content with blank lines before and after. Available on `Lines(string)`, `LinesFor<T>()`, and `ListInit<T>()`.

```csharp
q.LinesFor(items, i => $"options.Add({i});").Padded();
```

---

## Scoping

All scope types implement `IDisposable` — use `using` statements to auto-close them.

### `Block(string header)` -> `BlockScope`

Opens a block: writes the header line and `{`, increases indent. On dispose, trims trailing blank lines inside the block and writes `}`.

The header can be a single line or a multi-line raw string literal.

```csharp
using (q.Block("public partial record struct Amount"))
{
    q.Line("public decimal Value { get; }");
}
```

`BlockScope` supports post-decoration with XML docs and attributes — see [XML Documentation](#xml-documentation-blockscope-post-decoration) and [Attributes](#attributes).

### `Scope()` -> `BlockScope`

Opens a bare `{` ... `}` scope with no header line. Useful for variable scoping or grouping.

```csharp
using (q.Scope())
{
    q.Line("var temp = Calculate();");
}
```

### `SwitchExpr(string header)` -> `SwitchExprScope`

Opens a switch expression block. On dispose, writes `};` (with semicolon).

```csharp
using (q.SwitchExpr("var result = kind switch"))
{
    q.Line("0 => \"zero\",");
    q.Line("_ => \"other\",");
}
```

### `Case(string expression)` -> `CaseScope`

Opens a `case` block. On dispose, emits `break;`, closes the block, and adds a trailing blank line.

```csharp
using (q.Case("\"widget\""))
{
    q.Line("return new Widget();");
}
```

### `Region(string name)` -> `RegionScope`

Opens `#region name`. On dispose, emits `#endregion`.

```csharp
using (q.Region("Properties"))
{
    q.Line("public int X { get; }");
}
```

### `Indent()` -> `IndentScope`

Increase indentation without braces. On dispose, restores the previous level.

```csharp
q.Line("if (true)");
using (q.Indent())
{
    q.Line("DoSomething();");
}
```

---

## Shortcuts

### `ListInit<T>(string target, string type, IEnumerable<T> items, Func<T, string> selector)` -> `ContentResult`

Emit a collection initializer with braces, trailing commas, and proper indentation.

```csharp
q.ListInit("var names", "new List<string>", people, p => $"\"{p.Name}\"");
```

Output:

```csharp
var names = new List<string>
{
    "Alice",
    "Bob",
};
```

---

## XML Documentation (Standalone)

These methods emit XML doc comments at the current indentation level. Use them for one-liner members that don't use `Block()`.

### `Summary(string text)`

```csharp
q.Summary("The underlying value.");
q.Line("public decimal Value { get; }");
```

Outputs:

```csharp
/// <summary>The underlying value.</summary>
public decimal Value { get; }
```

Multi-line text is automatically wrapped:

```csharp
q.Summary("""
    A longer description that
    spans multiple lines.
    """);
```

### `Remarks(string text)`

```csharp
q.Remarks("This is computed on the fly.");
```

### `Param(string name, string text)`

```csharp
q.Param("value", "The underlying decimal.");
```

### `TypeParam(string name, string description)`

```csharp
q.TypeParam("T", "The element type.");
```

### `Returns(string description)`

```csharp
q.Returns("The parsed value.");
```

### `InheritDoc(string? cref = null)`

```csharp
q.InheritDoc();                    // /// <inheritdoc />
q.InheritDoc("IWidget.Create");    // /// <inheritdoc cref="IWidget.Create" />
```

### `Example(string text)`

```csharp
q.Example("var w = Widget.Create(42);");
```

### `Exception(string cref, string description)`

```csharp
q.Exception("ArgumentException", "Thrown when value is invalid.");
```

### `SeeAlso(string cref)`

```csharp
q.SeeAlso("Widget.Parse");
```

---

## XML Documentation (BlockScope Post-Decoration)

`BlockScope` supports the same XML doc methods as above, but inserts them *before* the block header. This lets you write the header first and decorate it after:

```csharp
using (q.Block("public static Amount Parse(string s, IFormatProvider? provider)")
        .Summary("Parses a string into an Amount.")
        .Param("s", "The string to parse.")
        .Param("provider", "The format provider.")
        .Returns("The parsed Amount.")
        .Exception("FormatException", "Invalid input."))
{
    q.Line("return new Amount(decimal.Parse(s, provider));");
}
```

Available methods on `BlockScope`: `Summary`, `Remarks`, `Param`, `TypeParam`, `Returns`, `InheritDoc`, `Example`, `Exception`, `SeeAlso`, `Attribute`.

---

## Attributes

### Standalone

```csharp
q.Attribute("Obsolete", "\"Use Bar instead.\"");
q.Attribute("MethodImpl", "MethodImplOptions.AggressiveInlining");
q.Line("public void Foo() { }");
```

### On BlockScope (Post-Decoration)

Inserted before the header, after any XML docs:

```csharp
using (q.Block("public void Foo()")
        .Summary("Does foo.")
        .Attribute("Obsolete", "\"Use Bar instead.\"")
        .Attribute("MethodImpl", "MethodImplOptions.AggressiveInlining"))
{
    q.Line("// body");
}
```

---

## Constants

### `Quill.NewLine` (char)

Deterministic newline constant: `'\n'` (LF). Use instead of `Environment.NewLine` in all generator code.

### `Quill.NewLineString` (string)

String version: `"\n"`. Use when a `string` is needed (e.g. string concatenation for `AppendRaw`).

**Why LF-only?** `Environment.NewLine` is banned by RS1035 in analyzer assemblies and produces platform-dependent output. `StringBuilder.AppendLine()` also uses it internally. Hardcoded LF ensures deterministic builds — the C# compiler accepts both `\n` and `\r\n`.

---

## Header

### `Header(string? header)` -> `Quill`

Override the auto-discovered header (from `[assembly: ScribeHeader]`). Pass `null` to clear.

```csharp
q.Header("My Generator");
```

Normally you don't call this — place `[assembly: ScribeHeader("...")]` on your generator assembly and Quill discovers it automatically via `Assembly.GetCallingAssembly()`.

---

## Inscribe

```csharp
string source = q.Inscribe();
```

Finalizes and returns the complete source text. Can only be called once. Performs these steps:

1. Registers all `Alias()` entries as `using` directives
2. Scans the body for `global::` references, resolves conflicts, and shortens type names
3. Prepends the `// <auto-generated/>` marker
4. Renders the page header (if `ScribeHeaderAttribute` is present) or a minimal Scribe attribution
5. Emits `#nullable enable`
6. Emits sorted, deduplicated `using` directives
7. Emits the file-scoped namespace
8. Appends the body content
9. Appends `// I HAVE SPOKEN` footer

### ScribeHeaderAttribute

Assembly-level attribute that provides the header text for generated files:

```csharp
// In the generator project's Scribe.cs:
[assembly: ScribeHeader("My Generator")]
```

Multi-line headers are supported — use `\n` or string concatenation:

```csharp
[assembly: ScribeHeader(
    "My Source Generator\n" +
    "https://github.com/example/my-framework")]
```

When present, `Inscribe()` renders a decorative page with the header text, a Scribe attribution section, and a quill illustration. When absent, Scribe injects a minimal header by itself.
