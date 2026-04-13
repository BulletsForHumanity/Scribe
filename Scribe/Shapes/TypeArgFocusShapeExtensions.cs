using System;

namespace Scribe.Shapes;

/// <summary>
///     Fluent sub-lens entry points on <see cref="FocusShape{TypeArgFocus}"/>. Currently
///     exposes <see cref="AsTypeShape"/> — re-entering the type-shape world rooted at
///     the type argument to reuse type-level lenses (attributes, members,
///     base-type-chain) underneath a generic position.
/// </summary>
public static class TypeArgFocusShapeExtensions
{
    /// <summary>
    ///     Enter a nested <see cref="FocusShape{TypeFocus}"/> rooted at the type
    ///     argument. Yields no focus (silent pass) when the argument's symbol is not an
    ///     <see cref="Microsoft.CodeAnalysis.INamedTypeSymbol"/> — type parameters,
    ///     arrays, pointers, and function-pointer types all pass silently. Downstream
    ///     lens branches declared on the nested shape evaluate against each named type
    ///     argument.
    /// </summary>
    public static FocusShape<TypeArgFocus> AsTypeShape(
        this FocusShape<TypeArgFocus> shape,
        Action<FocusShape<TypeFocus>>? configure = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        var nested = new FocusShape<TypeFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.TypeArgAsTypeShape();

        shape.AddBranch(new LensBranch<TypeArgFocus, TypeFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: 0,
            MaxCount: null,
            Presence: null,
            ParentOrigin: parent => parent.Origin));

        return shape;
    }
}
