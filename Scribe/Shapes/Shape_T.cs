using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Attributes;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     A sealed shape typed with its projection <typeparamref name="TModel"/>. Produced
///     by <see cref="ShapeBuilder.Project{TModel}"/>. In Phase 3 only
///     <see cref="ToProvider"/> is exposed; <c>ToAnalyzer</c> and <c>ToFixProvider</c>
///     ship in Phase 4.
/// </summary>
/// <typeparam name="TModel">User projection type. Must be <see cref="IEquatable{T}"/> for cache correctness.</typeparam>
public sealed partial class Shape<TModel> where TModel : IEquatable<TModel>
{
    private readonly TypeKindFilter _kind;
    private readonly ShapeCheck[] _checks;
    private readonly MemberCheck[] _memberChecks;
    private readonly string? _primaryAttributeMetadataName;
    private readonly string? _primaryInterfaceMetadataName;
    private readonly ProjectionDelegate<TModel> _project;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _customFixes =
        new(StringComparer.Ordinal);

    internal Shape(
        TypeKindFilter kind,
        ShapeCheck[] checks,
        MemberCheck[] memberChecks,
        string? primaryAttributeMetadataName,
        string? primaryInterfaceMetadataName,
        ProjectionDelegate<TModel> project)
    {
        _kind = kind;
        _checks = checks;
        _memberChecks = memberChecks;
        _primaryAttributeMetadataName = primaryAttributeMetadataName;
        _primaryInterfaceMetadataName = primaryInterfaceMetadataName;
        _project = project;
    }

