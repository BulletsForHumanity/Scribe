namespace Scribe.Shapes;

/// <summary>
///     Closed enumeration of squiggle anchor points on a declared member of a
///     type (property, field, method, event, nested type, constructor).
///     Resolved by <see cref="MemberSquiggleLocator"/> against the member's
///     first declaring syntax reference.
/// </summary>
public enum MemberSquiggleAt
{
    /// <summary>The member's identifier (name) token.</summary>
    Identifier,

    /// <summary>The entire member declaration span.</summary>
    FullDeclaration,

    /// <summary>The member's type/return-type syntax.</summary>
    TypeAnnotation,

    /// <summary>The first attribute list on the member.</summary>
    FirstAttribute,

    /// <summary>The member's modifier list.</summary>
    ModifierList,
}
