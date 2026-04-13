using System;

namespace Scribe.Shapes;

/// <summary>
///     Fluent sub-lens entry points on <see cref="FocusShape{BaseTypeChainFocus}"/>.
///     Currently exposes <see cref="AsTypeShape"/> — re-entering the type-shape world
///     rooted at a base-chain step so type-level lenses (attributes, members,
///     base-type-chain) can run against the base type itself.
/// </summary>
public static class BaseTypeChainFocusShapeExtensions
{
    /// <summary>
    ///     Enter a nested <see cref="FocusShape{TypeFocus}"/> rooted at this base-chain
    ///     step. Always yields exactly one <see cref="TypeFocus"/> — the base type's
    ///     symbol, which is guaranteed to be an
    ///     <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/>.
    /// </summary>
    public static FocusShape<BaseTypeChainFocus> AsTypeShape(
        this FocusShape<BaseTypeChainFocus> shape,
        Action<FocusShape<TypeFocus>>? configure = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        var nested = new FocusShape<TypeFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.BaseTypeChainAsTypeShape();

        shape.AddBranch(new LensBranch<BaseTypeChainFocus, TypeFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: 0,
            MaxCount: null,
            Presence: null,
            ParentOrigin: parent => parent.Origin));

        return shape;
    }
}
