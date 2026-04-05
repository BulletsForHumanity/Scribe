using System.Threading;
using Microsoft.CodeAnalysis;

namespace Scribe;

/// <summary>
///     Walks namespace trees recursively, visiting every type (including nested types).
///     Eliminates the duplicated recursive walk across compilation-based generators.
/// </summary>
public static class NamespaceWalker
{
    /// <summary>
    ///     Visitor callback. Invoked for every <see cref="INamedTypeSymbol"/> in the namespace tree,
    ///     including nested types.
    /// </summary>
    public delegate void TypeVisitor(INamedTypeSymbol type);

    /// <summary>Walk a single namespace tree (one assembly's global namespace).</summary>
    public static void Walk(INamespaceSymbol root, TypeVisitor visitor, CancellationToken ct)
    {
        foreach (var member in root.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            switch (member)
            {
                case INamespaceSymbol child:
                    Walk(child, visitor, ct);
                    break;
                case INamedTypeSymbol type:
                    visitor(type);
                    foreach (var nested in type.GetTypeMembers())
                    {
                        visitor(nested);
                    }

                    break;
            }
        }
    }

    /// <summary>
    ///     Walk all referenced assemblies <b>and</b> the current compilation's assembly.
    ///     This is the standard pattern for compilation-based generators that discover types
    ///     across the entire dependency graph.
    /// </summary>
    public static void WalkCompilation(
        Compilation compilation,
        TypeVisitor visitor,
        CancellationToken ct
    )
    {
        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
            {
                Walk(asm.GlobalNamespace, visitor, ct);
            }
        }

        Walk(compilation.Assembly.GlobalNamespace, visitor, ct);
    }

    /// <summary>Walk only the current assembly's namespace tree (not references).</summary>
    public static void WalkLocalAssembly(
        Compilation compilation,
        TypeVisitor visitor,
        CancellationToken ct
    )
    {
        Walk(compilation.Assembly.GlobalNamespace, visitor, ct);
    }
}
