using System;

namespace Scribe.Shapes;

/// <summary>
///     A value-equatable pair of <see cref="ShapedSymbol{TModel}"/>s produced by
///     <see cref="Prism.By{TLeft, TRight}"/>. Flows through
///     <see cref="Microsoft.CodeAnalysis.IncrementalValuesProvider{TValues}"/>
///     so that joined-pair consumers react only to actual pairing changes, not
///     unrelated edits on either side.
/// </summary>
public readonly record struct ShapedPrism<TLeft, TRight>(
    ShapedSymbol<TLeft> Left,
    ShapedSymbol<TRight> Right)
    where TLeft : IEquatable<TLeft>
    where TRight : IEquatable<TRight>;
