# Project Setup & Infrastructure

This guide covers how to configure a source generator or analyzer project that uses Scribe, and how to set up the LocalDev workflow for multi-repo development.

---

## Generator Project Configuration

Roslyn analyzers and source generators must target **netstandard2.0** — this is a hard requirement from the compiler host.

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0</TargetFrameworks>
    <LangVersion>14</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
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

### Key Properties Explained

| Property | Purpose |
|----------|---------|
| `TargetFrameworks=netstandard2.0` | Required by the Roslyn compiler host |
| `LangVersion=14` | Use modern C# features (with Stubs polyfills) |
| `EnforceExtendedAnalyzerRules` | Catches common analyzer authoring mistakes at compile time (e.g. RS1035) |
| `IncludeBuildOutput=false` | Prevents the DLL from landing in `lib/` — it goes into `analyzers/dotnet/cs/` instead |
| `PackageType=Analyzer` | Marks this package as an analyzer for NuGet |
| `DebugType=embedded` | Embeds PDB into the DLL for IDE debugging |
| `NU5128` suppression | Expected warning when `lib/` is empty (due to `IncludeBuildOutput=false`) |

### PrivateAssets

Both `Microsoft.CodeAnalysis.CSharp` and `BulletsForHumanity.Scribe` must use `PrivateAssets="all"`:

- **Roslyn SDK** is provided by the compiler host, not bundled.
- **Scribe** is a build-time utility — its DLL must be bundled into *your* analyzer package, not exposed as a transitive dependency.

### Multi-targeting

If your generator also needs a net10.0 target (e.g. for test projects that reference it directly), use conditional targeting:

```xml
<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
```

The `Stubs.cs` polyfills are guarded by `#if !NET5_0_OR_GREATER` and automatically deactivate on modern targets.

---

## Referencing a Generator Package

