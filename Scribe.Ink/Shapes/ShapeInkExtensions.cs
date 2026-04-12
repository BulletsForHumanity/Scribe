using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Scribe.Shapes;

namespace Scribe.Ink.Shapes;

/// <summary>
///     Entry points that project a <see cref="Shape{TModel}"/> into the Roslyn
///     analyzer/fixer surface. Lives in <c>Scribe.Ink</c> so Scribe core stays
///     free of the <c>Microsoft.CodeAnalysis.CSharp.Workspaces</c> dependency.
/// </summary>
public static class ShapeInkExtensions
{
    /// <summary>
    ///     Return a <see cref="CodeFixProvider"/> that offers code fixes for the
    ///     diagnostics emitted by this shape's analyzer. Package the returned
    ///     instance in a concrete <c>[ExportCodeFixProvider]</c>-attributed class
    ///     that delegates its members for deployment.
    /// </summary>
    public static CodeFixProvider ToFixProvider<TModel>(this Shape<TModel> shape)
        where TModel : IEquatable<TModel>
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in shape.CheckList)
        {
            ids.Add(check.Id);
        }

        foreach (var check in shape.MemberCheckList)
        {
            ids.Add(check.Id);
        }

        return new ShapeCodeFixProvider(ids.ToImmutableArray(), shape.TryGetCustomFix);
    }

    /// <summary>
    ///     Register a <see cref="IShapeCustomFix"/> handler under <paramref name="tag"/>.
    ///     Diagnostics whose <c>fixKind</c> is <see cref="FixKind.Custom"/> and whose
    ///     <c>customFixTag</c> property matches <paramref name="tag"/> will be routed
    ///     to <paramref name="fix"/> by the provider returned from
    ///     <see cref="ToFixProvider{TModel}"/>.
    /// </summary>
    public static Shape<TModel> WithCustomFix<TModel>(
        this Shape<TModel> shape, string tag, IShapeCustomFix fix)
        where TModel : IEquatable<TModel>
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        shape.RegisterCustomFix(tag, fix ?? throw new ArgumentNullException(nameof(fix)));
        return shape;
    }
}
