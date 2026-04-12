using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Fluent builder that accumulates a conjunction of <c>MustBeX</c> checks on a single
///     type-shape subject and projects the match into a cache-safe <c>TModel</c>.
/// </summary>
/// <remarks>
///     <para>Obtain via the factories on <see cref="Shape"/>.</para>
///     <para>Each primitive is a declarative constraint: the check carries a default
///     diagnostic ID, title, message, severity, squiggle anchor, and fix hint; callers
///     can selectively override any of these via a <see cref="DiagnosticSpec"/>.</para>
/// </remarks>
public sealed partial class ShapeBuilder
{
    private readonly TypeKindFilter _kind;
    private readonly List<ShapeCheck> _checks = new();
    private string? _primaryAttributeMetadataName;

    internal ShapeBuilder(TypeKindFilter kind) => _kind = kind;

    internal TypeKindFilter Kind => _kind;
    internal IReadOnlyList<ShapeCheck> Checks => _checks;
    internal string? PrimaryAttributeMetadataName => _primaryAttributeMetadataName;

    /// <summary>
    ///     Seal the builder and project matches into a cache-safe model.
    ///     The projection runs inside the incremental pipeline's transform stage, with
    ///     access to the matched symbol, semantic model, and — if declared — the
    ///     driving attribute reader.
    /// </summary>
    /// <typeparam name="TModel">Must be <see cref="IEquatable{T}"/> for cache correctness.</typeparam>
    public Shape<TModel> Project<TModel>(ProjectionDelegate<TModel> project)
        where TModel : IEquatable<TModel>
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        return new Shape<TModel>(
            kind: _kind,
            checks: _checks.ToArray(),
            primaryAttributeMetadataName: _primaryAttributeMetadataName,
            project: project);
    }

    internal bool MatchesSyntaxNode(SyntaxNode node) =>
        _kind switch
        {
            TypeKindFilter.Class =>
                node is ClassDeclarationSyntax c && !c.Modifiers.Any(SyntaxKind.RecordKeyword),
            TypeKindFilter.Record =>
                node is RecordDeclarationSyntax r && r.IsKind(SyntaxKind.RecordDeclaration),
            TypeKindFilter.RecordStruct =>
                node is RecordDeclarationSyntax rs && rs.IsKind(SyntaxKind.RecordStructDeclaration),
            TypeKindFilter.Struct =>
                node is StructDeclarationSyntax s && !s.Modifiers.Any(SyntaxKind.RecordKeyword),
            TypeKindFilter.Interface =>
                node is InterfaceDeclarationSyntax,
            TypeKindFilter.Any =>
                node is TypeDeclarationSyntax,
            _ => false,
        };

    internal bool MatchesSymbolKind(INamedTypeSymbol symbol) =>
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

    private void AddCheck(
        string defaultId,
        string defaultTitle,
        string defaultMessage,
        DiagnosticSeverity defaultSeverity,
        SquiggleAt defaultSquiggle,
        FixKind defaultFix,
        Func<INamedTypeSymbol, Compilation, CancellationToken, bool> predicate,
        Func<INamedTypeSymbol, EquatableArray<string>> messageArgs,
        DiagnosticSpec? spec,
        Func<INamedTypeSymbol, ImmutableDictionary<string, string?>>? fixProperties = null)
    {
        _checks.Add(new ShapeCheck(
            Id: InternPool.Intern(spec?.Id ?? defaultId),
            Title: spec?.Title ?? defaultTitle,
            MessageFormat: spec?.Message ?? defaultMessage,
            Severity: spec?.Severity ?? defaultSeverity,
            SquiggleAt: spec?.Target ?? defaultSquiggle,
            FixKind: spec?.Fix?.Kind ?? defaultFix,
            Predicate: predicate,
            MessageArgs: messageArgs,
            FixProperties: fixProperties));
    }

    // ───────────────────────────────────────────────────────────────
    //  Primitives (Phase 3)
    // ───────────────────────────────────────────────────────────────

    /// <summary>Require the type to be declared <c>partial</c>.</summary>
    public ShapeBuilder MustBePartial(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE001",
            defaultTitle: "Type must be partial",
            defaultMessage: "Type '{0}' must be declared 'partial'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.AddPartialModifier,
            predicate: static (sym, _, ct) => IsPartial(sym, ct),
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Require the type to be declared <c>sealed</c>.</summary>
    public ShapeBuilder MustBeSealed(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE002",
            defaultTitle: "Type must be sealed",
            defaultMessage: "Type '{0}' must be declared 'sealed'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.AddSealedModifier,
            predicate: static (sym, _, _) => sym.IsSealed || sym.IsValueType || sym.IsStatic,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>
    ///     Require the type to implement <typeparamref name="T"/>. For non-generic interfaces
    ///     only in v1 — use the <see cref="MustImplement(string, DiagnosticSpec?)"/> overload
    ///     with a metadata name for generic interfaces.
    /// </summary>
    public ShapeBuilder MustImplement<T>(DiagnosticSpec? spec = null)
        where T : class
    {
        var fqn = typeof(T).FullName!;
        return MustImplement(fqn, spec);
    }

    /// <summary>Require the type to implement the interface named by <paramref name="metadataName"/>.</summary>
    public ShapeBuilder MustImplement(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE003",
            defaultTitle: "Type must implement required interface",
            defaultMessage: "Type '{0}' must implement '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.BaseList,
            defaultFix: FixKind.AddInterfaceToBaseList,
            predicate: (sym, comp, _) => ImplementsByMetadataName(sym, comp, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("interface", interned));
        return this;
    }

    /// <summary>
    ///     Require the type to carry attribute <typeparamref name="T"/>. The first
    ///     <c>MustHaveAttribute</c> declared on the shape is promoted to the driving
    ///     attribute and routes collection through
    ///     <see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/>.
    /// </summary>
    public ShapeBuilder MustHaveAttribute<T>(DiagnosticSpec? spec = null)
        where T : System.Attribute
        => MustHaveAttribute(typeof(T).FullName!, spec);

    /// <summary>
    ///     Require the type to carry the attribute named by <paramref name="metadataName"/>.
    /// </summary>
    public ShapeBuilder MustHaveAttribute(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        _primaryAttributeMetadataName ??= interned;

        AddCheck(
            defaultId: "SCRIBE004",
            defaultTitle: "Type must have required attribute",
            defaultMessage: "Type '{0}' must be annotated with '[{1}]'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.AddAttribute,
            predicate: (sym, _, _) => HasAttribute(sym, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("attribute", interned));
        return this;
    }

    /// <summary>
    ///     Require the type's <see cref="ISymbol.Name"/> to match <paramref name="pattern"/>
    ///     as a regular expression. No auto-fix is offered by default — a regex pattern
    ///     does not uniquely identify a target name; override via
    ///     <see cref="DiagnosticSpec"/> with a concrete <see cref="FixSpec"/> to supply one.
    /// </summary>
    public ShapeBuilder MustBeNamed(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE005",
            defaultTitle: "Type name must match required pattern",
            defaultMessage: "Type '{0}' name does not match pattern '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.None,
            predicate: (sym, _, _) => regex.IsMatch(sym.Name),
            messageArgs: sym => EquatableArray.Create(sym.Name, pattern),
            spec: spec);
        return this;
    }

    /// <summary>Require the type to be declared <c>abstract</c>.</summary>
    public ShapeBuilder MustBeAbstract(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE006",
            defaultTitle: "Type must be abstract",
            defaultMessage: "Type '{0}' must be declared 'abstract'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.AddAbstractModifier,
            predicate: static (sym, _, _) => sym.IsAbstract,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Require the type to be declared <c>static</c>.</summary>
    public ShapeBuilder MustBeStatic(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE007",
            defaultTitle: "Type must be static",
            defaultMessage: "Type '{0}' must be declared 'static'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.AddStaticModifier,
            predicate: static (sym, _, _) => sym.IsStatic,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>
    ///     Require the type to extend the base class <typeparamref name="T"/>.
    ///     Interfaces use <see cref="MustImplement{T}(DiagnosticSpec?)"/> instead.
    /// </summary>
    public ShapeBuilder MustExtend<T>(DiagnosticSpec? spec = null)
        where T : class
        => MustExtend(typeof(T).FullName!, spec);

    /// <summary>
    ///     Require the type to extend the base class named by <paramref name="metadataName"/>.
    /// </summary>
    public ShapeBuilder MustExtend(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE008",
            defaultTitle: "Type must extend required base class",
            defaultMessage: "Type '{0}' must extend base class '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.BaseList,
            defaultFix: FixKind.AddBaseClass,
            predicate: (sym, compilation, _) => ExtendsByMetadataName(sym, compilation, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("baseClass", interned));
        return this;
    }

    /// <summary>
    ///     Require the type's containing namespace to match <paramref name="pattern"/>
    ///     as a regular expression. No auto-fix is offered — moving a file is outside
    ///     Scribe's automation surface.
    /// </summary>
    public ShapeBuilder MustBeInNamespace(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE009",
            defaultTitle: "Type's namespace must match required pattern",
            defaultMessage: "Type '{0}' is in namespace '{1}' which does not match pattern '{2}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ContainingNamespace,
            defaultFix: FixKind.None,
            predicate: (sym, _, _) => regex.IsMatch(sym.ContainingNamespace?.ToDisplayString() ?? string.Empty),
            messageArgs: sym => EquatableArray.Create(
                sym.Name,
                sym.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                pattern),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>abstract</c> modifier on the type.</summary>
    public ShapeBuilder MustNotBeAbstract(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE010",
            defaultTitle: "Type must not be abstract",
            defaultMessage: "Type '{0}' must not be declared 'abstract'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.RemoveAbstractModifier,
            predicate: static (sym, _, _) => !sym.IsAbstract,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid generic type parameters on the type.</summary>
    public ShapeBuilder MustNotBeGeneric(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE011",
            defaultTitle: "Type must not be generic",
            defaultMessage: "Type '{0}' must not declare generic type parameters",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => !sym.IsGenericType,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid implementation of interface <typeparamref name="T"/>.</summary>
    public ShapeBuilder MustNotImplement<T>(DiagnosticSpec? spec = null)
        where T : class
        => MustNotImplement(typeof(T).FullName!, spec);

    /// <summary>
    ///     Forbid implementation of the interface named by <paramref name="metadataName"/>.
    /// </summary>
    public ShapeBuilder MustNotImplement(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE012",
            defaultTitle: "Type must not implement forbidden interface",
            defaultMessage: "Type '{0}' must not implement '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.BaseList,
            defaultFix: FixKind.RemoveFromBaseList,
            predicate: (sym, compilation, _) => !ImplementsByMetadataName(sym, compilation, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("interface", interned));
        return this;
    }

    // ───────────────────────────────────────────────────────────────
    //  Predicate helpers (static, closure-free)
    // ───────────────────────────────────────────────────────────────

    private static bool IsPartial(INamedTypeSymbol symbol, CancellationToken ct)
    {
        var refs = symbol.DeclaringSyntaxReferences;
        foreach (var reference in refs)
        {
            ct.ThrowIfCancellationRequested();
            if (reference.GetSyntax(ct) is TypeDeclarationSyntax tds
                && tds.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsByMetadataName(
        INamedTypeSymbol symbol, Compilation compilation, string metadataName)
    {
        var target = compilation.GetTypeByMetadataName(metadataName);
        if (target is null)
        {
            // If the interface is not visible in this compilation, cannot assert — treat as satisfied.
            return true;
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

    private static bool ExtendsByMetadataName(
        INamedTypeSymbol symbol, Compilation compilation, string metadataName)
    {
        var target = compilation.GetTypeByMetadataName(metadataName);
        if (target is null)
        {
            // Base class not visible in this compilation — cannot assert, treat as satisfied.
            return true;
        }

        var current = symbol.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

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

            // Strip generic args and/or trailing "Attribute" for a forgiving match.
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
