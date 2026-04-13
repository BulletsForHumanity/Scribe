namespace Scribe.Shapes;

/// <summary>
///     The door to the Shape DSL. Pick the type-kind you want to match and
///     <c>Expose</c> a focused <see cref="TypeShape"/>; then fluently layer
///     <c>MustBeX</c> constraints and seal the chain with
///     <see cref="TypeShape.Etch{TModel}"/>.
/// </summary>
/// <remarks>
///     <para>
///         The lithography metaphor runs end-to-end: the Stencil is the mask,
///         <see cref="ExposeClass"/> (and peers) is the UV exposure step that prints
///         the latent pattern onto the wafer, and <see cref="TypeShape.Etch{TModel}"/>
///         is the permanent commitment of the finished pattern into the substrate.
///     </para>
/// </remarks>
/// <example>
///     <code>
///         Shape&lt;ThingModel&gt; shape = Stencil.ExposeClass()
///             .MustHaveAttribute&lt;ThingAttribute&gt;()
///             .MustBePartial()
///             .MustImplement&lt;IThing&gt;()
///             .Etch(in ctx =&gt; new ThingModel(
///                 Fqn: ctx.Fqn,
///                 Flavor: ctx.Attribute.Ctor&lt;string&gt;(0) ?? "default"));
///     </code>
/// </example>
public static class Stencil
{
    /// <summary>Expose a <see cref="TypeShape"/> over <c>class</c> declarations.</summary>
    public static TypeShape ExposeClass() => new(TypeKindFilter.Class);

    /// <summary>Expose a <see cref="TypeShape"/> over <c>record</c> declarations (class form).</summary>
    public static TypeShape ExposeRecord() => new(TypeKindFilter.Record);

    /// <summary>Expose a <see cref="TypeShape"/> over <c>record struct</c> declarations.</summary>
    public static TypeShape ExposeRecordStruct() => new(TypeKindFilter.RecordStruct);

    /// <summary>Expose a <see cref="TypeShape"/> over <c>struct</c> declarations.</summary>
    public static TypeShape ExposeStruct() => new(TypeKindFilter.Struct);

    /// <summary>Expose a <see cref="TypeShape"/> over <c>interface</c> declarations.</summary>
    public static TypeShape ExposeInterface() => new(TypeKindFilter.Interface);

    /// <summary>Expose a <see cref="TypeShape"/> over any type declaration regardless of kind.</summary>
    public static TypeShape ExposeAnyType() => new(TypeKindFilter.Any);

    /// <summary>
    ///     Compose two or more <see cref="TypeShape"/> alternatives into a single
    ///     <see cref="OneOfTypeShape"/>. The resulting shape passes a symbol when
    ///     at least one alternative's check tree is silent against it; when every
    ///     alternative fails, a single fused diagnostic summarises each branch's
    ///     unsatisfied check IDs.
    /// </summary>
    /// <param name="alternatives">Two or more fully-authored branches. Each branch supplies its own kind filter and checks.</param>
    public static OneOfTypeShape OneOf(params TypeShape[] alternatives) =>
        new(alternatives, fusionSpec: null);

    /// <summary>
    ///     Overload of <see cref="OneOf(TypeShape[])"/> that accepts a
    ///     <see cref="DiagnosticSpec"/> override for the fused diagnostic.
    /// </summary>
    public static OneOfTypeShape OneOf(DiagnosticSpec fusionSpec, params TypeShape[] alternatives) =>
        new(alternatives, fusionSpec);
}
