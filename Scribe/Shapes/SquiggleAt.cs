namespace Scribe.Shapes;

/// <summary>
///     Closed enumeration of squiggle anchor points on a type declaration.
///     Each <see cref="TypeShape"/> primitive picks an opinionated default
///     so the diagnostic lands in the most editorially-useful place.
/// </summary>
public enum SquiggleAt
{
    /// <summary>The <c>class</c> / <c>struct</c> / <c>record</c> keyword token.</summary>
    TypeKeyword,

    /// <summary>The type's identifier (name) token.</summary>
    Identifier,

    /// <summary>The modifier list (e.g., <c>public sealed</c>).</summary>
    ModifierList,

    /// <summary>The base list (e.g., <c>: IFoo, IDisposable</c>).</summary>
    BaseList,

    /// <summary>The attribute list preceding the declaration.</summary>
    AttributeList,

    /// <summary>The containing namespace declaration.</summary>
    ContainingNamespace,

    /// <summary>The entire type declaration span.</summary>
    EntireDeclaration,

    /// <summary>The first member of the offending kind.</summary>
    FirstMemberOfKind,
}
