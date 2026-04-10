# BulletsForHumanity.Scribe.Sdk

A custom MSBuild SDK that eliminates boilerplate from Roslyn analyzer and source generator projects.

## Quick Start

**1. Add a `global.json`** with the SDK version:

```json
{
  "msbuild-sdks": {
    "BulletsForHumanity.Scribe.Sdk": "0.3.0"
  }
}
```

**2. Use the SDK** in your `.csproj`:

```xml
<Project Sdk="BulletsForHumanity.Scribe.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="BulletsForHumanity.Scribe" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

That's it. The SDK handles everything else:

- `TargetFramework=netstandard2.0` (required by Roslyn compiler host)
- `LangVersion=14` for modern C# features
- `EnforceExtendedAnalyzerRules=true` to catch common mistakes
- Analyzer packaging (`analyzers/dotnet/cs/` placement, embedded PDB)
- Private dependency bundling (Scribe DLL alongside your analyzer)
- `Stubs.cs` polyfills for `init`, `record`, `required`, nullable annotations
- LocalDev multi-repo development infrastructure

## What the SDK Sets

| Property | Default | Purpose |
| ---------- | --------- | --------- |
| `TargetFramework` | `netstandard2.0` | Required by the Roslyn compiler host |
| `LangVersion` | `14` | Modern C# features with polyfill stubs |
| `EnforceExtendedAnalyzerRules` | `true` | Catches analyzer authoring mistakes |
| `IncludeBuildOutput` | `false` | DLL goes into `analyzers/`, not `lib/` |
| `PackageType` | `Analyzer` | Marks package as an analyzer |
| `IncludeSymbols` | `false` | PDB is embedded in the DLL |
| `DebugType` | `embedded` | IDE debugging support |
| `Nullable` | `enable` | Null-safety |
| `GenerateDocumentationFile` | `false` | Not needed for bundled DLLs |
| `CopyLocalLockFileAssemblies` | `true` | Enables private dependency bundling |

All defaults are set in the early phase (`Sdk.props`) and can be overridden in your `.csproj`. Packaging targets (analyzer DLL placement, dependency bundling) are enforced by the SDK and cannot be overridden.

## Stubs.cs Polyfills

The SDK auto-includes `Stubs.cs` with polyfill types for modern C# features on netstandard2.0:

- `init` setters / `record` types (C# 9)
- `[ModuleInitializer]`, `[SkipLocalsInit]` (C# 9)
- `[CallerArgumentExpression]` (C# 10)
- Interpolated string handlers (C# 10)
- `required` members (C# 11)
- `scoped ref` parameters (C# 11)
- Nullable flow analysis attributes

All polyfills are guarded by `#if !NET5_0_OR_GREATER` and deactivate on modern targets.

**Opt out:** Set `<ScribeSdkIncludeStubs>false</ScribeSdkIncludeStubs>` if you provide your own.

## Packaging

`dotnet pack` produces a correct analyzer NuGet package:

- Your analyzer DLL is placed in `analyzers/dotnet/cs/`
- Private dependencies (like Scribe) are bundled alongside
- Roslyn SDK DLLs are excluded (provided by the compiler host)

## LocalDev

The SDK includes [LocalDev](https://github.com/BulletsForHumanity/Scribe/blob/master/docs/project-setup.md#local-development-localdev) infrastructure for multi-repo development. See the main Scribe documentation for setup instructions.

## Solution-Local Analyzers

For analyzers that live inside the same solution they serve, set `ScribeSolutionAnalyzer=true` to auto-pack on build with automatic version management:

```xml
<Project Sdk="BulletsForHumanity.Scribe.Sdk">
  <PropertyGroup>
    <ScribeSolutionAnalyzer>true</ScribeSolutionAnalyzer>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="BulletsForHumanity.Scribe" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Each build produces a unique timestamp version and a version override file, using the same mechanism as [LocalDev](https://github.com/BulletsForHumanity/Scribe/blob/master/docs/project-setup.md#local-development-localdev) but scoped to the solution. See [Solution-Local Analyzers](https://github.com/BulletsForHumanity/Scribe/blob/master/docs/solution-local-analyzers.md) for the complete setup guide.

## Links

- [Scribe Documentation](https://github.com/BulletsForHumanity/Scribe)
- [Project Setup Guide](https://github.com/BulletsForHumanity/Scribe/blob/master/docs/project-setup.md)
- [Writing Generators](https://github.com/BulletsForHumanity/Scribe/blob/master/docs/writing-generators.md)
