# How the Infrastructure Works

Internal architecture of Scribe's build infrastructure — the Scribe SDK, Solution-Local Analyzer, and LocalDev systems. Read this if you're contributing to Scribe or want to understand the MSBuild mechanics.

For setup instructions, see [Project Setup & Infrastructure](project-setup.md).

---

## Scribe SDK

The Scribe SDK (`BulletsForHumanity.Scribe.Sdk`) is a custom MSBuild SDK that wraps `Microsoft.NET.Sdk` and auto-configures all boilerplate for Roslyn analyzer/generator projects.

### Package Layout

```
BulletsForHumanity.Scribe.Sdk.nupkg
  Sdk/
    Sdk.props               <- Chains to Microsoft.NET.Sdk, sets analyzer defaults
    Sdk.targets             <- Auto-includes Stubs.cs, defines packaging targets
  build/
    Scribe.LocalDev.props   <- Shared LocalDev infrastructure (early phase)
    Scribe.LocalDev.targets <- Shared LocalDev infrastructure (late phase)
    Scribe.SolutionAnalyzer.props   <- Solution-local analyzer support (early phase)
    Scribe.SolutionAnalyzer.targets <- Solution-local analyzer support (late phase)
  content/
    Stubs.cs                <- netstandard2.0 polyfills, injected as Compile item
```

### SDK Resolution

MSBuild resolves custom SDKs from NuGet packages that contain `Sdk/Sdk.props` and/or `Sdk/Sdk.targets`. Consumers declare the SDK version in `global.json`:

```json
{
  "msbuild-sdks": {
    "BulletsForHumanity.Scribe.Sdk": "0.3.0"
  }
}
```

### Sdk.props (Early Phase)

Runs before the project file is evaluated. Sets overridable defaults:

1. **Chains to `Microsoft.NET.Sdk`** via `<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />`
2. **Sets analyzer project defaults**: `TargetFramework=netstandard2.0`, `LangVersion=14`, `EnforceExtendedAnalyzerRules=true`, `IncludeBuildOutput=false`, `PackageType=Analyzer`, embedded PDB, nullable enabled
3. **Initialises `ScribeSdkIncludeStubs`** to `true` (opt-out via `<ScribeSdkIncludeStubs>false</ScribeSdkIncludeStubs>`)
4. **Imports `Scribe.SolutionAnalyzer.props`** for solution-local analyzer auto-pack configuration
5. **Imports `Scribe.LocalDev.props`** for sentinel file detection and local NuGet source registration

All properties can be overridden by the consuming `.csproj` because they're set in the early phase.

### Sdk.targets (Late Phase)

Runs after the project file. Enforces packaging behaviour:

1. **Chains to `Microsoft.NET.Sdk`** targets
2. **Auto-includes `Stubs.cs`** as a `Compile` item with `Link="Generated\Stubs.cs"` (invisible in Solution Explorer). Guarded by `ScribeSdkIncludeStubs=true`.
3. **`_ScribeSdkAddAnalyzerDlls` target**: Places the analyzer DLL into `analyzers/dotnet/cs/` in the NuGet package
4. **`_ScribeSdkAddAnalyzerDependencies` target**: Bundles private NuGet dependencies alongside the analyzer DLL. Excludes `Microsoft.CodeAnalysis.*` DLLs (provided by the compiler host).
5. **Sets `CopyLocalLockFileAssemblies=true`** to enable dependency bundling
6. **Imports `Scribe.SolutionAnalyzer.targets`** for solution-local cache invalidation
7. **Imports `Scribe.LocalDev.targets`** for version override wildcard import

---

## Solution-Local Analyzer

The Solution-Local Analyzer infrastructure enables intra-solution analyzer development — analyzers that live inside the same solution as the projects they serve, without publishing to NuGet.

For setup instructions, see [Solution-Local Analyzers](solution-local-analyzers.md).

### File Layout

```
Scribe/build/
  Scribe.SolutionAnalyzer.props    <- Configuration (early phase)
  Scribe.SolutionAnalyzer.targets  <- Cache invalidation target (late phase)
```

### Props Phase (Early)

`Scribe.SolutionAnalyzer.props` runs when `ScribeSolutionAnalyzer=true`:

