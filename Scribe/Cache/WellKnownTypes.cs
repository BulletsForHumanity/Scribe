using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Scribe.Cache;

/// <summary>
///     A small per-<see cref="Compilation"/> cache of <see cref="INamedTypeSymbol"/>s
///     resolved by metadata name, so each symbol is looked up at most once per compilation.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Not cache-safe.</strong> This type holds <see cref="INamedTypeSymbol"/>
///         references, which are tied to the specific <see cref="Compilation"/> they were
///         resolved from. Construct it fresh at the <em>edge</em> of an incremental pipeline
///         stage (typically inside a <c>CompilationProvider.Select</c> callback) and discard
///         it before any cached value crosses the stage boundary.
///     </para>
///     <para>
///         The purpose is purely to avoid repeating
///         <see cref="Compilation.GetTypeByMetadataName(string)"/> — an O(n) scan over
///         referenced assemblies — for the same name within a single pipeline stage.
///     </para>
/// </remarks>
public sealed class WellKnownTypes
{
    private readonly Dictionary<string, INamedTypeSymbol?> _cache;

    private WellKnownTypes(Dictionary<string, INamedTypeSymbol?> cache) => _cache = cache;

    /// <summary>
    ///     Resolve all listed metadata names against <paramref name="compilation"/> and build
    ///     a fresh cache.
    /// </summary>
    public static WellKnownTypes From(Compilation compilation, params string[] metadataNames)
    {
        if (compilation is null)
        {
            throw new System.ArgumentNullException(nameof(compilation));
        }

        var cache = new Dictionary<string, INamedTypeSymbol?>(
            metadataNames?.Length ?? 0,
            System.StringComparer.Ordinal);

        if (metadataNames is not null)
        {
            foreach (var name in metadataNames)
            {
                if (!string.IsNullOrEmpty(name) && !cache.ContainsKey(name))
                {
                    cache[name] = compilation.GetTypeByMetadataName(name);
                }
            }
        }

        return new WellKnownTypes(cache);
    }

    /// <summary>
    ///     Resolve a metadata name, caching the result. Unlike <see cref="From(Compilation, string[])"/>,
    ///     this is used when the set of required types is not known up front. Requires the caller
    ///     to hold a live <see cref="Compilation"/>.
    /// </summary>
    public INamedTypeSymbol? Resolve(Compilation compilation, string metadataName)
    {
        if (_cache.TryGetValue(metadataName, out var cached))
        {
            return cached;
        }

        var resolved = compilation.GetTypeByMetadataName(metadataName);
        _cache[metadataName] = resolved;
        return resolved;
    }

    /// <summary>Look up a pre-resolved metadata name. Returns <see langword="null"/> if not present or not found.</summary>
    public INamedTypeSymbol? Get(string metadataName) =>
        _cache.TryGetValue(metadataName, out var symbol) ? symbol : null;

    /// <summary>Look up a pre-resolved metadata name, returning <see langword="false"/> if not present.</summary>
    public bool TryGet(string metadataName, out INamedTypeSymbol? symbol) =>
        _cache.TryGetValue(metadataName, out symbol);
}
