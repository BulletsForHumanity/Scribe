using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Attributes;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     A sealed, etched Shape typed with its model <typeparamref name="TModel"/>. Produced
///     by <see cref="TypeShape.Etch{TModel}"/>. Materialises into an analyzer, a generator
///     provider, or a code-fixer via <see cref="ToProvider"/> / <c>ToAnalyzer</c> /
///     <c>ToInk</c>.
/// </summary>
/// <typeparam name="TModel">User model type. Must be <see cref="IEquatable{T}"/> for cache correctness.</typeparam>
public sealed partial class Shape<TModel> where TModel : IEquatable<TModel>
{
    private readonly TypeKindFilter _kind;
    private readonly ShapeCheck[] _checks;
    private readonly MemberCheck[] _memberChecks;
    private readonly ILensBranch<TypeFocus>[] _lensBranches;
    private readonly string? _primaryAttributeMetadataName;
    private readonly string? _primaryInterfaceMetadataName;
    private readonly EtchDelegate<TModel> _etch;
    private readonly OneOfBranch[]? _alternatives;
    private readonly LensQuantifierSpec? _fusionSpec;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _customFixes =
        new(StringComparer.Ordinal);

    internal Shape(
        TypeKindFilter kind,
        ShapeCheck[] checks,
        MemberCheck[] memberChecks,
        ILensBranch<TypeFocus>[] lensBranches,
        string? primaryAttributeMetadataName,
        string? primaryInterfaceMetadataName,
        EtchDelegate<TModel> etch)
    {
        _kind = kind;
        _checks = checks;
        _memberChecks = memberChecks;
        _lensBranches = lensBranches;
        _primaryAttributeMetadataName = primaryAttributeMetadataName;
        _primaryInterfaceMetadataName = primaryInterfaceMetadataName;
        _etch = etch;
        _alternatives = null;
        _fusionSpec = null;
    }

    internal Shape(
        OneOfBranch[] alternatives,
        string? primaryAttributeMetadataName,
        string? primaryInterfaceMetadataName,
        LensQuantifierSpec fusionSpec,
        EtchDelegate<TModel> etch)
    {
        _kind = TypeKindFilter.Any;
        _checks = Array.Empty<ShapeCheck>();
        _memberChecks = Array.Empty<MemberCheck>();
        _lensBranches = Array.Empty<ILensBranch<TypeFocus>>();
        _primaryAttributeMetadataName = primaryAttributeMetadataName;
        _primaryInterfaceMetadataName = primaryInterfaceMetadataName;
        _etch = etch;
        _alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
        _fusionSpec = fusionSpec ?? throw new ArgumentNullException(nameof(fusionSpec));
    }

