using System;
using System.Collections.Generic;

namespace Scribe.Shapes;

/// <summary>
///     Authoring-time builder for a focus-stream chain. Parallel to
///     <see cref="TypeShape"/> but parametrised by any cache-safe focus type
///     produced by a <see cref="Lens{TSource, TTarget}"/>. Accumulates leaf checks
///     that apply at the focus and nested lens branches that navigate deeper.
/// </summary>
/// <typeparam name="TFocus">The focus this shape operates on. Must be equatable for cache correctness.</typeparam>
/// <remarks>
///     <para>
///         A <see cref="FocusShape{TFocus}"/> is obtained by calling a lens method on
///         its parent authoring type — e.g. <see cref="TypeShape"/>'s
///         <c>Attributes(fqn)</c> entry point returns a
///         <see cref="FocusShape{AttributeFocus}"/>. Further lens calls return
///         additional nested <see cref="FocusShape{TFocus}"/> instances, one per hop.
///     </para>
///     <para>
///         Leaf checks and nested branches are accumulated in declaration order and
///         flattened into a single evaluation tree at <see cref="TypeShape.Etch{TModel}"/>
///         time. The parent <see cref="Shape{TModel}"/> walks the tree during
///         materialisation and emits <see cref="Scribe.Cache.DiagnosticInfo"/> for each
///         violation.
///     </para>
/// </remarks>
public sealed class FocusShape<TFocus>
    where TFocus : IEquatable<TFocus>
{
    private readonly List<FocusCheck<TFocus>> _checks = new();
    private readonly List<ILensBranch<TFocus>> _branches = new();

    internal FocusShape()
    {
    }

    internal IReadOnlyList<FocusCheck<TFocus>> Checks => _checks;
    internal IReadOnlyList<ILensBranch<TFocus>> Branches => _branches;

    internal FocusShape<TFocus> AddCheck(FocusCheck<TFocus> check)
    {
        if (check is null)
        {
            throw new ArgumentNullException(nameof(check));
        }

        _checks.Add(check);
        return this;
    }

    internal FocusShape<TFocus> AddBranch(ILensBranch<TFocus> branch)
    {
        if (branch is null)
        {
            throw new ArgumentNullException(nameof(branch));
        }

        _branches.Add(branch);
        return this;
    }
}
