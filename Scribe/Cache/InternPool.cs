using System.Collections.Concurrent;

namespace Scribe.Cache;

/// <summary>
///     Process-wide string intern pool for strings that repeat across an incremental
///     compilation — diagnostic IDs, fully-qualified type names, file paths, attribute
///     metadata names.
/// </summary>
/// <remarks>
///     <para>
///         The primary benefit is reducing <see cref="string.Equals(string, string)"/> on
///         cache-key comparison paths. An interned string compares reference-first, so the
///         hot path collapses from <c>O(length)</c> to <c>O(1)</c> in the common case.
///     </para>
///     <para>
///         This pool is separate from the BCL <see cref="string.Intern(string)"/> to avoid
///         polluting the CLR's global intern table with per-analyzer strings.
///     </para>
///     <para>
///         <strong>Scope:</strong> pool strings that are (a) small, (b) high-repetition,
///         (c) drawn from a bounded vocabulary. Do <em>not</em> intern user message text,
///         user-authored member names, or anything unbounded.
///     </para>
/// </remarks>
public static class InternPool
{
    private static readonly ConcurrentDictionary<string, string> Pool = new();

    /// <summary>
    ///     Return the canonical interned instance for <paramref name="value"/>. Subsequent
    ///     calls with an equal string return the same reference.
    /// </summary>
    public static string Intern(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return Pool.GetOrAdd(value, value);
    }

    /// <summary>Current number of distinct strings in the pool. Primarily for diagnostics/tests.</summary>
    public static int Count => Pool.Count;
}
