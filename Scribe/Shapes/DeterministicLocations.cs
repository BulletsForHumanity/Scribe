using Microsoft.CodeAnalysis;

namespace Scribe.Shapes;

/// <summary>
///     Picks a deterministic primary <see cref="Location"/> or
///     <see cref="SyntaxReference"/> for a symbol. Necessary because
///     <c>ISymbol.Locations[0]</c> and <c>ISymbol.DeclaringSyntaxReferences[0]</c>
///     do not guarantee a stable ordering across partial declarations —
///     the same type declared in two files may yield different "first"
///     references depending on compilation ordering. Ordering by
///     <see cref="SyntaxTree.FilePath"/> then <see cref="Microsoft.CodeAnalysis.Text.TextSpan.Start"/>
///     produces a stable anchor so diagnostic squiggles land on a predictable
///     declaration.
/// </summary>
internal static class DeterministicLocations
{
    public static Location? PrimaryLocation(ISymbol symbol)
    {
        var reference = PrimaryReference(symbol);
        if (reference is not null)
        {
            return Location.Create(reference.SyntaxTree, reference.Span);
        }

        var locations = symbol.Locations;
        if (locations.Length == 0)
        {
            return null;
        }

        if (locations.Length == 1)
        {
            return locations[0];
        }

        Location? best = null;
        foreach (var candidate in locations)
        {
            if (best is null || CompareLocations(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    public static Location PrimaryLocationOrNone(ISymbol symbol)
        => PrimaryLocation(symbol) ?? Location.None;

    public static SyntaxReference? PrimaryReference(ISymbol symbol)
    {
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length == 0)
        {
            return null;
        }

        if (refs.Length == 1)
        {
            return refs[0];
        }

        SyntaxReference best = refs[0];
        for (var i = 1; i < refs.Length; i++)
        {
            if (CompareReferences(refs[i], best) < 0)
            {
                best = refs[i];
            }
        }

        return best;
    }

    private static int CompareReferences(SyntaxReference x, SyntaxReference y)
    {
        var fileCmp = string.CompareOrdinal(x.SyntaxTree.FilePath, y.SyntaxTree.FilePath);
        return fileCmp != 0 ? fileCmp : x.Span.Start.CompareTo(y.Span.Start);
    }

    private static int CompareLocations(Location x, Location y)
    {
        var xPath = x.SourceTree?.FilePath ?? string.Empty;
        var yPath = y.SourceTree?.FilePath ?? string.Empty;
        var fileCmp = string.CompareOrdinal(xPath, yPath);
        return fileCmp != 0 ? fileCmp : x.SourceSpan.Start.CompareTo(y.SourceSpan.Start);
    }
}
