using System;
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

        var builder = ImmutableArray.CreateBuilder<string>(shape.CheckList.Length);
        foreach (var check in shape.CheckList)
        {
            builder.Add(check.Id);
        }

        return new ShapeCodeFixProvider(builder.ToImmutable());
    }
}
