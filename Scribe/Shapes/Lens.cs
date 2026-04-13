using System;
using System.Collections.Generic;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Structural refocus primitive. A <see cref="Lens{TSource, TTarget}"/> redirects
///     attention from one focus (<typeparamref name="TSource"/>) to another
///     (<typeparamref name="TTarget"/>) along an edge the symbol graph already provides —
///     attributes, type arguments, constructor arguments, members, base types.
/// </summary>
/// <typeparam name="TSource">The focus type the lens reads from.</typeparam>
/// <typeparam name="TTarget">The focus type the lens produces.</typeparam>
/// <remarks>
///     <para>
///         A Lens is a <c>SelectMany</c>: one source focus can produce zero or more
///         target foci. Lenses chain to reach deep positions, each refocusing the
///         view. A query like <em>"for every type implementing X, for every
///         <c>[Foo&lt;T&gt;]</c> attribute, T must implement Y"</em> is three Lens hops.
///     </para>
///     <para>
///         Every lens carries a <c>Smudge</c> — a location-propagation function that
///         resolves the new focus's source span so a violation reported three hops
///         deep squiggles at the right character, not at the root type.
///     </para>
///     <para>
///         Target focus types must themselves be equatable for the incremental cache
///         to stay correct. The lens does not enforce this — it is a discipline the
///         focus authors follow (see <see cref="TypeFocus"/>).
///     </para>
/// </remarks>
public sealed class Lens<TSource, TTarget>
{
    /// <summary>
    ///     Navigate from one source focus to the set of target foci it refocuses to.
    ///     An empty result means "this lens does not apply here" — a zero-hit lens
    ///     silently passes through sub-predicates.
    /// </summary>
    public Func<TSource, IEnumerable<TTarget>> Navigate { get; }

    /// <summary>
    ///     Resolve the source span of a target focus. Called when a violation reported
    ///     downstream needs to surface at the newly-focused position rather than at the
    ///     source focus. The returned <see cref="LocationInfo"/> is cache-safe and
    ///     equatable; raw Roslyn <c>Location</c> values must not be carried here.
    /// </summary>
    public Func<TTarget, LocationInfo?> Smudge { get; }

    public Lens(
        Func<TSource, IEnumerable<TTarget>> navigate,
        Func<TTarget, LocationInfo?> smudge)
    {
        Navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        Smudge = smudge ?? throw new ArgumentNullException(nameof(smudge));
    }

    /// <summary>
    ///     Compose this lens with a second lens to form a deeper hop. The resulting
    ///     lens's <see cref="Smudge"/> resolves at the deepest target — that is where
    ///     violations at the end of the chain should squiggle.
    /// </summary>
    public Lens<TSource, TNext> Then<TNext>(Lens<TTarget, TNext> next)
    {
        if (next is null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        var outer = this;
        return new Lens<TSource, TNext>(
            navigate: source =>
            {
                var results = new List<TNext>();
                foreach (var mid in outer.Navigate(source))
                {
                    foreach (var target in next.Navigate(mid))
                    {
                        results.Add(target);
                    }
                }

                return results;
            },
            smudge: next.Smudge);
    }
}
