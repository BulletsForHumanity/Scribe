# Writing Generators with Quill

This guide covers how to write a Roslyn incremental source generator using Scribe's Quill builder and the recommended **Transform -> Register -> Render** architecture.

For project setup and .csproj configuration, see [Project Setup & Infrastructure](project-setup.md).
For the complete Quill API, see [Quill Feature Reference](quill-reference.md).

---

## The Three-Phase Pattern

Scribe encourages splitting every generator into three clean phases:

1. **Transform** — Extract an equatable record ("target") from the Roslyn semantic model
2. **Register** — Wire the syntax provider, transform, and output in `Initialize()`
3. **Render** — Use Quill to produce the final source string

This separation keeps the generator thin, the transform testable, and the renderer easy to iterate on.

---

## Phase 1: Transform

The transform extracts everything the renderer needs from Roslyn's semantic model into a plain record. The record must be equatable so Roslyn's incremental pipeline can cache unchanged types.

```csharp
internal sealed record WidgetTarget(
    string Name,
    string Namespace,
    string UnderlyingFqn,
    bool EmitCreateFactory,
    bool EmitParsable
);

private static WidgetTarget? TransformWidget(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    var symbol = ctx.SemanticModel.GetDeclaredSymbol((RecordDeclarationSyntax)ctx.Node, ct);
    if (symbol is null || !symbol.AllInterfaces.Any(i => i.Name == "IWidget"))
        return null;

    var underlying = symbol.AllInterfaces
        .First(i => i.Name == "IWidget")
        .TypeArguments[0];

    var hasUserCreate = symbol.GetMembers("Create")
        .OfType<IMethodSymbol>()
        .Any(m => m.IsStatic);

    return new WidgetTarget(
        symbol.Name,
        symbol.ContainingNamespace.ToDisplayString(),
        underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        EmitCreateFactory: !hasUserCreate,
        EmitParsable: !symbol.AllInterfaces.Any(i => i.Name == "IParsable")
    );
}
```

**Key principles:**

- Capture **emit flags** that encode what the user already defined — the renderer checks these to avoid generating duplicate members.
- Use `SymbolDisplayFormat.FullyQualifiedFormat` for type names — they come out as `global::Namespace.Type`, which Quill resolves automatically.
- Return `null` for non-matching nodes — the pipeline's `.Where(t => t is not null)` filter handles it.

---

## Phase 2: Register

Wire everything together in the generator's `Initialize()` method. Use `SyntaxPredicates` for the fast syntactic filter:

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class WidgetGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var widgets = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => SyntaxPredicates.IsPartialRecord(node),
                static (ctx, ct) => TransformWidget(ctx, ct))
            .Where(static t => t is not null);

        context.RegisterSourceOutput(
            widgets.Collect(),
            static (spc, targets) =>
            {
                foreach (var t in targets)
                {
                    if (t is not null)
                        spc.AddSource($"{t.Name}.g.cs", new WidgetRenderer(t).Render());
                }
            });
    }
}
```

**Tips:**

- The syntactic predicate (`IsPartialRecord`) runs on every keystroke — keep it fast and allocation-free.
- The semantic transform runs only when the syntactic filter matches — it's safe to do more work here.
- `.Collect()` batches all targets for a single source-output callback. Use this when you need to see all targets at once (e.g. for a registration class). For per-type output, you can also register directly on the unbatched pipeline.

---

## Phase 3: Render

The renderer is a plain class that takes the target and uses Quill to produce source:

```csharp
internal sealed class WidgetRenderer(WidgetTarget target)
{
    public string Render()
    {
        var q = new Quill();
        q.FileNamespace(target.Namespace);

        var interfaces = new List<string>();
        if (target.EmitParsable)
            interfaces.Add($"global::System.IParsable<{target.Name}>");

        var declaration = interfaces.Count > 0
            ? $"public partial record {target.Name} : {string.Join(", ", interfaces)}"
            : $"public partial record {target.Name}";

        using (q.Block(declaration))
        {
            if (target.EmitCreateFactory)
                EmitCreate(q);

            if (target.EmitParsable)
                EmitParsable(q);
        }

        return q.Inscribe();
    }

