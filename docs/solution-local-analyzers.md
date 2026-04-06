# Solution-Local Analyzers

How to set up Roslyn analyzers and source generators that live inside the same solution they serve — no NuGet publishing, no cross-repo setup.

---

## Overview

A **solution-local analyzer** is a Roslyn analyzer or source generator project that exists inside the same solution as the projects it serves. It is not published to NuGet. It is not consumed cross-repo. It automates *this* codebase and nothing else.

Examples:

- A generator that reads domain model types and emits mapping code, DTOs, or validation rules
- An analyzer that enforces solution-specific coding conventions
- A generator that emits strongly-typed configuration accessors from appsettings schema

The Scribe SDK provides first-class support for this workflow. When `ScribeSolutionAnalyzer=true` is set on a Scribe SDK project, the SDK automatically:

1. **Packs the analyzer on every build** — produces a `.nupkg` in a solution-local `.packages/` directory
2. **Uses a fixed local version** (`0.0.0-local`) — no version management needed
3. **Clears the NuGet cache** — consuming projects always get the freshly-built package
4. **Bundles private dependencies** — the same dependency bundling as any Scribe SDK project

Consuming projects reference the analyzer via standard `<PackageReference>`, getting all the same analyzer wiring that any published package gets.

---

## Quick Start

### 1. Create the analyzer project

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

### 2. Register the local package source

Add a `NuGet.config` at the solution root (one-time setup):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="solution-local" value=".packages" />
  </packageSources>
</configuration>
```

Alternatively, add to your `Directory.Build.props`:

```xml
<PropertyGroup>
  <RestoreAdditionalProjectSources>
    $(RestoreAdditionalProjectSources);$(MSBuildThisFileDirectory).packages
  </RestoreAdditionalProjectSources>
</PropertyGroup>
```

### 3. Reference from consuming projects

```xml
<ItemGroup>
  <PackageReference Include="MyAnalyzer" Version="0.0.0-local" />
</ItemGroup>
```

Or with [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management):

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="MyAnalyzer" Version="0.0.0-local" />
```

### 4. Add `.packages/` to `.gitignore`

```gitignore
.packages/
```

### 5. Build

```shell
dotnet build MySolution.sln
```

The analyzer auto-packs on build. Consuming projects resolve the package from the local `.packages/` directory. Diagnostics and generated code appear automatically.

---

## How It Works

### Auto-Pack on Build

When `ScribeSolutionAnalyzer=true`, the SDK sets `GeneratePackageOnBuild=true` and redirects `PackageOutputPath` to the `.packages/` directory. Every build produces a fresh `.nupkg`.

### Fixed Version

The version is locked to `0.0.0-local`. This avoids version management complexity — there's no NBGV, no timestamps, no override files. Consuming projects always reference `Version="0.0.0-local"`.

### Cache Invalidation

NuGet caches extracted packages in the global packages folder (`~/.nuget/packages/`). With a fixed version, the cache would serve stale content. The SDK handles this automatically: before each pack, the `_ScribeSolutionAnalyzerClearCache` target removes the cached extraction and the old `.nupkg`, forcing NuGet to re-extract the freshly-built package on the next restore.

### Package Directory

By default, packages are placed in `.packages/` relative to the solution directory. The SDK resolves the location in this order:

1. `$(ScribeSolutionPackagesDir)` — if explicitly set by the developer
2. `$(SolutionDir).packages\` — when building via a solution file or from Visual Studio
3. `$(MSBuildProjectDirectory)\..\.packages\` — fallback for individual project builds

Override the default in your `Directory.Build.props`:

```xml
<PropertyGroup>
  <ScribeSolutionPackagesDir>$(MSBuildThisFileDirectory).packages\</ScribeSolutionPackagesDir>
</PropertyGroup>
```

---

## Solution Layout

A typical solution with a solution-local analyzer:

```
MySolution/
  MySolution.sln
  NuGet.config              ← registers .packages/ as a source
  Directory.Build.props     ← (optional) shared properties
  .gitignore                ← includes .packages/
  .packages/                ← auto-generated, git-ignored
    MyAnalyzer.0.0.0-local.nupkg
  MyAnalyzer/
    MyAnalyzer.csproj       ← ScribeSolutionAnalyzer=true
    MyGenerator.cs
  MyApp/
    MyApp.csproj            ← <PackageReference Include="MyAnalyzer" Version="0.0.0-local" />
    Program.cs