1. **Locks the version** to `0.0.0-local` — disables NBGV (`NerdbankGitVersioningEnabled=false`)
2. **Enables auto-pack** via `GeneratePackageOnBuild=true`
3. **Resolves the package directory** — defaults to `.packages/` at the solution root (via `$(SolutionDir)` or parent of project directory)
4. **Redirects pack output** to `$(ScribeSolutionPackagesDir)`
5. **Registers as NuGet source** via `RestoreAdditionalProjectSources` (for the analyzer project itself)

### Targets Phase (Late)

`Scribe.SolutionAnalyzer.targets` defines one target:

**`_ScribeSolutionAnalyzerClearCache`** — Runs before `GenerateNuspec` (which precedes Pack). Removes:
- The cached package extraction from the NuGet global packages folder (`~/.nuget/packages/<id>/0.0.0-local/`)
- The old `.nupkg` from the `.packages/` directory

This ensures consuming projects always pick up the freshly-built package without requiring unique version numbers.

### Relationship to LocalDev

Solution-Local Analyzer and LocalDev are complementary:

| Concern | LocalDev | Solution-Local Analyzer |
|---------|----------|------------------------|
| Scope | Cross-repo | Intra-solution |
| Trigger | `.localscribe` sentinel | `ScribeSolutionAnalyzer=true` |
| Version | NBGV + timestamp suffix | Fixed `0.0.0-local` |
| Override files | Generated `.Directory.Packages.targets` | None needed |
| Package directory | Shared `/.artifacts/packages/` | Solution-local `.packages/` |

Both features are independent and can coexist. A solution can have solution-local analyzers and also participate in a LocalDev chain.

---

## LocalDev File Layout

The LocalDev infrastructure lives in `Scribe/build/` and is shipped inside both the `BulletsForHumanity.Scribe` NuGet package and the `BulletsForHumanity.Scribe.Sdk` package:

```
Scribe/build/
  BulletsForHumanity.Scribe.props    <- NuGet auto-import entry point (early)
  BulletsForHumanity.Scribe.targets  <- NuGet auto-import entry point (late)
  Scribe.LocalDev.props              <- Actual logic (early phase)
  Scribe.LocalDev.targets            <- Actual logic (late phase)
```

### NuGet Auto-Import Convention

NuGet automatically imports `build/<PackageId>.props` and `build/<PackageId>.targets` for any installed package. The entry-point files are thin wrappers that delegate to the `Scribe.LocalDev.*` files:

```xml
<!-- BulletsForHumanity.Scribe.props -->
<Import Project="$(MSBuildThisFileDirectory)Scribe.LocalDev.props" />
```

The `Scribe.LocalDev.*` files can also be imported directly from a sibling repo checkout, without requiring the NuGet package. This dual-import design enables the bootstrap chain.

---

## Props Phase (Early)

`Scribe.LocalDev.props` runs early in MSBuild evaluation — before `Directory.Packages.props` and before most project-level properties.

### Import Guard

Prevents double-import when both a NuGet package and a direct file import bring in the same file:

```xml
<PropertyGroup Condition="'$(_ScribeLocalDevImported)' == 'true'">
  <_ScribeLocalDevSkip>true</_ScribeLocalDevSkip>
</PropertyGroup>
<PropertyGroup Condition="'$(_ScribeLocalDevImported)' != 'true'">
  <_ScribeLocalDevImported>true</_ScribeLocalDevImported>
</PropertyGroup>
```

All subsequent property groups and imports are conditioned on `'$(_ScribeLocalDevSkip)' != 'true'`.

### Sentinel File Detection

Activates Local Scribe mode when a `.localscribe` file exists in `$(ScribeRoot)` (the shared workspace root). This works in both Visual Studio and CLI builds without build configuration hacks.

```xml
<PropertyGroup Condition="'$(_ScribeLocalDevSkip)' != 'true'
                          and '$(IsLocalScribe)' != 'true'
                          and '$(ScribeRoot)' != ''
                          and Exists('$(ScribeRoot)\.localscribe')">
  <IsLocalScribe>true</IsLocalScribe>
</PropertyGroup>
```

Three activation methods:

1. **Sentinel file (recommended):** Create `.localscribe` in `$(ScribeRoot)`. Delete it to deactivate. Add `.localscribe` to `.gitignore`.
2. **Explicit property:** `-p:IsLocalScribe=true` on the command line.
3. **Props file:** `<IsLocalScribe>true</IsLocalScribe>` in Directory.Build.props or Directory.Solution.props.

