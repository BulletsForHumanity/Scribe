using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Attributes;

namespace Scribe.Shapes;

/// <summary>
///     Stack-only context passed to the user etch callback of
///     <see cref="TypeShape.Etch{TModel}"/>. Exposes the matched symbol and the
///     attribute reader (if <see cref="TypeShape.MustHaveAttribute{T}"/> declared
///     the driving attribute).
/// </summary>
/// <remarks>
///     This is a <c>ref struct</c>: it pins <see cref="INamedTypeSymbol"/> and must not
///     escape the etch callback. Use it only to extract the cache-safe values that will
///     live in the resulting <c>TModel</c>.
/// </remarks>
public readonly ref struct ShapeEtchContext
{
    private readonly INamedTypeSymbol _symbol;
    private readonly AttributeReader _attribute;
    private readonly SemanticModel _semanticModel;
    private readonly Compilation _compilation;
    private readonly CancellationToken _cancellationToken;

    internal ShapeEtchContext(
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

    /// <summary>Reader over the driving attribute, if <see cref="TypeShape.MustHaveAttribute{T}"/> was declared.</summary>
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

/// <summary>Etch delegate for <see cref="TypeShape.Etch{TModel}"/>.</summary>
public delegate TModel EtchDelegate<out TModel>(in ShapeEtchContext ctx);