Consumers reference the generator via NuGet (or a local package via [LocalDev](#local-development-localdev)):

```xml
<PackageReference Include="MyGenerator" PrivateAssets="all" />
```

### Test projects

For test projects that instantiate generators directly via `CSharpGeneratorDriver`, reference the generator as a normal compile assembly so you can `new MyGenerator()`:

```xml
<PackageReference Include="MyGenerator" />
```

---

## Packaging

### Placing analyzer DLLs

The analyzer DLL must be in `analyzers/dotnet/cs/` in the NuGet package:

```xml
<Target Name="_AddAnalyzerDllsToPackage" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(OutputPath)$(AssemblyName).dll"
          Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  </ItemGroup>
</Target>
```

### Bundling dependencies

If your analyzer has private dependencies (like Scribe), include them alongside the main DLL:

```xml
<PropertyGroup>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>

<Target Name="_AddAnalyzerDependencies" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(OutputPath)Scribe.dll"
          Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  </ItemGroup>
</Target>
```

---

## Stubs (netstandard2.0 Polyfills)

Scribe ships `Stubs.cs` with internal polyfill types that enable modern C# features on netstandard2.0:

| Feature | C# Version | Polyfill |
|---------|-----------|----------|
| `init` setters / `record` types | C# 9 | `IsExternalInit` |
| `[ModuleInitializer]` | C# 9 | `ModuleInitializerAttribute` |
| `[SkipLocalsInit]` | C# 9 | `SkipLocalsInitAttribute` |
| `[CallerArgumentExpression]` | C# 10 | `CallerArgumentExpressionAttribute` |
| Interpolated string handlers | C# 10 | `InterpolatedStringHandlerAttribute` |
| `required` members | C# 11 | `RequiredMemberAttribute`, `CompilerFeatureRequiredAttribute` |
| `scoped ref` | C# 11 | `ScopedRefAttribute` |
| Nullable annotations | — | `NotNull`, `MaybeNull`, `AllowNull`, `NotNullWhen`, etc. |

Copy `Stubs.cs` into your own netstandard2.0 generator project to use these features. The polyfills are guarded by `#if !NET5_0_OR_GREATER` and deactivate automatically on modern targets.

---

## Local Development (LocalDev)

Scribe includes build infrastructure for developing NuGet packages across multiple repositories locally — without publishing to NuGet between each iteration.

### The Problem

When developing a framework (e.g. `MyFramework`) consumed by an application (e.g. `MyApp`), the normal workflow is:

1. Change MyFramework
2. Pack to NuGet
3. Update MyApp's package version
4. Restore and build MyApp

This is slow. LocalDev automates the entire cycle: build MyFramework once and MyApp automatically picks up the new package.

### How It Works

LocalDev uses MSBuild props/targets shipped inside the Scribe NuGet package. When activated:

1. The **producer** (MyFramework) auto-packs on build and writes packages to a shared `/.artifacts/packages/` directory.
2. The producer generates a version override file (`/.artifacts/MyFramework.Directory.Packages.targets`) that pins the local package version.
3. The **consumer** (MyApp) adds the shared packages directory as a NuGet source and imports the override file, so `dotnet restore` resolves the locally-built package.

### Setup

#### 1. Set `ScribeRoot` in the consumer

In the consumer's `Directory.Build.props`, point `ScribeRoot` to the shared workspace root (the parent directory that contains both repositories):

```xml
<PropertyGroup>
  <ScribeRoot>$(MSBuildThisFileDirectory)..\..</ScribeRoot>
</PropertyGroup>
```

#### 2. Configure the producer

In the producer's `Directory.Build.props`, set the trigger project and package names *before* importing Scribe's LocalDev files:

```xml
<PropertyGroup>
  <ScribeRoot>$(MSBuildThisFileDirectory)..</ScribeRoot>
  <ScribeLocalDevTriggerProject>MyFramework</ScribeLocalDevTriggerProject>
  <ScribeLocalDevPackageNames>MyFramework</ScribeLocalDevPackageNames>
</PropertyGroup>
```

#### 3. Import the LocalDev files

If you consume Scribe via NuGet, the import happens automatically (NuGet auto-imports `build/BulletsForHumanity.Scribe.props` and `.targets`).

For direct import from a sibling checkout:

```xml
<!-- In Directory.Build.props -->
<Import Project="$(ScribeRoot)\Scribe\Scribe\build\Scribe.LocalDev.props"
        Condition="Exists('$(ScribeRoot)\Scribe\Scribe\build\Scribe.LocalDev.props')" />

<!-- In Directory.Build.targets -->
<Import Project="$(ScribeRoot)\Scribe\Scribe\build\Scribe.LocalDev.targets"
        Condition="Exists('$(ScribeRoot)\Scribe\Scribe\build\Scribe.LocalDev.targets')" />
```

#### 4. Activate

Three ways to activate LocalDev:

1. **Build configuration:** Select `Debug (Local Scribe)` or `Release (Local Scribe)` in your IDE — configurations are injected automatically.
2. **MSBuild property:** `dotnet build -p:IsLocalScribe=true`
3. **Props file:** Set `<IsLocalScribe>true</IsLocalScribe>` in `Directory.Build.props` or `Directory.Solution.props`.

### Provided Properties

| Property | Value when active |
|----------|-------------------|
| `$(IsLocalScribe)` | `true` |
| `$(ScribeArtifactsDir)` | `$(ScribeRoot)\.artifacts\` |
| `$(ScribePackagesDir)` | `$(ScribeArtifactsDir)packages\` |

### Configuration Properties

| Property | Set by | Purpose |
|----------|--------|---------|
| `$(ScribeRoot)` | Consumer | Shared workspace root |
| `$(ScribeLocalDevTriggerProject)` | Producer | MSBuild project name that triggers auto-pack and override generation |
| `$(ScribeLocalDevPackageNames)` | Producer | Semicolon-separated NuGet package IDs to include in the override file |
| `$(ScribeLocalDevOverrideName)` | Producer (optional) | Override file name prefix — defaults to `$(ScribeLocalDevTriggerProject)` |

### Chaining

LocalDev supports multi-level chains. For example:

```
Scribe  ->  Hermetic  ->  MyApp
```

Each producer generates its own override file. The consumer imports all override files via wildcard from the shared `/.artifacts/` directory. Hermetic can re-export the LocalDev files inside its own NuGet package so that MyApp only needs `<ScribeRoot>` — no direct Scribe dependency required.
