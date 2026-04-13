using System;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Fluent sub-lens entry points on <see cref="FocusShape{AttributeFocus}"/>. These
///     extensions thread further navigation off a single attribute application — into
///     its generic type arguments, positional constructor arguments, and named
///     arguments — each producing a nested <see cref="FocusShape{T}"/> the caller can
///     populate with per-argument checks.
/// </summary>
public static class AttributeFocusShapeExtensions
{
    /// <summary>
    ///     Enter the generic-type-argument sub-lens: navigate the type argument at
    ///     position <paramref name="index"/> of the attribute's generic argument list —
    ///     e.g. index <c>0</c> of <c>[Foo&lt;string&gt;]</c>. Declares a presence
    ///     constraint when <paramref name="min"/> / <paramref name="max"/> are supplied;
    ///     <c>min: 1</c> means "an argument at this index must exist".
    /// </summary>
    public static FocusShape<AttributeFocus> GenericTypeArg(
        this FocusShape<AttributeFocus> shape,
        int index,
        Action<FocusShape<TypeArgFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<TypeArgFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.GenericTypeArg(index);

        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE053",
            defaultTitle: "Attribute generic-type-argument presence constraint",
            defaultMessage: "Expected [{0}..{1}] generic type arguments at index "
                + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", observed {2}");

        shape.AddBranch(new LensBranch<AttributeFocus, TypeArgFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin));

        return shape;
    }

    /// <summary>
    ///     Enter the positional-constructor-argument sub-lens: navigate the argument at
    ///     position <paramref name="index"/>, coercing its value to
    ///     <typeparamref name="T"/>. Declares a presence constraint when
    ///     <paramref name="min"/> / <paramref name="max"/> are supplied;
    ///     <c>min: 1</c> means "an argument of this type at this index must exist".
    /// </summary>
    public static FocusShape<AttributeFocus> ConstructorArg<T>(
        this FocusShape<AttributeFocus> shape,
        int index,
        Action<FocusShape<ConstructorArgFocus<T>>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<ConstructorArgFocus<T>>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.ConstructorArg<T>(index);

        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE054",
            defaultTitle: "Attribute constructor-argument presence constraint",
            defaultMessage: "Expected [{0}..{1}] constructor argument(s) of type '"
                + typeof(T).FullName
                + "' at index "
                + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", observed {2}");

        shape.AddBranch(new LensBranch<AttributeFocus, ConstructorArgFocus<T>>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin));

        return shape;
    }

    /// <summary>
    ///     Enter the named-argument sub-lens: navigate the argument named
    ///     <paramref name="name"/>, coercing its value to <typeparamref name="T"/>.
    ///     Declares a presence constraint when <paramref name="min"/> /
    ///     <paramref name="max"/> are supplied; <c>min: 1</c> means "a named argument
    ///     with this name must exist".
    /// </summary>
    public static FocusShape<AttributeFocus> NamedArg<T>(
        this FocusShape<AttributeFocus> shape,
        string name,
        Action<FocusShape<NamedArgFocus<T>>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        ValidateCounts(min, max);

        var nested = new FocusShape<NamedArgFocus<T>>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.NamedArg<T>(name);

        var presence = BuildPresence(
            min, max, presenceSpec,
            defaultId: "SCRIBE055",
            defaultTitle: "Attribute named-argument presence constraint",
            defaultMessage: "Expected [{0}..{1}] named argument(s) named '"
                + name + "' of type '" + typeof(T).FullName + "', observed {2}");

        shape.AddBranch(new LensBranch<AttributeFocus, NamedArgFocus<T>>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin));

        return shape;
    }

    private static void ValidateCounts(int min, int? max)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum count must be non-negative.");
        }

        if (max is { } m && m < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum count must be at least the minimum.");
        }
    }

    private static LensPresenceSpec? BuildPresence(
        int min,
        int? max,
        DiagnosticSpec? spec,
        string defaultId,
        string defaultTitle,
        string defaultMessage)
    {
        var hasPresence = min > 0 || max is not null || spec is not null;
        if (!hasPresence)
        {
            return null;
        }

        return new LensPresenceSpec(
            Id: InternPool.Intern(spec?.Id ?? defaultId),
            Title: spec?.Title ?? defaultTitle,
            MessageFormat: spec?.Message ?? defaultMessage,
            Severity: spec?.Severity ?? DiagnosticSeverity.Error);
    }
}
