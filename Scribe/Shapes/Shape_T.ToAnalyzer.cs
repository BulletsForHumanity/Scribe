using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scribe.Shapes;

/// <summary>
///     Triple-lowering: project the same declarative shape into a
///     <see cref="DiagnosticAnalyzer"/>. Each registered check becomes a
///     <see cref="DiagnosticDescriptor"/>; on each <see cref="SymbolKind.NamedType"/>
///     the analyser runs the same predicates the generator pipeline runs and emits
///     <see cref="Diagnostic"/>s at each check's <see cref="SquiggleAt"/>.
/// </summary>
public sealed partial class Shape<TModel>
{
    private DiagnosticAnalyzer? _cachedAnalyzer;
    private ImmutableArray<DiagnosticDescriptor> _cachedDescriptors;
    private readonly ConcurrentDictionary<string, DiagnosticDescriptor> _descriptorsById = new();

    /// <summary>
    ///     Return a <see cref="DiagnosticAnalyzer"/> that reports this shape's checks
    ///     against every matching type in a compilation. Package the returned instance
    ///     inside a concrete <c>[DiagnosticAnalyzer]</c>-attributed wrapper class for
    ///     deployment as an analyzer.
    /// </summary>
    public DiagnosticAnalyzer ToAnalyzer() =>
        _cachedAnalyzer ??= new ShapeDiagnosticAnalyzer(this);

    internal ImmutableArray<DiagnosticDescriptor> SupportedDescriptors
    {
        get
        {
            if (_cachedDescriptors.IsDefault)
            {
                var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>(_checks.Length);
                foreach (var check in _checks)
                {
                    builder.Add(DescriptorFor(check));
                }

                _cachedDescriptors = builder.ToImmutable();
            }

            return _cachedDescriptors;
        }
    }

    internal DiagnosticDescriptor DescriptorFor(ShapeCheck check)
    {
        return _descriptorsById.GetOrAdd(check.Id, _ => new DiagnosticDescriptor(
            id: check.Id,
            title: check.Title,
            messageFormat: check.MessageFormat,
            category: "Scribe.Shape",
            defaultSeverity: check.Severity,
            isEnabledByDefault: true));
    }

    internal TypeKindFilter KindFilter => _kind;
    internal string? PrimaryAttributeName => _primaryAttributeMetadataName;
    internal ShapeCheck[] CheckList => _checks;

    // RS1001 flags DiagnosticAnalyzer subclasses missing [DiagnosticAnalyzer]. This nested
    // class is intentionally unattributed: as a nested type of an open generic it would
    // throw CS8032 at consumer analyzer load if Roslyn tried to Activator.CreateInstance
    // it. Consumers are expected to wrap the ToAnalyzer() instance in their own concrete,
    // attributed subclass (see class-level XML doc).
#pragma warning disable RS1001
    private sealed class ShapeDiagnosticAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1001
    {
        private readonly Shape<TModel> _shape;

        public ShapeDiagnosticAnalyzer(Shape<TModel> shape) => _shape = shape;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => _shape.SupportedDescriptors;

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private void AnalyzeNamedType(SymbolAnalysisContext ctx)
        {
            if (ctx.Symbol is not INamedTypeSymbol type)
            {
                return;
            }

            if (!MatchesKind(_shape.KindFilter, type))
            {
                return;
            }

            if (_shape.PrimaryAttributeName is { } attrName
                && !HasAttribute(type, attrName))
            {
                // Shape drives off an attribute the type doesn't carry — silently skip.
                return;
            }

            foreach (var check in _shape.CheckList)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                if (check.Predicate(type, ctx.Compilation, ctx.CancellationToken))
                {
                    continue;
                }

                var descriptor = _shape.DescriptorFor(check);
                var location = SquiggleLocator.Resolve(type, check.SquiggleAt, ctx.CancellationToken);
                var properties = BuildProperties(check, type);
                var args = check.MessageArgs(type);
                var objArgs = new object?[args.Count];
                for (var i = 0; i < args.Count; i++)
                {
                    objArgs[i] = args[i];
                }

                ctx.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    location,
                    properties,
                    objArgs));
            }
        }

        private static ImmutableDictionary<string, string?> BuildProperties(
            ShapeCheck check, INamedTypeSymbol symbol)
        {
            var props = check.FixProperties?.Invoke(symbol)
                ?? ImmutableDictionary<string, string?>.Empty;

            return props
                .SetItem("fixKind", check.FixKind.ToString())
                .SetItem("squiggleAt", check.SquiggleAt.ToString());
        }

        private static bool MatchesKind(TypeKindFilter filter, INamedTypeSymbol symbol) =>
            filter switch
            {
                TypeKindFilter.Class => symbol.TypeKind == TypeKind.Class && !symbol.IsRecord,
                TypeKindFilter.Record => symbol.TypeKind == TypeKind.Class && symbol.IsRecord,
                TypeKindFilter.RecordStruct => symbol.TypeKind == TypeKind.Struct && symbol.IsRecord,
                TypeKindFilter.Struct => symbol.TypeKind == TypeKind.Struct && !symbol.IsRecord,
                TypeKindFilter.Interface => symbol.TypeKind == TypeKind.Interface,
                TypeKindFilter.Any => symbol.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface,
                _ => false,
            };

        private static bool HasAttribute(INamedTypeSymbol symbol, string metadataName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var fqn = attribute.AttributeClass?.ToDisplayString();
                if (fqn is null)
                {
                    continue;
                }

                if (string.Equals(fqn, metadataName, StringComparison.Ordinal))
                {
                    return true;
                }

                var bracket = fqn.IndexOf('<');
                var bare = bracket > 0 ? fqn.Substring(0, bracket) : fqn;
                if (string.Equals(bare, metadataName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
