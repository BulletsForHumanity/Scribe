namespace Scribe.Shapes;

/// <summary>
///     How a lens branch aggregates the pass/fail result of its nested sub-shape
///     across the set of navigated children. Chooses between three classic
///     higher-order quantifiers.
/// </summary>
/// <remarks>
///     <para>
///         "A child passes" is defined as: <em>every</em> nested leaf predicate
///         returns <see langword="true"/> <em>and</em> every nested sub-lens branch
///         produces zero violations when evaluated against that child.
///     </para>
///     <para>
///         <see cref="All"/> is the default — the behaviour every lens branch had
///         before quantifiers existed — and is usually the implicit semantic of a
///         chained lens. <see cref="Any"/> / <see cref="None"/> exist for set-level
///         checks the fluent-chain form cannot express.
///     </para>
/// </remarks>
public enum Quantifier
{
    /// <summary>
    ///     Every navigated child must pass. Violations emit per (child, check)
    ///     and land at each offending child's smudge anchor. Default.
    /// </summary>
    All = 0,

    /// <summary>
    ///     At least one navigated child must pass. Per-child violations are
    ///     suppressed; when zero children pass, one aggregate diagnostic is emitted
    ///     at the parent focus's span.
    /// </summary>
    Any = 1,

    /// <summary>
    ///     No navigated child may pass. Per-child "violations" (which here would
    ///     indicate the absence of a match) are suppressed; when one or more
    ///     children pass, one aggregate diagnostic is emitted at each offending
    ///     child's smudge anchor.
    /// </summary>
    None = 2,
}