    internal bool IsOneOf => _alternatives is not null;
    internal OneOfBranch[]? Alternatives => _alternatives;
    internal LensQuantifierSpec? FusionSpec => _fusionSpec;

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
        if (_alternatives is not null)
        {
            foreach (var alt in _alternatives)
            {
                if (MatchesNodeKindSingle(alt.Kind, node))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchesNodeKindSingle(_kind, node);
    }

    private static bool MatchesNodeKindSingle(TypeKindFilter kind, SyntaxNode node) =>
        kind switch
        {
            TypeKindFilter.Any => node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax,
            _ => KindMatcher.Matches(kind, node),
        };

    private bool MatchesSymbolKind(INamedTypeSymbol symbol)
    {
        if (_alternatives is not null)
        {
            foreach (var alt in _alternatives)
            {
                if (MatchesSymbolKindSingle(alt.Kind, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchesSymbolKindSingle(_kind, symbol);
    }

    internal static bool MatchesSymbolKindSingle(TypeKindFilter kind, INamedTypeSymbol symbol) =>
        kind switch
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

        var etchContext = new ShapeEtchContext(
            symbol, attributeReader, semanticModel, compilation, ct);
        var model = _etch(in etchContext);

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
        if (_alternatives is not null)
        {
            return RunOneOfChecks(symbol, compilation, ct);
        }

        if (_checks.Length == 0 && _memberChecks.Length == 0 && _lensBranches.Length == 0)
        {
            return EquatableArray<DiagnosticInfo>.Empty;
        }

        List<DiagnosticInfo>? violations = null;
        TypeFocus? typeFocus = null;
        if (_checks.Length > 0 || _lensBranches.Length > 0)
        {
            typeFocus = new TypeFocus(
                symbol: symbol,
                fqn: InternPool.Intern(symbol.ToDisplayString()),
                origin: LocationInfo.From(FirstLocation(symbol)));
        }

        foreach (var check in _checks)
        {
            ct.ThrowIfCancellationRequested();
            if (check.Predicate(typeFocus!.Value, compilation, ct))
            {
                continue;
            }

            violations ??= new List<DiagnosticInfo>();
            violations.Add(new DiagnosticInfo(
                Id: check.Id,
                Severity: check.Severity,
                MessageArgs: check.MessageArgs(typeFocus!.Value),
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

        if (_lensBranches.Length > 0)
        {
            foreach (var branch in _lensBranches)
            {
                violations ??= new List<DiagnosticInfo>();
                branch.Evaluate(typeFocus!.Value, compilation, ct, violations);
            }
        }

        return violations is null
            ? EquatableArray<DiagnosticInfo>.Empty
            : EquatableArray.From<DiagnosticInfo>(violations);
    }

    private EquatableArray<DiagnosticInfo> RunOneOfChecks(
        INamedTypeSymbol symbol, Compilation compilation, CancellationToken ct)
    {
        var alternatives = _alternatives!;
        var typeFocus = new TypeFocus(
            symbol: symbol,
            fqn: InternPool.Intern(symbol.ToDisplayString()),
            origin: LocationInfo.From(FirstLocation(symbol)));

        var perBranchViolations = new List<DiagnosticInfo>[alternatives.Length];

        for (var i = 0; i < alternatives.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var alt = alternatives[i];
            var scratch = new List<DiagnosticInfo>();

            if (!MatchesSymbolKindSingle(alt.Kind, symbol))
            {
                // Kind mismatch is itself a branch failure; synthesise a marker so fusion
                // can surface the reason "this branch didn't match your symbol kind".
                scratch.Add(new DiagnosticInfo(
                    Id: "SCRIBE101",
                    Severity: DiagnosticSeverity.Info,
                    MessageArgs: EquatableArray.Create(alt.Kind.ToString()),
                    Location: typeFocus.Origin));
                perBranchViolations[i] = scratch;
                continue;
            }

            foreach (var check in alt.Checks)
            {
                ct.ThrowIfCancellationRequested();
                if (check.Predicate(typeFocus, compilation, ct))
                {
                    continue;
                }

                scratch.Add(new DiagnosticInfo(
                    Id: check.Id,
                    Severity: check.Severity,
                    MessageArgs: check.MessageArgs(typeFocus),
                    Location: typeFocus.Origin));
            }

            foreach (var branch in alt.LensBranches)
            {
                branch.Evaluate(typeFocus, compilation, ct, scratch);
            }

            perBranchViolations[i] = scratch;
        }

        // Any branch with zero violations is a pass — overall silent.
        for (var i = 0; i < perBranchViolations.Length; i++)
        {
            if (perBranchViolations[i].Count == 0)
            {
                return EquatableArray<DiagnosticInfo>.Empty;
            }
        }

        var summary = FormatFusionSummary(perBranchViolations);
        var fusion = new DiagnosticInfo(
            Id: _fusionSpec!.Id,
            Severity: _fusionSpec.Severity,
            MessageArgs: EquatableArray.Create(
                alternatives.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                summary),
            Location: typeFocus.Origin);

        return EquatableArray.From<DiagnosticInfo>(new[] { fusion });
    }

    private static string FormatFusionSummary(List<DiagnosticInfo>[] perBranchViolations)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < perBranchViolations.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("  —OR—  ");
            }

            builder.Append('[');
            builder.Append("branch").Append(i + 1).Append(": ");
            var branch = perBranchViolations[i];
            for (var j = 0; j < branch.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(branch[j].Id);
            }

            builder.Append(']');
        }

        return builder.ToString();
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
