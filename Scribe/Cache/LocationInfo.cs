using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Scribe.Cache;

/// <summary>
///     A cache-safe stand-in for <see cref="Location"/>. Captures file path and spans
///     as primitive values so the enclosing value can flow through the Roslyn incremental
///     cache without pinning a <see cref="SyntaxTree"/> / <see cref="Compilation"/>.
/// </summary>
/// <remarks>
///     <para>
///         Roslyn's <see cref="Location"/> holds a reference to its <see cref="SyntaxTree"/>,
///         which in turn is tied to a <see cref="Compilation"/>. Storing a <see cref="Location"/>
///         in any cached pipeline value pins the entire compilation in memory and invalidates
///         the cache on every compilation change.
///     </para>
///     <para>
///         Convert at the boundary: use <see cref="From(Location)"/> when entering the pipeline
///         and <see cref="ToLocation"/> when materialising a diagnostic for reporting.
///     </para>
/// </remarks>
public readonly record struct LocationInfo(
    string FilePath,
    TextSpan TextSpan,
    LinePositionSpan LineSpan)
{
    /// <summary>
    ///     Convert a Roslyn <see cref="Location"/> into a cache-safe <see cref="LocationInfo"/>.
    ///     Returns <see langword="null"/> for null, non-source, or tree-less locations.
    /// </summary>
    public static LocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var tree = location.SourceTree;
        if (tree is null)
        {
            return null;
        }

        return new LocationInfo(
            InternPool.Intern(tree.FilePath),
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    /// <summary>Materialise back into a Roslyn <see cref="Location"/> for reporting.</summary>
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}
