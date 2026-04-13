namespace Scribe.Shapes;

/// <summary>
///     Coarse type-kind filter applied at the syntax stage before semantic analysis.
///     Picked by <see cref="Stencil.ExposeClass"/>, <see cref="Stencil.ExposeRecord"/>, etc.
/// </summary>
internal enum TypeKindFilter
{
    Class,
    Record,
    RecordStruct,
    Struct,
    Interface,
    Any,
}
