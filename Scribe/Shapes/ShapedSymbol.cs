using System;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     A cache-safe projection of a single type matched by a <see cref="Shape{TModel}"/>.
///     Flows through <see cref="Microsoft.CodeAnalysis.IncrementalValuesProvider{TValues}"/>
///     as the unit of change the generator downstream reacts to.
/// </summary>
/// <typeparam name="TModel">Consumer-supplied projection; must be value-equatable for cache correctness.</typeparam>
/// <param name="Fqn">Fully-qualified display name of the matched type (interned).</param>
/// <param name="Model">User projection of the symbol — cache-safe by constraint.</param>
/// <param name="Location">Location of the primary declaration, for downstream reporting.</param>
/// <param name="Violations">Collected <see cref="DiagnosticInfo"/>s emitted by declared shape checks.</param>
public readonly record struct ShapedSymbol<TModel>(
    string Fqn,
    TModel Model,
    LocationInfo? Location,
    EquatableArray<DiagnosticInfo> Violations)
    where TModel : IEquatable<TModel>;