    private void EmitCreate(Quill q)
    {
        using (q.Block($"public static {target.Name} Create({target.UnderlyingFqn} value)")
                .Summary($"Creates a new {target.Name}."))
        {
            q.Lines($$"""
                if (value == default)
                    throw new global::System.ArgumentException("bad", nameof(value));
                return new {{target.Name}}(value);
                """);
        }
    }

    private void EmitParsable(Quill q)
    {
        using (q.Block($"public static {target.Name} Parse(string s, global::System.IFormatProvider? provider)")
                .Summary("Parses a string into a widget value."))
        {
            q.Line($"return Create({target.UnderlyingFqn}.Parse(s, provider));");
        }
    }
}
```

**Key principles:**

- Each concern gets its own `private void Emit*(Quill q)` method.
- Use `global::` FQNs in string content — Quill's `Inscribe()` auto-resolves them to short names with `using` directives.
- The renderer is easy to test: construct a target, call `Render()`, assert on the returned string.

---

## Using Quill Effectively

### Type References

Write `global::Namespace.Type` directly in your strings. At `Inscribe()` time, Quill scans the body, adds `using` directives, and shortens the references:

```csharp
q.Line($"throw new global::System.ArgumentException(\"bad\");");
// Output: throw new ArgumentException("bad");
// Added:  using System;
```

When two types share the same short name, Quill creates disambiguated aliases:

```csharp
q.Line($"global::Foo.Widget a = default;");
q.Line($"global::Bar.Widget b = default;");
// Output:
// using FooWidget = Foo.Widget;
// using BarWidget = Bar.Widget;
// FooWidget a = default;
// BarWidget b = default;
```

For static member access (`StringComparer.Ordinal`), use `Using()` + short names instead — `global::` resolution treats the last dotted segment as the type name:

```csharp
q.Using("System");
q.Line("var cmp = StringComparer.Ordinal;");
```

### XML Documentation

**Post-decoration** on blocks (inserted before the header):

```csharp
using (q.Block("public static Amount Parse(string s)")
        .Summary("Parses a string.")
        .Param("s", "The input string.")
        .Returns("The parsed amount.")
        .Exception("FormatException", "Invalid input."))
{
    q.Line("// ...");
}
```

**Standalone** for one-liner members:

```csharp
q.Summary("The underlying value.");
q.Line("public decimal Value { get; }");
```

### Multi-line Raw Strings

Use `Lines("""...""")` to emit dedented multi-line content. The raw string is stripped of common indentation and re-indented to the current Quill level:

```csharp
using (q.Block("public static Widget Parse(string s, IFormatProvider? provider)"))
{
    q.Lines("""
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("Empty input.");
        return new Widget(int.Parse(s, provider));
        """);
}
```

### Collection Iteration

Use `LinesFor()` to emit one line per item:

```csharp
q.LinesFor(fields, f => $"options.Converters.Add(new {f.TypeName}JsonConverter());")
 .Padded();
```

Use `ListInit()` for collection initializers:

```csharp
q.ListInit("var items", "new List<string>", names, n => $"\"{n}\"");
```

---

## Newlines

Scribe uses hardcoded `\n` (LF) for all output. This is deliberate:

- `Environment.NewLine` is banned by RS1035 in analyzer assemblies and produces platform-dependent output.
- `StringBuilder.AppendLine()` silently uses `Environment.NewLine`.
- The C# compiler accepts both `\n` and `\r\n`.

When building strings outside Quill (e.g. for `AppendRaw`), use `Quill.NewLine` or `Quill.NewLineString`.

---

## Testing Generators

The renderer pattern makes testing straightforward:

```csharp
[Fact]
public void Widget_EmitsCreateFactory()
{
    var target = new WidgetTarget("Amount", "MyApp", "global::System.Decimal",
        EmitCreateFactory: true, EmitParsable: false);
    var source = new WidgetRenderer(target).Render();

    source.ShouldContain("public static Amount Create(decimal value)");
}
```

For full integration tests, use `CSharpGeneratorDriver` from `Microsoft.CodeAnalysis.Testing`:

```csharp
var driver = CSharpGeneratorDriver.Create(new WidgetGenerator());
driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
    compilation, out var output, out var diagnostics);

diagnostics.ShouldBeEmpty();
output.SyntaxTrees.Count().ShouldBe(expectedCount);
```
