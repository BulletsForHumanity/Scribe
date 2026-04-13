using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     B.9 — LINQ query-comprehension parity. These pass-throughs give the fluent
///     Shape DSL the verb names the C# query-comprehension desugarer expects, so
///     <c>from / where / select</c> against a <see cref="TypeShape"/> compiles
///     without wrappers.
/// </summary>
/// <remarks>
///     <para>
///         <c>select</c> (<see cref="Select{TModel}"/>) is an alias for
///         <see cref="Etch{TModel}"/>. <c>where</c> (the single-argument
///         <c>Where</c>) is an alias for <see cref="Check"/> accepting a
///         focus-shaped predicate.
///     </para>
///     <para>
///         Multi-focus composition (<c>from t in shape from a in t.Attributes(...)</c>)
///         is expressed in v1 via the lens-entry callbacks (<c>Attributes</c>,
///         <c>Members</c>, <c>BaseTypeChain</c>). A true <c>SelectMany</c> that joins
///         two focus streams into a multi-variable comprehension is deferred to a
///         later phase.
///     </para>
/// </remarks>
public sealed partial class TypeShape
{
    /// <summary>
    ///     LINQ alias for <see cref="Etch{TModel}"/>. Receives a <see cref="TypeFocus"/>
    ///     (the non-ref, cache-safe view of the matched type) rather than a
    ///     <see cref="ShapeEtchContext"/>, so an implicit-typed lambda — as used by the
    ///     query-comprehension desugarer — binds cleanly.
    /// </summary>
    /// <typeparam name="TModel">Equatable model type produced for each surviving row.</typeparam>
    /// <param name="selector">Projection from the focus to the terminal model.</param>
    public Shape<TModel> Select<TModel>(Func<TypeFocus, TModel> selector)
        where TModel : IEquatable<TModel>
    {
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        return Etch<TModel>((in ShapeEtchContext ctx) =>
        {
            var focus = new TypeFocus(
                symbol: ctx.Symbol,
                fqn: InternPool.Intern(ctx.Fqn),
                origin: LocationInfo.From(FirstLocation(ctx.Symbol)));
            return selector(focus);
        });
    }

    /// <summary>
    ///     LINQ alias for a focus-shaped <see cref="Check"/>. Registers a positive
    ///     predicate — <see langword="true"/> means the Shape is satisfied — and
    ///     reports <c>SCRIBE200</c> when it fails. This single-argument form is what
    ///     the query-comprehension <c>where</c> clause desugars to; use the
    ///     <see cref="Where(Func{TypeFocus, bool}, DiagnosticSpec)"/> overload when
    ///     you want a stable custom diagnostic id or message.
    /// </summary>
    /// <param name="predicate">Positive predicate evaluated against the matched type's focus.</param>
    public TypeShape Where(Func<TypeFocus, bool> predicate) =>
        Where(predicate, new DiagnosticSpec(Id: "SCRIBE200"));

    /// <summary>
    ///     Explicit-spec form of <see cref="Where(Func{TypeFocus, bool})"/>. Use this
    ///     when authoring in fluent form and you want to pin a stable diagnostic id.
    /// </summary>
    /// <param name="predicate">Positive predicate evaluated against the matched type's focus.</param>
    /// <param name="spec">
    ///     Diagnostic descriptor. <see cref="DiagnosticSpec.Id"/> is required;
    ///     other fields fall back to sensible defaults (title <c>"Where predicate"</c>,
    ///     message <c>"Where predicate on '{0}' was not satisfied"</c>, severity
    ///     <see cref="DiagnosticSeverity.Warning"/>).
    /// </param>
    public TypeShape Where(Func<TypeFocus, bool> predicate, DiagnosticSpec spec)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (string.IsNullOrEmpty(spec.Id))
        {
            throw new ArgumentException(
                "DiagnosticSpec.Id is required on Where(...) — the query author must pick a stable diagnostic id.",
                nameof(spec));
        }

        return Check(
            predicate: (symbol, _, _) =>
            {
                var focus = new TypeFocus(
                    symbol: symbol,
                    fqn: InternPool.Intern(symbol.ToDisplayString()),
                    origin: LocationInfo.From(FirstLocation(symbol)));
                return predicate(focus);
            },
            id: spec.Id!,
            title: spec.Title ?? "Where predicate",
            message: spec.Message ?? "Where predicate on '{0}' was not satisfied",
            severity: spec.Severity ?? DiagnosticSeverity.Warning,
            squiggle: spec.Target ?? SquiggleAt.Identifier);
    }

    private static Location? FirstLocation(INamedTypeSymbol symbol)
    {
        var locations = symbol.Locations;
        return locations.Length == 0 ? null : locations[0];
    }
}
