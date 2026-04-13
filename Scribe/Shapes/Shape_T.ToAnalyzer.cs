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
                var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>();

                if (_alternatives is not null)
                {
                    // Fusion diagnostic + every branch's descriptors (so SuppressMessage /
                    // ruleset authoring still resolves each individual Id even though the
                    // fused message is what gets emitted).
                    var fusion = _fusionSpec!;
                    builder.Add(_descriptorsById.GetOrAdd(fusion.Id, _ => new DiagnosticDescriptor(
                        id: fusion.Id,
                        title: fusion.Title,
                        messageFormat: fusion.MessageFormat,
                        category: "Scribe.Shape",
                        defaultSeverity: fusion.Severity,
                        isEnabledByDefault: true)));

                    builder.Add(_descriptorsById.GetOrAdd("SCRIBE101", _ => new DiagnosticDescriptor(
                        id: "SCRIBE101",
                        title: "OneOf branch kind mismatch",
                        messageFormat: "Branch expected type-kind filter '{0}'; symbol does not match.",
                        category: "Scribe.Shape",
                        defaultSeverity: DiagnosticSeverity.Info,
                        isEnabledByDefault: true)));

                    foreach (var alt in _alternatives)
                    {
                        foreach (var check in alt.Checks)
                        {
                            builder.Add(DescriptorFor(check));
                        }

                        foreach (var branch in alt.LensBranches)
                        {
                            foreach (var (id, title, messageFormat, severity) in branch.CollectDescriptors())
                            {
                                builder.Add(_descriptorsById.GetOrAdd(id, _ => new DiagnosticDescriptor(
                                    id: id,
                                    title: title,
                                    messageFormat: messageFormat,
                                    category: "Scribe.Shape",
                                    defaultSeverity: severity,
                                    isEnabledByDefault: true)));
                            }
                        }
                    }

                    _cachedDescriptors = builder.ToImmutable();
                    return _cachedDescriptors;
                }

                foreach (var check in _checks)
                {
                    builder.Add(DescriptorFor(check));
                }

                foreach (var check in _memberChecks)
                {
                    builder.Add(DescriptorFor(check));
                }

                foreach (var branch in _lensBranches)
                {
                    foreach (var (id, title, messageFormat, severity) in branch.CollectDescriptors())
                    {
                        builder.Add(_descriptorsById.GetOrAdd(id, _ => new DiagnosticDescriptor(
                            id: id,
                            title: title,
                            messageFormat: messageFormat,
                            category: "Scribe.Shape",
                            defaultSeverity: severity,
                            isEnabledByDefault: true)));
                    }
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

    internal DiagnosticDescriptor DescriptorFor(MemberCheck check)
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
    internal string? PrimaryInterfaceName => _primaryInterfaceMetadataName;
    internal ShapeCheck[] CheckList => _checks;
    internal MemberCheck[] MemberCheckList => _memberChecks;
    internal ILensBranch<TypeFocus>[] LensBranchList => _lensBranches;
    internal ConcurrentDictionary<string, DiagnosticDescriptor> DescriptorsById => _descriptorsById;

    internal static Location? FirstLocationOf(INamedTypeSymbol symbol) => FirstLocation(symbol);

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

            if (_shape.IsOneOf)
            {
                AnalyzeOneOf(ctx, type);
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

            if (_shape.PrimaryInterfaceName is { } ifaceName
                && !Shape<TModel>.ImplementsInterface(type, ctx.Compilation, ifaceName))
            {
                // Shape drives off an interface the type doesn't implement — silently skip.
                return;
            }

            TypeFocus? typeFocus = null;
            if (_shape.CheckList.Length > 0 || _shape.LensBranchList.Length > 0)
            {
                typeFocus = new TypeFocus(
                    symbol: type,
                    fqn: Scribe.Cache.InternPool.Intern(type.ToDisplayString()),
                    origin: Scribe.Cache.LocationInfo.From(Shape<TModel>.FirstLocationOf(type)));
            }

            foreach (var check in _shape.CheckList)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                if (check.Predicate(typeFocus!.Value, ctx.Compilation, ctx.CancellationToken))
                {
                    continue;
                }

                var descriptor = _shape.DescriptorFor(check);
                var location = SquiggleLocator.Resolve(type, check.SquiggleAt, ctx.CancellationToken);
                var properties = BuildProperties(check, typeFocus!.Value);
                var args = check.MessageArgs(typeFocus!.Value);
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

            if (_shape.LensBranchList.Length > 0)
            {
                var branchViolations = new List<Scribe.Cache.DiagnosticInfo>();
                foreach (var branch in _shape.LensBranchList)
                {
                    branch.Evaluate(typeFocus!.Value, ctx.Compilation, ctx.CancellationToken, branchViolations);
                }

                foreach (var violation in branchViolations)
                {
                    if (_shape.DescriptorsById.TryGetValue(violation.Id, out var descriptor))
                    {
                        ctx.ReportDiagnostic(violation.Materialize(descriptor));
                    }
                }
            }

            if (_shape.MemberCheckList.Length > 0)
            {
                foreach (var member in EnumerateDeclaredMembers(type))
                {
                    foreach (var check in _shape.MemberCheckList)
                    {
                        ctx.CancellationToken.ThrowIfCancellationRequested();
                        if (!check.Match(member))
                        {
                            continue;
                        }

                        var descriptor = _shape.DescriptorFor(check);
                        var location = MemberSquiggleLocator.Resolve(
                            member, check.SquiggleAt, ctx.CancellationToken);
                        var properties = BuildMemberProperties(check, type, member);
                        var args = check.MessageArgs(type, member);
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
            }
        }

        private void AnalyzeOneOf(SymbolAnalysisContext ctx, INamedTypeSymbol type)
        {
            // Overall kind filter: at least one alternative's kind must match.
            var anyKindMatch = false;
            foreach (var alt in _shape.Alternatives!)
            {
                if (Shape<TModel>.MatchesSymbolKindSingle(alt.Kind, type))
                {
                    anyKindMatch = true;
                    break;
                }
            }

            if (!anyKindMatch)
            {
                return;
            }

            if (_shape.PrimaryAttributeName is { } attrName
                && !HasAttribute(type, attrName))
            {
                return;
            }

            if (_shape.PrimaryInterfaceName is { } ifaceName
                && !Shape<TModel>.ImplementsInterface(type, ctx.Compilation, ifaceName))
            {
                return;
            }

            var typeFocus = new TypeFocus(
                symbol: type,
                fqn: Scribe.Cache.InternPool.Intern(type.ToDisplayString()),
                origin: Scribe.Cache.LocationInfo.From(Shape<TModel>.FirstLocationOf(type)));

            var alternatives = _shape.Alternatives!;
            var perBranchCounts = new int[alternatives.Length];
            var perBranchIds = new List<string>[alternatives.Length];
            var anyBranchPassed = false;

            for (var i = 0; i < alternatives.Length; i++)
            {
                var alt = alternatives[i];
                perBranchIds[i] = new List<string>();

                if (!Shape<TModel>.MatchesSymbolKindSingle(alt.Kind, type))
                {
                    perBranchIds[i].Add("SCRIBE101");
                    perBranchCounts[i] = 1;
                    continue;
                }

                foreach (var check in alt.Checks)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    if (check.Predicate(typeFocus, ctx.Compilation, ctx.CancellationToken))
                    {
                        continue;
                    }

                    perBranchIds[i].Add(check.Id);
                    perBranchCounts[i]++;
                }

                if (alt.LensBranches.Length > 0)
                {
                    var scratch = new List<Scribe.Cache.DiagnosticInfo>();
                    foreach (var branch in alt.LensBranches)
                    {
                        branch.Evaluate(typeFocus, ctx.Compilation, ctx.CancellationToken, scratch);
                    }

                    foreach (var violation in scratch)
                    {
                        perBranchIds[i].Add(violation.Id);
                        perBranchCounts[i]++;
                    }
                }

                if (perBranchCounts[i] == 0)
                {
                    anyBranchPassed = true;
                    break;
                }
            }

            if (anyBranchPassed)
            {
                return;
            }

            var summary = new System.Text.StringBuilder();
            for (var i = 0; i < alternatives.Length; i++)
            {
                if (i > 0)
                {
                    summary.Append("  —OR—  ");
                }

                summary.Append("[branch").Append(i + 1).Append(": ");
                for (var j = 0; j < perBranchIds[i].Count; j++)
                {
                    if (j > 0)
                    {
                        summary.Append(", ");
                    }

                    summary.Append(perBranchIds[i][j]);
                }

                summary.Append(']');
            }

            var fusionDescriptor = _shape.DescriptorsById[_shape.FusionSpec!.Id];
            var location = Shape<TModel>.FirstLocationOf(type);
            ctx.ReportDiagnostic(Diagnostic.Create(
                fusionDescriptor,
                location,
                alternatives.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                summary.ToString()));
        }

        private static ImmutableDictionary<string, string?> BuildProperties(
            ShapeCheck check, TypeFocus focus)
        {
            var props = check.FixProperties?.Invoke(focus)
                ?? ImmutableDictionary<string, string?>.Empty;

            return props
                .SetItem("fixKind", check.FixKind.ToString())
                .SetItem("squiggleAt", check.SquiggleAt.ToString());
        }

        private static ImmutableDictionary<string, string?> BuildMemberProperties(
            MemberCheck check, INamedTypeSymbol type, ISymbol member)
        {
            var props = check.FixProperties?.Invoke(type, member)
                ?? ImmutableDictionary<string, string?>.Empty;

            props = props
                .SetItem("fixKind", check.FixKind.ToString())
                .SetItem("memberSquiggleAt", check.SquiggleAt.ToString())
                .SetItem("memberName", member.Name);

            if (check.FixKind == FixKind.Custom && !string.IsNullOrEmpty(check.CustomFixTag))
            {
                props = props.SetItem("customFixTag", check.CustomFixTag);
            }

            return props;
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
