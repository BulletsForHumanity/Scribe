namespace Scribe.Shapes;

/// <summary>
///     A declarative hint — picked up by Phase-4 code-fix generation — describing
///     how a violation of a <see cref="TypeShape"/> check should be auto-repaired.
/// </summary>
public readonly record struct FixSpec(FixKind Kind, string? EquivalenceKey = null);

/// <summary>
///     Catalog of built-in fix shapes recognised by <c>Scribe.Fixes</c>.
/// </summary>
public enum FixKind
{
    /// <summary>No automatic fix — the diagnostic is informational or requires user judgement.</summary>
    None,

    /// <summary>Add the <c>partial</c> modifier to the type declaration.</summary>
    AddPartialModifier,

    /// <summary>Add the <c>sealed</c> modifier to the type declaration.</summary>
    AddSealedModifier,

    /// <summary>Add an interface to the base list.</summary>
    AddInterfaceToBaseList,

    /// <summary>Remove a specific entry from the base list.</summary>
    RemoveFromBaseList,

    /// <summary>Add an attribute to the declaration.</summary>
    AddAttribute,

    /// <summary>Rename the type to match a specified pattern.</summary>
    RenameTo,

    /// <summary>Generate a stub method matching a specified signature.</summary>
    AddStubMethod,

    /// <summary>Add the <c>abstract</c> modifier to the type declaration.</summary>
    AddAbstractModifier,

    /// <summary>Remove the <c>abstract</c> modifier from the type declaration.</summary>
    RemoveAbstractModifier,

    /// <summary>Add the <c>static</c> modifier to the type declaration.</summary>
    AddStaticModifier,

    /// <summary>Add a base class to the type declaration (fix property <c>baseClass</c>).</summary>
    AddBaseClass,

    /// <summary>Remove the <c>partial</c> modifier from the type declaration.</summary>
    RemovePartialModifier,

    /// <summary>Remove the <c>sealed</c> modifier from the type declaration.</summary>
    RemoveSealedModifier,

    /// <summary>Remove the <c>static</c> modifier from the type declaration.</summary>
    RemoveStaticModifier,

    /// <summary>Remove an attribute from the declaration (fix property <c>attribute</c>).</summary>
    RemoveAttribute,

    /// <summary>Change the type's visibility modifier (fix property <c>visibility</c> — <c>public</c>/<c>internal</c>/<c>private</c>).</summary>
    SetVisibility,

    /// <summary>Add an explicit public parameterless constructor to the type.</summary>
    AddParameterlessConstructor,

    /// <summary>Add the <c>readonly</c> modifier to the type declaration.</summary>
    AddReadOnlyModifier,

    /// <summary>Remove the <c>readonly</c> modifier from the type declaration.</summary>
    RemoveReadOnlyModifier,

    /// <summary>
    ///     User-supplied fix. The diagnostic carries a <c>customFixTag</c> property
    ///     that <see cref="TypeShape"/> consumers register a handler for via the
    ///     Ink extension <c>WithCustomFix</c>. The fix provider looks up the tag
    ///     and dispatches to the user delegate instead of the built-in
    ///     <see cref="FixKind"/> catalog.
    /// </summary>
    Custom,
}
