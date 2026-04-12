using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Attributes;

namespace Scribe.Shapes;

/// <summary>
///     Stack-only context passed to the user projection callback of
///     <see cref="ShapeBuilder.Project{TModel}"/>. Exposes the matched symbol and the
///     attribute reader (if <see cref="ShapeBuilder.MustHaveAttribute{T}"/> declared
///     the driving attribute).
/// </summary>
/// <remarks>
///     This is a <c>ref struct</c>: it pins <see cref="INamedTypeSymbol"/> and must not
///     escape the projection. Use it only to extract the cache-safe values that will
///     live in the resulting <c>TModel</c>.
/// </remarks>
public readonly ref struct ShapeProjectionContext
{
    private readonly INamedTypeSymbol _symbol;
    private readonly AttributeReader _attribute;
    private readonly SemanticModel _semanticModel;
    private readonly Compilation _compilation;
    private readonly CancellationToken _cancellationToken;

    internal ShapeProjectionContext(
        INamedTypeSymbol symbol,
        AttributeReader attribute,
        SemanticModel semanticModel,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        _symbol = symbol;
        _attribute = attribute;
        _semanticModel = semanticModel;
        _compilation = compilation;
        _cancellationToken = cancellationToken;
    }

    /// <summary>The matched type symbol.</summary>
    public INamedTypeSymbol Symbol => _symbol;

    /// <summary>Reader over the driving attribute, if <see cref="ShapeBuilder.MustHaveAttribute{T}"/> was declared.</summary>
    public AttributeReader Attribute => _attribute;

    /// <summary>Semantic model of the declaration's syntax tree.</summary>
    public SemanticModel SemanticModel => _semanticModel;

    /// <summary>Current compilation.</summary>
    public Compilation Compilation => _compilation;

    /// <summary>Cancellation token flowed from the incremental pipeline.</summary>
    public CancellationToken CancellationToken => _cancellationToken;

    /// <summary>Fully-qualified display name of the matched type.</summary>
    public string Fqn => _symbol.ToDisplayString();
}

/// <summary>Projection delegate for <see cref="ShapeBuilder.Project{TModel}"/>.</summary>
public delegate TModel ProjectionDelegate<out TModel>(in ShapeProjectionContext ctx);
