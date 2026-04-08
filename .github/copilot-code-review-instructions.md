You are reviewing code in the Scribe library — a fluent DSL for writing Roslyn incremental source generators, code fix providers, and analyzers.

## Before reviewing any change, read these docs first

1. **[README](../README.md)** — Overview, quick start, key concepts

## Review lens

Every change should be evaluated against these principles:

### netstandard2.0 Compatibility

- Scribe targets `netstandard2.0` — this is a hard Roslyn compiler host requirement.
- No APIs or language features that require newer TFMs unless properly polyfilled.
- Polyfill stubs in `Stubs.cs` must be guarded by `#if !NET5_0_OR_GREATER`.

### API Consistency

- Does the new API follow the fluent builder pattern established by `Quill`?
- Are naming conventions consistent with existing helpers (`Naming`, `XmlDoc`, `WellKnownFqns`)?
- Are all public types documented with XML doc comments?

### Technical Properties

- Zero runtime reflection — no `Type.GetType()`, no `Activator.CreateInstance()`, no `Reflection.Emit`
- Fully trimmable — no patterns that break trim analysis
- AOT compatible — no dynamic type loading
- Generated code must be deterministic (same input → same output, always)

### Framework Purity

- Scribe is a public, reusable library. **No application-specific references**.
- Examples in code and docs must use generic names.

### Conventional Commits

- Commit messages must follow the format: `<type>(<scope>): <subject>`
- Valid types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert
- Valid scopes: scribe, docs, ci, deps

### Documentation Sync

- If the change modifies the public surface, corresponding documentation must be updated in the same PR.
- Check: README.md, package READMEs, copilot instructions.