```

---

## Multiple Analyzers

A solution can have multiple solution-local analyzers. All pack to the same `.packages/` directory:

```xml
<!-- FirstAnalyzer/FirstAnalyzer.csproj -->
<Project Sdk="BulletsForHumanity.Scribe.Sdk">
  <PropertyGroup>
    <ScribeSolutionAnalyzer>true</ScribeSolutionAnalyzer>
  </PropertyGroup>
  ...
</Project>
```

```xml
<!-- SecondAnalyzer/SecondAnalyzer.csproj -->
<Project Sdk="BulletsForHumanity.Scribe.Sdk">
  <PropertyGroup>
    <ScribeSolutionAnalyzer>true</ScribeSolutionAnalyzer>
  </PropertyGroup>
  ...
</Project>
```

```xml
<!-- MyApp/MyApp.csproj -->
<ItemGroup>
  <PackageReference Include="FirstAnalyzer" Version="0.0.0-local" />
  <PackageReference Include="SecondAnalyzer" Version="0.0.0-local" />
</ItemGroup>
```

---

## Relationship to LocalDev

Solution-local analyzers and LocalDev solve different problems:

| Concern | LocalDev | Solution-Local Analyzer |
|---------|----------|------------------------|
| Scope | Cross-repo (e.g. Scribe → Hermetic → MyApp) | Intra-solution |
| Trigger | `.localscribe` sentinel file | `ScribeSolutionAnalyzer=true` property |
| Published? | Yes (eventually to NuGet) | Never |
| Version management | NBGV + timestamp + override files | Fixed `0.0.0-local` |
| Package directory | Shared `/.artifacts/packages/` | Solution-local `.packages/` |
| Setup complexity | `$(ScribeRoot)`, trigger project, package names | One property + NuGet source |

If your analyzer is (or will become) a standalone package consumed across repositories, use the standard Scribe SDK workflow with [LocalDev](project-setup.md#local-development-localdev). If it exists solely to serve the solution it lives in, use `ScribeSolutionAnalyzer`.

---

## Provided Properties

| Property | Value | Set by |
|----------|-------|--------|
| `$(ScribeSolutionAnalyzer)` | `true` | Developer (in `.csproj`) |
| `$(ScribeSolutionPackagesDir)` | `.packages/` at solution root | SDK (overridable) |
| `$(Version)` | `0.0.0-local` | SDK |
| `$(PackageVersion)` | `0.0.0-local` | SDK |
| `$(GeneratePackageOnBuild)` | `true` | SDK |
| `$(PackageOutputPath)` | `$(ScribeSolutionPackagesDir)` | SDK |

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Detection mechanism | Explicit `ScribeSolutionAnalyzer=true` property | Simple, discoverable, no magic. Convention-based detection would be ambiguous. |
| Version strategy | Fixed `0.0.0-local` | Avoids version management complexity. Cache invalidation is handled by a build target. |
| Package directory | `.packages/` at solution root | Mirrors `.artifacts/` convention. Solution-scoped, git-ignored. |
| NuGet source registration | Manual one-time setup (NuGet.config or Directory.Build.props) | The SDK can only configure projects that use it. Consuming projects need a standard NuGet mechanism. |
| Cache invalidation | Delete from NuGet global cache before each pack | Ensures consuming projects always get the fresh package without requiring unique versions. |

---

## Troubleshooting

### Consuming project doesn't see the analyzer

1. Verify the `.packages/` directory contains a `.nupkg` for the analyzer
2. Verify `NuGet.config` (or `RestoreAdditionalProjectSources`) includes the `.packages/` directory
3. Run `dotnet restore` on the consuming project
4. In Visual Studio, try **Build → Rebuild Solution**

### Stale generated code after analyzer changes

The `_ScribeSolutionAnalyzerClearCache` target should handle this automatically. If you still see stale output:

1. Delete the `.packages/` directory
2. Delete `~/.nuget/packages/<analyzer-name>/0.0.0-local/`
3. Run `dotnet restore && dotnet build`

### Package directory location is wrong

Set `$(ScribeSolutionPackagesDir)` explicitly in your `Directory.Build.props`:

```xml
<PropertyGroup>
  <ScribeSolutionPackagesDir>$(MSBuildThisFileDirectory).packages\</ScribeSolutionPackagesDir>
</PropertyGroup>
```
