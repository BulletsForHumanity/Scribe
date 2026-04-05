# Generator Architecture Pattern

Scribe encourages a clean three-phase generator architecture:

## 1. Target Collection (Transform)

Extract an equatable record ("target") from the Roslyn semantic model. The target captures all
the information the renderer needs — emit flags, type names, detected user-defined members:

```csharp
private static EssenceTarget? TransformEssence(GeneratorSyntaxContext ctx, CancellationToken ct)
{
    var symbol = ctx.SemanticModel.GetDeclaredSymbol((RecordDeclarationSyntax)ctx.Node, ct);
    if (symbol is null || !symbol.DerivesFrom("MyFramework.IEssence"))
        return null;

    var underlying = /* extract underlying type info */;
    var hasUserCreate = symbol.GetMembers("Create").OfType<IMethodSymbol>().Any(m => m.IsStatic);

    return new EssenceTarget(
        symbol.Name,
        Namespace: symbol.ContainingNamespace.ToDisplayString(),
        UnderlyingFqn: underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        EmitCreateFactory: !hasUserCreate,
        EmitParsable: !alreadyParsable,
        // ... emit flags based on what the user already defined
    );
}
```

The target record must be equatable so Roslyn's incremental pipeline can cache and skip
unchanged types between compilations.

## 2. Pipeline Registration

Wire the syntax provider, transform, and output in `Initialize()`:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var essences = context.SyntaxProvider
        .CreateSyntaxProvider(
            static (node, _) => SyntaxPredicates.IsPartialRecord(node),
            static (ctx, ct) => TransformEssence(ctx, ct))
        .Where(static t => t is not null);

    context.RegisterSourceOutput(
        essences.Collect(),
        static (spc, targets) =>
        {
            foreach (var t in targets)
            {
                if (t is not null)
                    spc.AddSource($"{t.Name}.g.cs", new EssenceRenderer(t).Render());
            }
        });
}
```

## 3. Rendering

A renderer class takes the target and uses `Quill` to produce the output.
Each concern gets its own private method — the `Render()` method orchestrates the structure:

```csharp
internal sealed class EssenceRenderer(EssenceTarget target)
{
    public string Render()
    {
        var q = new Quill();
        if (target.Namespace.Length > 0)
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

            EmitToString(q);
        }

        return q.Inscribe();
    }

    private void EmitCreate(Quill q)
    {
        using (q.Block($"public static {target.Name} Create({target.UnderlyingFqn} value)")
                .Summary($"Creates a new <see cref=\"{target.Name}\"/> from the given value."))
        {
            q.Lines($$"""
                if (value == default)
                    throw new global::System.ArgumentException("bad", nameof(value));
                return new {{target.Name}}(value);
                """);
        }
    }

    private void EmitParsable(Quill q) { /* ... */ }
    private void EmitToString(Quill q) { /* ... */ }
}
```

This pattern keeps the generator (`IIncrementalGenerator`) thin — it only wires pipelines.
The transform extracts semantic info into a cacheable record. The renderer is a plain class
that's easy to test: pass a target, call `Render()`, assert on the returned string.

## Project Configuration

### Generator .csproj

Roslyn analyzers and source generators must target **netstandard2.0** — this is a hard requirement
from the compiler host:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0</TargetFrameworks>
    <LangVersion>14</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <!--
      IncludeBuildOutput=false: the DLL must NOT land in lib/ — NuGet would try to
      reference it as a compile assembly. Place it in analyzers/dotnet/cs/ instead.
    -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <PackageType>Analyzer</PackageType>
    <IncludeSymbols>false</IncludeSymbols>
    <DebugType>embedded</DebugType>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="BulletsForHumanity.Scribe" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Key points:
- **`EnforceExtendedAnalyzerRules`** — catches common analyzer/generator authoring mistakes at compile time
- **`IncludeBuildOutput=false`** — prevents the DLL from landing in `lib/`; you pack it into `analyzers/dotnet/cs/` instead
- **`PrivateAssets="all"`** on `Microsoft.CodeAnalysis.CSharp` — the Roslyn SDK is provided by the compiler host, not bundled
- **`CopyLocalLockFileAssemblies=true`** (optional) — if your analyzer has private NuGet dependencies, this ensures they're copied to the output for bundling into the analyzer folder

### Referencing a generator project

Consumer projects reference your generator as an analyzer, not a compile dependency:

```xml
<ProjectReference Include="..\MyGenerator\MyGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

For **dev mode** (where test projects instantiate generators directly), reference it as a
normal compile assembly instead — omit `OutputItemType="Analyzer"` so you can `new MyGenerator()`
in test code and drive it through `CSharpGeneratorDriver`.

### Packaging

To place analyzer DLLs in the correct NuGet package path:

```xml
<Target Name="_AddAnalyzerDllsToPackage" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(OutputPath)$(AssemblyName).dll"
          Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  </ItemGroup>
</Target>
```

If your analyzer has private dependencies (like Scribe), use a `GetAnalyzerFiles` target to
include them alongside the main DLL in `analyzers/dotnet/cs/`.
