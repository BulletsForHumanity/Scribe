namespace Scribe.Shapes;

/// <summary>
///     Entry point for the Shape DSL. Pick the type-kind you want to match, then fluently
///     layer <c>MustBeX</c> constraints, then <see cref="ShapeBuilder.Project{TModel}"/>
///     into a cache-safe model.
/// </summary>
/// <example>
///     <code>
///         var shape = Shape.Class()
///             .MustHaveAttribute&lt;ThingAttribute&gt;()
///             .MustBePartial()
///             .MustImplement&lt;IThing&gt;()
///             .Project(in ctx => new ThingModel(
///                 Fqn: ctx.Fqn,
///                 Flavor: ctx.Attribute.Ctor&lt;string&gt;(0) ?? "default"));
///     </code>
/// </example>
public static class Shape
{
    /// <summary>Match <c>class</c> declarations.</summary>
    public static ShapeBuilder Class() => new(TypeKindFilter.Class);

    /// <summary>Match <c>record</c> declarations (class form).</summary>
    public static ShapeBuilder Record() => new(TypeKindFilter.Record);

    /// <summary>Match <c>record struct</c> declarations.</summary>
    public static ShapeBuilder RecordStruct() => new(TypeKindFilter.RecordStruct);

    /// <summary>Match <c>struct</c> declarations.</summary>
    public static ShapeBuilder Struct() => new(TypeKindFilter.Struct);

    /// <summary>Match <c>interface</c> declarations.</summary>
    public static ShapeBuilder Interface() => new(TypeKindFilter.Interface);

    /// <summary>Match any type declaration regardless of kind.</summary>
    public static ShapeBuilder AnyType() => new(TypeKindFilter.Any);
}
