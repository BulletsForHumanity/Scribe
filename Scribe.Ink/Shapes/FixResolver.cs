using Scribe.Ink.Shapes.Fixes;
using Scribe.Shapes;

namespace Scribe.Ink.Shapes;

/// <summary>
///     Maps a <see cref="FixKind"/> to its concrete <see cref="IShapeFix"/>
///     implementation. Shared between <see cref="ShapeCodeFixProvider"/> and
///     <see cref="ShapeFixAllProvider"/>.
/// </summary>
internal static class FixResolver
{
    public static IShapeFix? Resolve(FixKind kind) => kind switch
    {
        FixKind.AddPartialModifier => new AddPartialModifierFix(),
        FixKind.AddSealedModifier => new AddSealedModifierFix(),
        FixKind.AddInterfaceToBaseList => new AddInterfaceToBaseListFix(),
        FixKind.AddAttribute => new AddAttributeFix(),
        FixKind.AddAbstractModifier => new AddAbstractModifierFix(),
        FixKind.RemoveAbstractModifier => new RemoveAbstractModifierFix(),
        FixKind.AddStaticModifier => new AddStaticModifierFix(),
        FixKind.AddBaseClass => new AddBaseClassFix(),
        FixKind.RemoveFromBaseList => new RemoveFromBaseListFix(),
        FixKind.RemovePartialModifier => new RemovePartialModifierFix(),
        FixKind.RemoveSealedModifier => new RemoveSealedModifierFix(),
        FixKind.RemoveStaticModifier => new RemoveStaticModifierFix(),
        FixKind.RemoveAttribute => new RemoveAttributeFix(),
        FixKind.SetVisibility => new SetVisibilityFix(),
        FixKind.AddParameterlessConstructor => new AddParameterlessConstructorFix(),
        FixKind.AddReadOnlyModifier => new AddReadOnlyModifierFix(),
        _ => null,
    };
}
