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
    private readonly string? _primaryAttributeMetadataName;
    private readonly ProjectionDelegate<TModel> _project;

    internal Shape(
        TypeKindFilter kind,
        ShapeCheck[] checks,
        string? primaryAttributeMetadataName,
        ProjectionDelegate<TModel> project)
    {
        _kind = kind;
        _checks = checks;
        _primaryAttributeMetadataName = primaryAttributeMetadataName;
        _project = project;
    }

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
        if (_checks.Length == 0)
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

        return violations is null
            ? EquatableArray<DiagnosticInfo>.Empty
            : EquatableArray.From<DiagnosticInfo>(violations);
    }

    private static Location? FirstLocation(INamedTypeSymbol symbol)
    {
        var locations = symbol.Locations;
        return locations.Length == 0 ? null : locations[0];
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
