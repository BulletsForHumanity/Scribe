using Microsoft.CodeAnalysis;

namespace Scribe.Shapes;

/// <summary>
///     B.7 — cross-focus symbol comparison helpers. Compares two foci without
///     leaking raw <see cref="ISymbol"/> references into cached pipeline state:
///     the helpers run inside a live analyzer / generator pass where the symbol
///     reference is valid, and return a plain <see cref="bool"/> that is safe to
///     feed into downstream equality.
/// </summary>
/// <remarks>
///     <para>
///         Use inside lens-configure callbacks or <c>Satisfy</c> predicates where
///         an outer focus has been captured via closure, and you need to assert
///         that a navigated inner focus refers to the same underlying type.
///         The canonical example is the Event Contract cycle check: the
///         <c>[Applies&lt;E&gt;]</c> type argument on a handler must equal the
///         event <c>E</c> the outer chain navigated to.
///     </para>
///     <para>
///         All comparisons use <see cref="SymbolEqualityComparer.Default"/> over
///         <see cref="ISymbol.OriginalDefinition"/> — so open- and closed-generic
///         instantiations of the same definition compare equal.
///     </para>
/// </remarks>
public static class FocusSymbols
{
    /// <summary>
    ///     Compare two type-symbol references using <see cref="ISymbol.OriginalDefinition"/>.
    ///     Returns <see langword="false"/> if either side is <see langword="null"/>.
    /// </summary>
    public static bool SameOriginalDefinition(ISymbol? left, ISymbol? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(
            left.OriginalDefinition, right.OriginalDefinition);
    }

    /// <summary>
    ///     Direct <see cref="SymbolEqualityComparer.Default"/> comparison — stricter
    ///     than <c>SameOriginalDefinition</c>; a closed-generic <c>List&lt;int&gt;</c>
    ///     and <c>List&lt;string&gt;</c> compare unequal here but equal under
    ///     <c>SameOriginalDefinition</c>.
    /// </summary>
    public static bool SymbolEquals(ISymbol? left, ISymbol? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(left, right);
    }

    /// <summary>
    ///     Cross-focus wrapper: compare the underlying symbols of two
    ///     <see cref="TypeFocus"/> values by original definition.
    /// </summary>
    public static bool SameOriginalDefinition(this TypeFocus left, TypeFocus right) =>
        SameOriginalDefinition(left.Symbol, right.Symbol);

    /// <summary>
    ///     Cross-focus wrapper: compare a <see cref="TypeFocus"/> against a
    ///     <see cref="TypeArgFocus"/> by original definition. Typical use: assert
    ///     that a navigated generic type argument refers to the outer type focus.
    /// </summary>
    public static bool SameOriginalDefinition(this TypeFocus left, TypeArgFocus right) =>
        SameOriginalDefinition(left.Symbol, right.Symbol);

    /// <summary>Mirror of the <c>(TypeFocus, TypeArgFocus)</c> overload.</summary>
    public static bool SameOriginalDefinition(this TypeArgFocus left, TypeFocus right) =>
        SameOriginalDefinition(left.Symbol, right.Symbol);

    /// <summary>Cross-focus wrapper over two <see cref="TypeArgFocus"/> values.</summary>
    public static bool SameOriginalDefinition(this TypeArgFocus left, TypeArgFocus right) =>
        SameOriginalDefinition(left.Symbol, right.Symbol);
}