    /// <summary>
    ///     Register a custom fix handler under <paramref name="tag"/>. The handler's
    ///     runtime type is opaque to Scribe core — the Ink extension layer
    ///     (<c>Scribe.Ink.Shapes.ShapeInkExtensions.WithCustomFix</c>) supplies the
    ///     concrete delegate / <c>IShapeCustomFix</c> shape and casts back at dispatch
    ///     time. Checks declared with <see cref="FixKind.Custom"/> and a matching
    ///     <c>customFixTag</c> route through this registry.
    /// </summary>
    internal void RegisterCustomFix(string tag, object handler)
    {
        if (string.IsNullOrEmpty(tag))
        {
            throw new ArgumentException("Custom fix tag must not be empty.", nameof(tag));
        }

        _customFixes[tag] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    ///     Look up a handler previously registered via <see cref="RegisterCustomFix"/>.
    ///     Returns <see langword="null"/> when no handler is registered for <paramref name="tag"/>.
    /// </summary>
    internal object? TryGetCustomFix(string tag) =>
        _customFixes.TryGetValue(tag, out var handler) ? handler : null;

    /// <summary>
    ///     Produce an <see cref="IncrementalValuesProvider{TValues}"/> of
    ///     <see cref="ShapedSymbol{TModel}"/> — one per type in the compilation that
    ///     matches the shape's kind filter and (if declared) carries the driving
    ///     attribute. Violations of declared checks are carried on each result as
    ///     <see cref="ShapedSymbol{TModel}.Violations"/>.
    /// </summary>
    public IncrementalValuesProvider<ShapedSymbol<TModel>> ToProvider(
        IncrementalGeneratorInitializationContext context)
    {
        if (_primaryAttributeMetadataName is not null)
        {
            return context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    _primaryAttributeMetadataName,
                    predicate: (node, _) => MatchesNodeKind(node),
                    transform: TransformWithAttribute)
                .Where(static s => s.HasValue)
                .Select(static (s, _) => s!.Value);
        }

        return context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => MatchesNodeKind(node),
                transform: TransformFromSyntax)
            .Where(static s => s.HasValue)
            .Select(static (s, _) => s!.Value);
    }

    private ShapedSymbol<TModel>? TransformWithAttribute(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        return BuildResult(type, ctx.SemanticModel, ct);
    }

    private ShapedSymbol<TModel>? TransformFromSyntax(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct);
        if (symbol is not INamedTypeSymbol type)
        {
            return null;
        }

        return BuildResult(type, ctx.SemanticModel, ct);
    }

    private bool MatchesNodeKind(SyntaxNode node)
    {
        // Delegate to the builder's kind matcher by reconstructing a lightweight check.
        return _kind switch
        {
            TypeKindFilter.Any => node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax,
            _ => KindMatcher.Matches(_kind, node),
        };
    }

    private bool MatchesSymbolKind(INamedTypeSymbol symbol) =>
        _kind switch
        {
            TypeKindFilter.Class => symbol.TypeKind == TypeKind.Class && !symbol.IsRecord,
            TypeKindFilter.Record => symbol.TypeKind == TypeKind.Class && symbol.IsRecord,
            TypeKindFilter.RecordStruct => symbol.TypeKind == TypeKind.Struct && symbol.IsRecord,
            TypeKindFilter.Struct => symbol.TypeKind == TypeKind.Struct && !symbol.IsRecord,
            TypeKindFilter.Interface => symbol.TypeKind == TypeKind.Interface,
            TypeKindFilter.Any => symbol.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface,
            _ => false,
        };

    private ShapedSymbol<TModel>? BuildResult(
        INamedTypeSymbol symbol, SemanticModel semanticModel, CancellationToken ct)
    {
        if (!MatchesSymbolKind(symbol))
        {
            return null;
        }

        var compilation = semanticModel.Compilation;

        if (_primaryInterfaceMetadataName is { } ifaceName
            && !ImplementsInterface(symbol, compilation, ifaceName))
        {
            return null;
        }

        var violations = RunChecks(symbol, compilation, ct);

        var attributeReader = _primaryAttributeMetadataName is not null
            ? AttributeSchema.For(symbol, _primaryAttributeMetadataName)
            : default;

        var projectionContext = new ShapeProjectionContext(
            symbol, attributeReader, semanticModel, compilation, ct);
        var model = _project(in projectionContext);

        var location = LocationInfo.From(FirstLocation(symbol));

        return new ShapedSymbol<TModel>(
            Fqn: InternPool.Intern(symbol.ToDisplayString()),
            Model: model,
            Location: location,
            Violations: violations);
    }

    private EquatableArray<DiagnosticInfo> RunChecks(
        INamedTypeSymbol symbol, Compilation compilation, CancellationToken ct)
    {
        if (_checks.Length == 0 && _memberChecks.Length == 0)
        {
            return EquatableArray<DiagnosticInfo>.Empty;
        }

        List<DiagnosticInfo>? violations = null;
        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            if (check.Predicate(symbol, compilation, ct))
            {
                continue;
            }

            violations ??= new List<DiagnosticInfo>();
            violations.Add(new DiagnosticInfo(
                Id: check.Id,
                Severity: check.Severity,
                MessageArgs: check.MessageArgs(symbol),
                Location: LocationInfo.From(FirstLocation(symbol))));
        }

        if (_memberChecks.Length > 0)
        {
            foreach (var member in EnumerateDeclaredMembers(symbol))
            {
                foreach (var check in _memberChecks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!check.Match(member))
                    {
                        continue;
                    }

                    violations ??= new List<DiagnosticInfo>();
                    violations.Add(new DiagnosticInfo(
                        Id: check.Id,
                        Severity: check.Severity,
                        MessageArgs: check.MessageArgs(symbol, member),
                        Location: LocationInfo.From(MemberFirstLocation(member))));
                }
            }
        }

        return violations is null
            ? EquatableArray<DiagnosticInfo>.Empty
            : EquatableArray.From<DiagnosticInfo>(violations);
    }

    internal static IEnumerable<ISymbol> EnumerateDeclaredMembers(INamedTypeSymbol type)
    {
        // Stable source-order iteration: sort by first declaring syntax span.
        // Implicitly-declared and synthesized members are filtered.
        var members = new List<ISymbol>(type.GetMembers().Length);
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            if (member.DeclaringSyntaxReferences.Length == 0)
            {
                continue;
            }

            members.Add(member);
        }

        members.Sort(MemberSpanComparer.Instance);
        return members;
    }

    private static Location? MemberFirstLocation(ISymbol member)
    {
        var locations = member.Locations;
        return locations.Length == 0 ? null : locations[0];
    }

    private sealed class MemberSpanComparer : IComparer<ISymbol>
    {
        public static readonly MemberSpanComparer Instance = new();

        public int Compare(ISymbol? x, ISymbol? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var xRef = x.DeclaringSyntaxReferences.Length > 0 ? x.DeclaringSyntaxReferences[0] : null;
            var yRef = y.DeclaringSyntaxReferences.Length > 0 ? y.DeclaringSyntaxReferences[0] : null;
            if (xRef is null && yRef is null)
            {
                return string.CompareOrdinal(x.Name, y.Name);
            }

            if (xRef is null)
            {
                return -1;
            }

            if (yRef is null)
            {
                return 1;
            }

            var fileCmp = string.CompareOrdinal(xRef.SyntaxTree.FilePath, yRef.SyntaxTree.FilePath);
            if (fileCmp != 0)
            {
                return fileCmp;
            }

            return xRef.Span.Start.CompareTo(yRef.Span.Start);
        }
    }

    private static Location? FirstLocation(INamedTypeSymbol symbol)
    {
        var locations = symbol.Locations;
        return locations.Length == 0 ? null : locations[0];
    }

    internal static bool ImplementsInterface(
        INamedTypeSymbol symbol, Compilation compilation, string metadataName)
    {
        var target = compilation.GetTypeByMetadataName(metadataName);
        if (target is null)
        {
            return false;
        }

        foreach (var iface in symbol.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, target))
            {
                return true;
            }
        }

        return false;
    }

    private static class KindMatcher
    {
        public static bool Matches(TypeKindFilter kind, SyntaxNode node)
        {
            if (node is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax)
            {
                return false;
            }

            return kind switch
            {
                TypeKindFilter.Class =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                TypeKindFilter.Record =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax r
                    && r.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordDeclaration),
                TypeKindFilter.RecordStruct =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax rs
                    && rs.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordStructDeclaration),
                TypeKindFilter.Struct =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax,
                TypeKindFilter.Interface =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax,
                _ => false,
            };
        }
    }
}
