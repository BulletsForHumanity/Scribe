namespace Scribe.Shapes;

/// <summary>
///     Coarse type-kind filter applied at the syntax stage before semantic analysis.
///     Picked by <see cref="Shape.Class"/>, <see cref="Shape.Record"/>, etc.
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
