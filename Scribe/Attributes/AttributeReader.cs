using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Attributes;

/// <summary>
///     Stack-only, typed reader over a single <see cref="AttributeData"/>. Obtain via
///     <see cref="AttributeSchema.For"/> or the <see cref="AttributeProjection{TModel}"/>
///     callback supplied to <see cref="AttributeSchema.Read{TModel}"/>.
/// </summary>
/// <remarks>
///     <para>
///         This is a <c>ref struct</c>: it pins the underlying <see cref="AttributeData"/>
///         (which holds symbol references) and must not escape the current stack frame.
///         Read what you need, project into a cache-safe model, let the reader go out of scope.
///     </para>
///     <para>
///         All typed-read methods silently return <c>default</c> when the requested slot is
///         missing, out of range, or the value is not convertible to the requested type —
///         and push a <see cref="DiagnosticInfo"/> into <see cref="DrainErrors"/>.
///     </para>
/// </remarks>
public ref struct AttributeReader
{
    private readonly AttributeData? _attribute;
    private List<DiagnosticInfo>? _errors;

    internal AttributeReader(AttributeData? attribute)
    {
        _attribute = attribute;
        _errors = null;
    }

    /// <summary>Whether a matching attribute was found on the inspected symbol.</summary>
    public readonly bool Exists => _attribute is not null;

    /// <summary>Location of the attribute application syntax, if available.</summary>
    public readonly LocationInfo? Location =>
        _attribute is null ? null : LocationInfo.From(GetAttributeLocation(_attribute));

    /// <summary>
    ///     Number of constructor arguments supplied at the call site. Zero if the attribute
    ///     is missing.
    /// </summary>
    public readonly int CtorArgCount =>
        _attribute?.ConstructorArguments.Length ?? 0;

    /// <summary>
    ///     Read constructor argument <paramref name="index"/> as <typeparamref name="T"/>.
    ///     Emits a <see cref="DiagnosticInfo"/> into <see cref="DrainErrors"/> and returns
    ///     <c>default</c> if the index is out of range or the value is not a
    ///     <typeparamref name="T"/>.
    /// </summary>
    public T? Ctor<T>(int index)
    {
        if (_attribute is null)
        {
            return default;
        }

        var args = _attribute.ConstructorArguments;
        if ((uint)index >= (uint)args.Length)
        {
            PushError(
                DiagnosticIds.AttributeCtorMissing,
                DiagnosticSeverity.Warning,
                EquatableArray.Create(index.ToString(), _attribute.AttributeClass?.Name ?? "?"));
            return default;
        }

        return Coerce<T>(args[index], $"ctor[{index}]");
    }

    /// <summary>
    ///     Read constructor argument <paramref name="index"/> as <typeparamref name="T"/>;
    ///     returns <c>default</c> silently (no diagnostic) if the index is out of range.
    ///     A type mismatch still emits a diagnostic.
    /// </summary>
    public T? CtorOpt<T>(int index)
    {
        if (_attribute is null)
        {
            return default;
        }

        var args = _attribute.ConstructorArguments;
        if ((uint)index >= (uint)args.Length)
        {
            return default;
        }

        return Coerce<T>(args[index], $"ctor[{index}]");
    }

    /// <summary>
    ///     Read the named argument (property or field) <paramref name="name"/> as
    ///     <typeparamref name="T"/>, returning <paramref name="fallback"/> if the argument
    ///     is absent. A type mismatch emits a diagnostic and returns <paramref name="fallback"/>.
    /// </summary>
    public T Named<T>(string name, T fallback)
    {
        if (_attribute is null || string.IsNullOrEmpty(name))
        {
            return fallback;
        }

        foreach (var pair in _attribute.NamedArguments)
        {
            if (!string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                continue;
            }

            var coerced = Coerce<T>(pair.Value, name);
            return coerced is null ? fallback : coerced;
        }

        return fallback;
    }

    /// <summary>
    ///     Read the <paramref name="index"/>-th generic type argument of the attribute
    ///     (e.g., <c>[Foo&lt;Bar&gt;]</c> → <c>TypeArg(0) == Bar</c>). Returns
    ///     <see langword="null"/> and emits a diagnostic if the index is out of range.
    /// </summary>
    public ITypeSymbol? TypeArg(int index)
    {
        if (_attribute is null)
        {
            return null;
        }

        var typeArgs = _attribute.AttributeClass?.TypeArguments ?? ImmutableArray<ITypeSymbol>.Empty;
        if ((uint)index >= (uint)typeArgs.Length)
        {
            PushError(
                DiagnosticIds.AttributeTypeArgMissing,
                DiagnosticSeverity.Warning,
                EquatableArray.Create(index.ToString(), _attribute.AttributeClass?.Name ?? "?"));
            return null;
        }

        return typeArgs[index];
    }

    /// <summary>
    ///     Return the accumulated diagnostics and reset the internal buffer. Intended to be
    ///     called once at the end of a projection.
    /// </summary>
    public EquatableArray<DiagnosticInfo> DrainErrors()
    {
        if (_errors is null || _errors.Count == 0)
        {
            return EquatableArray<DiagnosticInfo>.Empty;
        }

        var array = EquatableArray.From<DiagnosticInfo>(_errors);
        _errors = null;
        return array;
    }

    private T? Coerce<T>(TypedConstant value, string slotDescription)
    {
        if (value.IsNull)
        {
            return default;
        }

        // Array values: project each element.
        if (value.Kind == TypedConstantKind.Array)
        {
            if (typeof(T).IsArray)
            {
                var elementType = typeof(T).GetElementType()!;
                var source = value.Values;
                var array = Array.CreateInstance(elementType, source.Length);
                for (var i = 0; i < source.Length; i++)
                {
                    var element = source[i].Value;
                    if (element is not null && elementType.IsAssignableFrom(element.GetType()))
                    {
                        array.SetValue(element, i);
                    }
                }

                return (T)(object)array;
            }
        }

        // Type arguments: TypedConstant of kind Type carries an ITypeSymbol in .Value.
        if (value.Kind == TypedConstantKind.Type)
        {
            if (value.Value is T typed)
            {
                return typed;
            }
        }

        if (value.Value is T direct)
        {
            return direct;
        }

        // Enums are boxed as their underlying integral value.
        if (value.Kind == TypedConstantKind.Enum && value.Value is not null)
        {
            try
            {
                return (T)value.Value;
            }
            catch (InvalidCastException)
            {
                // fall through to mismatch path
            }
        }

        PushError(
            DiagnosticIds.AttributeValueMismatch,
            DiagnosticSeverity.Warning,
            EquatableArray.Create(slotDescription, typeof(T).Name));
        return default;
    }

    private void PushError(string id, DiagnosticSeverity severity, EquatableArray<string> args)
    {
        _errors ??= new List<DiagnosticInfo>();
        _errors.Add(new DiagnosticInfo(
            Id: id,
            Severity: severity,
            MessageArgs: args,
            Location: Location));
    }

    private static Location? GetAttributeLocation(AttributeData attribute)
    {
        var reference = attribute.ApplicationSyntaxReference;
        if (reference is null)
        {
            return null;
        }

        return Microsoft.CodeAnalysis.Location.Create(reference.SyntaxTree, reference.Span);
    }
}

/// <summary>
///     Well-known diagnostic IDs emitted by <see cref="AttributeReader"/> when typed reads
///     fail. Consumers can register matching <see cref="DiagnosticDescriptor"/>s if they
///     wish to surface these to the user; otherwise the diagnostics are informational only.
/// </summary>
public static class DiagnosticIds
{
    /// <summary>Constructor argument index out of range.</summary>
    public const string AttributeCtorMissing = "SCRIBE100";

    /// <summary>Constructor / named argument value did not match the requested type.</summary>
    public const string AttributeValueMismatch = "SCRIBE101";

    /// <summary>Generic type-argument index out of range on the attribute class.</summary>
    public const string AttributeTypeArgMissing = "SCRIBE102";
}