### Path Setup

When active, derives the shared artifacts and packages directories from `$(ScribeRoot)`:

```xml
<ScribeArtifactsDir>$(ScribeRoot)\.artifacts\</ScribeArtifactsDir>
<ScribePackagesDir>$(ScribeArtifactsDir)packages\</ScribePackagesDir>
<RestoreAdditionalProjectSources>
  $(RestoreAdditionalProjectSources);$(ScribePackagesDir)
</RestoreAdditionalProjectSources>
```

### Auto-Pack

For the trigger project only, enables `GeneratePackageOnBuild` and redirects output to the shared packages directory:

```xml
<GeneratePackageOnBuild>true</GeneratePackageOnBuild>
<PackageOutputPath>$(ScribePackagesDir)</PackageOutputPath>
```

---

## Targets Phase (Late)

`Scribe.LocalDev.targets` runs late — after `Directory.Packages.props` has declared all `PackageVersion Include` items.

### Override File Import

Uses a wildcard import to pick up all override files from the shared artifacts directory:

```xml
<Import
  Project="$(ScribeArtifactsDir)*.Directory.Packages.targets"
  Condition="'$(IsLocalScribe)' == 'true'
             and '$(ScribeArtifactsDir)' != ''
             and Exists('$(ScribeArtifactsDir)')" />
```

**Why .targets and not .props?** `PackageVersion Update` items must be evaluated *after* `Directory.Packages.props` declares the `PackageVersion Include` items. `.props` files are evaluated before `.props` imports finish; `.targets` files are evaluated after.

### Override File Generation

The `_ScribeLocalDevOverride` target runs after `Pack` on the trigger project only. It generates a `.Directory.Packages.targets` file containing `PackageVersion Update` entries:

```xml
<Project>
  <!-- Auto-generated by Scribe LocalDev — do not edit. -->
  <ItemGroup>
    <PackageVersion Update="MyFramework" Version="1.2.3-dev.20260405120000" />
  </ItemGroup>
</Project>
```

The version is read from `$(NuGetPackageVersion)` (set by Nerdbank.GitVersioning or the SDK).

---

## Multi-Repo Chain

The design supports arbitrary depth chains:

```
Scribe  ->  Hermetic  ->  MyApp
```

### How Chaining Works

1. **Scribe** ships LocalDev files in its NuGet package.
2. **Hermetic** references Scribe via NuGet. NuGet auto-imports Scribe's LocalDev files. Hermetic configures itself as a producer (trigger project, package names). When built in Local Scribe mode, it:
   - Auto-packs to `/.artifacts/packages/`
   - Generates `/.artifacts/Hermetic.Directory.Packages.targets`
3. **Hermetic** re-exports the LocalDev files inside its own NuGet package, so consumers get them transitively.
4. **MyApp** references Hermetic via NuGet. NuGet auto-imports Hermetic's targets, which import Scribe's LocalDev files. When built in Local Scribe mode, the wildcard import picks up `Hermetic.Directory.Packages.targets`.

### Re-Exporting

A framework package that depends on Scribe can re-export the LocalDev files by including them in its own `build/` directory:

```xml
<!-- In the framework's .csproj -->
<None Include="path/to/Scribe.LocalDev.props" Pack="true" PackagePath="build" />
<None Include="path/to/Scribe.LocalDev.targets" Pack="true" PackagePath="build" />
```

The framework's own NuGet auto-import files (`build/Framework.props` and `.targets`) delegate to them, just like Scribe's entry points do.

---

## Version Suffixing

Local builds use timestamp-based dev version suffixes (e.g. `0.2.12-dev.20260405120000`). This is handled outside of Scribe — typically by a target in the producer's `Directory.Build.targets` that strips any NBGV git-hash suffix and replaces it with `-dev.yyyyMMddHHmmss`. The timestamp ensures:

- Versions are unique per build
- Versions sort chronologically
- Versions are human-readable
- No collision with published NuGet versions

---

## Artifacts Directory Layout

When LocalDev is active, the shared `/.artifacts/` directory looks like:

```
.artifacts/
  packages/
    MyFramework.1.2.3-dev.20260405120000.nupkg
    MyFramework.1.2.3-dev.20260405120000.snupkg
  MyFramework.Directory.Packages.targets    <- Version override file
```

The `packages/` subdirectory is registered as a NuGet source. The override file is imported via wildcard.
