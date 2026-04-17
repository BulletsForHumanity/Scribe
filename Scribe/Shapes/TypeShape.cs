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
///     Focused authoring Shape over a type declaration. Accumulates a conjunction of
///     <c>MustBeX</c> checks and seals the match into a cache-safe <c>TModel</c> via
///     <see cref="Etch{TModel}"/>.
/// </summary>
/// <remarks>
///     <para>Obtain via the <c>Expose*</c> factories on <see cref="Stencil"/>.</para>
///     <para>Each primitive is a declarative constraint: the check carries a default
///     diagnostic ID, title, message, severity, squiggle anchor, and fix hint; callers
///     can selectively override any of these via a <see cref="DiagnosticSpec"/>.</para>
/// </remarks>
public sealed partial class TypeShape
{
    private readonly TypeKindFilter _kind;
    private readonly List<ShapeCheck> _checks = new();
    private readonly List<MemberCheck> _memberChecks = new();
    private readonly List<ILensBranch<TypeFocus>> _lensBranches = new();
    private string? _primaryAttributeMetadataName;
    private string? _primaryInterfaceMetadataName;

    internal TypeShape(TypeKindFilter kind) => _kind = kind;

    internal TypeKindFilter Kind => _kind;
    internal IReadOnlyList<ShapeCheck> Checks => _checks;
    internal IReadOnlyList<MemberCheck> MemberChecks => _memberChecks;
    internal IReadOnlyList<ILensBranch<TypeFocus>> LensBranches => _lensBranches;
    internal string? PrimaryAttributeMetadataName => _primaryAttributeMetadataName;
    internal string? PrimaryInterfaceMetadataName => _primaryInterfaceMetadataName;

    internal void AddLensBranch(ILensBranch<TypeFocus> branch) =>
        _lensBranches.Add(branch ?? throw new ArgumentNullException(nameof(branch)));

    /// <summary>
    ///     Compose this <see cref="TypeShape"/> with one or more sibling alternatives
    ///     into a <see cref="OneOfTypeShape"/>. The caller can then seal the
    ///     disjunction with <see cref="OneOfTypeShape.Etch{TModel}"/>.
    /// </summary>
    /// <param name="others">Additional alternatives. At least one must be supplied.</param>
    public OneOfTypeShape OneOf(params TypeShape[] others)
    {
        if (others is null || others.Length == 0)
        {
            throw new ArgumentException("OneOf requires at least one sibling alternative.", nameof(others));
        }

        var all = new TypeShape[others.Length + 1];
        all[0] = this;
        Array.Copy(others, 0, all, 1, others.Length);
        return new OneOfTypeShape(all, fusionSpec: null);
    }

    /// <summary>
    ///     Overload of <see cref="OneOf(TypeShape[])"/> that accepts a
    ///     <see cref="DiagnosticSpec"/> override for the fused diagnostic.
    /// </summary>
    public OneOfTypeShape OneOf(DiagnosticSpec fusionSpec, params TypeShape[] others)
    {
        if (others is null || others.Length == 0)
        {
            throw new ArgumentException("OneOf requires at least one sibling alternative.", nameof(others));
        }

        var all = new TypeShape[others.Length + 1];
        all[0] = this;
        Array.Copy(others, 0, all, 1, others.Length);
        return new OneOfTypeShape(all, fusionSpec);
    }

    /// <summary>
    ///     Etch the authoring chain into a sealed <see cref="Shape{TModel}"/>, permanently
    ///     committing the accumulated predicates, lenses, and member checks and producing
    ///     a cache-safe <typeparamref name="TModel"/> for each surviving match.
    ///     The etch callback runs inside the incremental pipeline's transform stage, with
    ///     access to the matched symbol, semantic model, and — if declared — the
    ///     driving attribute reader.
    /// </summary>
    /// <typeparam name="TModel">Must be <see cref="IEquatable{T}"/> for cache correctness.</typeparam>
    public Shape<TModel> Etch<TModel>(EtchDelegate<TModel> etch)
        where TModel : IEquatable<TModel>
    {
        if (etch is null)
        {
            throw new ArgumentNullException(nameof(etch));
        }

        return new Shape<TModel>(
            kind: _kind,
            checks: _checks.ToArray(),
            memberChecks: _memberChecks.ToArray(),
            lensBranches: _lensBranches.ToArray(),
            primaryAttributeMetadataName: _primaryAttributeMetadataName,
            primaryInterfaceMetadataName: _primaryInterfaceMetadataName,
            etch: etch);
    }

    /// <summary>
    ///     Enter the attributes lens: navigate every attribute application on the matched
    ///     type whose class FQN equals <paramref name="attributeFqn"/>, optionally
    ///     declaring further predicates or sub-lens hops through the
    ///     <paramref name="configure"/> callback. Declares a presence constraint when
    ///     <paramref name="min"/> / <paramref name="max"/> are supplied.
    /// </summary>
    /// <param name="attributeFqn">Fully-qualified attribute class name. Open-generic forms are matched by the bare name before <c>&lt;</c>.</param>
    /// <param name="configure">Optional callback receiving the nested <see cref="FocusShape{AttributeFocus}"/> for per-application checks and deeper navigation.</param>
    /// <param name="min">Minimum number of attribute applications required on the type. <c>0</c> (default) disables the lower bound.</param>
    /// <param name="max">Maximum allowed. <see langword="null"/> (default) disables the upper bound.</param>
    /// <param name="presenceSpec">Override for the presence-violation diagnostic descriptor.</param>
    /// <param name="quantifier">How nested-check results aggregate across navigated attribute applications — <see cref="Quantifier.All"/> (default) emits per-child violations; <see cref="Quantifier.Any"/> requires at least one application to pass; <see cref="Quantifier.None"/> requires every application to fail.</param>
    /// <param name="quantifierSpec">Override for the aggregate diagnostic emitted when <paramref name="quantifier"/> is <see cref="Quantifier.Any"/> or <see cref="Quantifier.None"/>. Ignored for <see cref="Quantifier.All"/>.</param>
    public TypeShape Attributes(
        string attributeFqn,
        Action<FocusShape<AttributeFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (string.IsNullOrEmpty(attributeFqn))
        {
            throw new ArgumentException("Attribute FQN must not be empty.", nameof(attributeFqn));
        }

        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum count must be non-negative.");
        }

        if (max is { } m && m < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum count must be at least the minimum.");
        }

        var nested = new FocusShape<AttributeFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.Attributes(attributeFqn);

        LensPresenceSpec? presence = null;
        var hasPresence = min > 0 || max is not null || presenceSpec is not null;
        if (hasPresence)
        {
            presence = new LensPresenceSpec(
                Id: InternPool.Intern(presenceSpec?.Id ?? "SCRIBE050"),
                Title: presenceSpec?.Title ?? "Attribute presence constraint",
                MessageFormat: presenceSpec?.Message
                    ?? "Expected [{0}..{1}] applications of attribute '" + attributeFqn + "', observed {2}",
                Severity: presenceSpec?.Severity ?? DiagnosticSeverity.Error);
        }

        var quantifierDescriptor = BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE092" : "SCRIBE093",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one application of attribute '" + attributeFqn + "' must satisfy the required checks"
                : "No application of attribute '" + attributeFqn + "' may satisfy the disallowed checks");

        _lensBranches.Add(new LensBranch<TypeFocus, AttributeFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor,
            HopDescription: "Attributes(\"" + attributeFqn + "\")"));

        return this;
    }

    internal static LensQuantifierSpec? BuildQuantifierSpec(
        Quantifier quantifier,
        DiagnosticSpec? spec,
        string defaultId,
        string defaultMessage)
    {
        if (quantifier == Quantifier.All)
        {
            return null;
        }

        return new LensQuantifierSpec(
            Id: InternPool.Intern(spec?.Id ?? defaultId),
            Title: spec?.Title ?? (quantifier == Quantifier.Any
                ? "Any-quantifier aggregate"
                : "None-quantifier aggregate"),
            MessageFormat: spec?.Message ?? defaultMessage,
            Severity: spec?.Severity ?? DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     Enter the members lens: navigate every directly-declared member of the
    ///     matched type in source order, optionally filtered by
    ///     <see cref="SymbolKind"/> (field, property, method, event, nested type).
    ///     Declares a presence constraint when <paramref name="min"/> /
    ///     <paramref name="max"/> are supplied.
    /// </summary>
    /// <param name="kind">Restrict navigation to one member kind. <see langword="null"/> (default) navigates every declared member regardless of kind.</param>
    /// <param name="configure">Optional callback receiving the nested <see cref="FocusShape{MemberFocus}"/> for per-member checks.</param>
    /// <param name="min">Minimum member count required on the type.</param>
    /// <param name="max">Maximum member count allowed. <see langword="null"/> disables the upper bound.</param>
    /// <param name="presenceSpec">Override for the presence-violation diagnostic descriptor.</param>
    /// <param name="quantifier">How nested-check results aggregate across navigated members — <see cref="Quantifier.All"/> (default) emits per-child violations; <see cref="Quantifier.Any"/> requires at least one member to pass; <see cref="Quantifier.None"/> requires every member to fail.</param>
    /// <param name="quantifierSpec">Override for the aggregate diagnostic emitted when <paramref name="quantifier"/> is <see cref="Quantifier.Any"/> or <see cref="Quantifier.None"/>. Ignored for <see cref="Quantifier.All"/>.</param>
    public TypeShape Members(
        SymbolKind? kind = null,
        Action<FocusShape<MemberFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum count must be non-negative.");
        }

        if (max is { } m && m < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum count must be at least the minimum.");
        }

        var nested = new FocusShape<MemberFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.Members(kind);

        LensPresenceSpec? presence = null;
        var hasPresence = min > 0 || max is not null || presenceSpec is not null;
        var kindDescription = kind?.ToString() ?? "any kind";
        if (hasPresence)
        {
            presence = new LensPresenceSpec(
                Id: InternPool.Intern(presenceSpec?.Id ?? "SCRIBE051"),
                Title: presenceSpec?.Title ?? "Member presence constraint",
                MessageFormat: presenceSpec?.Message
                    ?? "Expected [{0}..{1}] members of " + kindDescription + ", observed {2}",
                Severity: presenceSpec?.Severity ?? DiagnosticSeverity.Error);
        }

        var quantifierDescriptor = BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE090" : "SCRIBE091",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one member of " + kindDescription + " must satisfy the required checks"
                : "No member of " + kindDescription + " may satisfy the disallowed checks");

        _lensBranches.Add(new LensBranch<TypeFocus, MemberFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor,
            HopDescription: kind is null ? "Members" : "Members(" + kindDescription + ")"));

        return this;
    }

    /// <summary>
    ///     Enter the base-type-chain lens: navigate the matched type's inheritance
    ///     chain from immediate base up to (but excluding) <see cref="object"/>.
    ///     Each step carries its depth — <c>0</c> = immediate base, <c>1</c> =
    ///     grandparent, and so on. Declares a presence constraint when
    ///     <paramref name="min"/> / <paramref name="max"/> are supplied;
    ///     <c>min: 1</c> means "must inherit from a non-<see cref="object"/> base",
    ///     <c>max: 0</c> means "must inherit directly from <see cref="object"/>".
    /// </summary>
    /// <param name="configure">Optional callback receiving the nested <see cref="FocusShape{BaseTypeChainFocus}"/> for per-step checks.</param>
    /// <param name="min">Minimum chain length required.</param>
    /// <param name="max">Maximum chain length allowed. <see langword="null"/> disables the upper bound.</param>
    /// <param name="presenceSpec">Override for the presence-violation diagnostic descriptor.</param>
    /// <param name="quantifier">How nested-check results aggregate across base-type-chain steps — <see cref="Quantifier.All"/> (default) emits per-child violations; <see cref="Quantifier.Any"/> requires at least one step to pass; <see cref="Quantifier.None"/> requires every step to fail.</param>
    /// <param name="quantifierSpec">Override for the aggregate diagnostic emitted when <paramref name="quantifier"/> is <see cref="Quantifier.Any"/> or <see cref="Quantifier.None"/>. Ignored for <see cref="Quantifier.All"/>.</param>
    public TypeShape BaseTypeChain(
        Action<FocusShape<BaseTypeChainFocus>>? configure = null,
        int min = 0,
        int? max = null,
        DiagnosticSpec? presenceSpec = null,
        Quantifier quantifier = Quantifier.All,
        DiagnosticSpec? quantifierSpec = null)
    {
        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum count must be non-negative.");
        }

        if (max is { } m && m < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum count must be at least the minimum.");
        }

        var nested = new FocusShape<BaseTypeChainFocus>();
        configure?.Invoke(nested);
        var lens = Lenses.BuiltinLenses.BaseTypeChain();

        LensPresenceSpec? presence = null;
        var hasPresence = min > 0 || max is not null || presenceSpec is not null;
        if (hasPresence)
        {
            presence = new LensPresenceSpec(
                Id: InternPool.Intern(presenceSpec?.Id ?? "SCRIBE052"),
                Title: presenceSpec?.Title ?? "Base-type-chain length constraint",
                MessageFormat: presenceSpec?.Message
                    ?? "Expected [{0}..{1}] base-type-chain steps, observed {2}",
                Severity: presenceSpec?.Severity ?? DiagnosticSeverity.Error);
        }

        var quantifierDescriptor = BuildQuantifierSpec(
            quantifier,
            quantifierSpec,
            defaultId: quantifier == Quantifier.Any ? "SCRIBE094" : "SCRIBE095",
            defaultMessage: quantifier == Quantifier.Any
                ? "At least one base-type-chain step must satisfy the required checks"
                : "No base-type-chain step may satisfy the disallowed checks");

        _lensBranches.Add(new LensBranch<TypeFocus, BaseTypeChainFocus>(
            Lens: lens,
            Nested: nested,
            MinCount: min,
            MaxCount: max,
            Presence: presence,
            ParentOrigin: parent => parent.Origin,
            Quantifier: quantifier,
            QuantifierSpec: quantifierDescriptor,
            HopDescription: "BaseTypeChain"));

        return this;
    }

    /// <summary>
    ///     Narrow the shape to types implementing the interface named by
    ///     <paramref name="metadataName"/>. Acts as a <em>filter</em>, not a check:
    ///     types not implementing the interface are silently skipped — no diagnostic
    ///     is produced. Contrast with <see cref="MustImplement(string, DiagnosticSpec?)"/>,
    ///     which applies to every type of the configured kind and reports a violation
    ///     on non-implementers.
    /// </summary>
    /// <remarks>
    ///     Only one primary interface selector may be declared per shape. Subsequent
    ///     calls are ignored. The selector routes collection through the ordinary
    ///     <c>CreateSyntaxProvider</c> + semantic-model lookup path — no Roslyn
    ///     fast-path equivalent to <c>ForAttributeWithMetadataName</c> exists for
    ///     interface implementation.
    /// </remarks>
    public TypeShape Implementing(string metadataName)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        _primaryInterfaceMetadataName ??= InternPool.Intern(metadataName);
        return this;
    }

    /// <summary>
    ///     Generic overload of <see cref="Implementing(string)"/>. Use when the
    ///     interface's closed form is known at shape-declaration time; for open
    ///     generics (e.g. <c>IFoo&lt;&gt;</c>), supply the metadata name directly.
    /// </summary>
    public TypeShape Implementing<T>()
        where T : class
        => Implementing(typeof(T).FullName!);

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
            Predicate: (focus, comp, ct) => predicate(focus.Symbol, comp, ct),
            MessageArgs: focus => messageArgs(focus.Symbol),
            FixProperties: fixProperties is null ? null : focus => fixProperties(focus.Symbol)));
    }

    // ───────────────────────────────────────────────────────────────
    //  Primitives (Phase 3)
    // ───────────────────────────────────────────────────────────────

    /// <summary>Require the type to be declared <c>partial</c>.</summary>
    public TypeShape MustBePartial(DiagnosticSpec? spec = null)
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
    public TypeShape MustBeSealed(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE005",
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
    public TypeShape MustImplement<T>(DiagnosticSpec? spec = null)
        where T : class
    {
        var fqn = typeof(T).FullName!;
        return MustImplement(fqn, spec);
    }

    /// <summary>Require the type to implement the interface named by <paramref name="metadataName"/>.</summary>
    public TypeShape MustImplement(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE007",
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
    public TypeShape MustHaveAttribute<T>(DiagnosticSpec? spec = null)
        where T : System.Attribute
        => MustHaveAttribute(typeof(T).FullName!, spec);

    /// <summary>
    ///     Require the type to carry the attribute named by <paramref name="metadataName"/>.
    /// </summary>
    public TypeShape MustHaveAttribute(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        _primaryAttributeMetadataName ??= interned;

        AddCheck(
            defaultId: "SCRIBE003",
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
    public TypeShape MustBeNamed(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE029",
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
    public TypeShape MustBeAbstract(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE015",
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
    public TypeShape MustBeStatic(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE017",
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
    public TypeShape MustExtend<T>(DiagnosticSpec? spec = null)
        where T : class
        => MustExtend(typeof(T).FullName!, spec);

    /// <summary>
    ///     Require the type to extend the base class named by <paramref name="metadataName"/>.
    /// </summary>
    public TypeShape MustExtend(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE009",
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
    public TypeShape MustBeInNamespace(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE027",
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
    public TypeShape MustNotBeAbstract(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE016",
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
    public TypeShape MustNotBeGeneric(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE024",
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
    public TypeShape MustNotImplement<T>(DiagnosticSpec? spec = null)
        where T : class
        => MustNotImplement(typeof(T).FullName!, spec);

    /// <summary>
    ///     Forbid implementation of the interface named by <paramref name="metadataName"/>.
    /// </summary>
    public TypeShape MustNotImplement(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE008",
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
    //  Phase 8.5 — negations of positive primitives
    // ───────────────────────────────────────────────────────────────

    /// <summary>Forbid the <c>partial</c> modifier on the type.</summary>
    public TypeShape MustNotBePartial(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE002",
            defaultTitle: "Type must not be partial",
            defaultMessage: "Type '{0}' must not be declared 'partial'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.RemovePartialModifier,
            predicate: static (sym, _, ct) => !IsPartial(sym, ct),
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>sealed</c> modifier on the type.</summary>
    public TypeShape MustNotBeSealed(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE006",
            defaultTitle: "Type must not be sealed",
            defaultMessage: "Type '{0}' must not be declared 'sealed'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.RemoveSealedModifier,
            // Value types and static classes are implicitly sealed — ignore them
            // so this check only flags explicit 'sealed' on classes that could be unsealed.
            predicate: static (sym, _, _) => !sym.IsSealed || sym.IsValueType || sym.IsStatic,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid attribute <typeparamref name="T"/> on the type.</summary>
    public TypeShape MustNotHaveAttribute<T>(DiagnosticSpec? spec = null)
        where T : System.Attribute
        => MustNotHaveAttribute(typeof(T).FullName!, spec);

    /// <summary>Forbid the attribute named by <paramref name="metadataName"/>.</summary>
    public TypeShape MustNotHaveAttribute(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE004",
            defaultTitle: "Type must not carry forbidden attribute",
            defaultMessage: "Type '{0}' must not be annotated with '[{1}]'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.AttributeList,
            defaultFix: FixKind.RemoveAttribute,
            predicate: (sym, _, _) => !HasAttribute(sym, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("attribute", interned));
        return this;
    }

    /// <summary>
    ///     Forbid the type's <see cref="ISymbol.Name"/> from matching <paramref name="pattern"/>.
    ///     No auto-fix — renaming is a cross-file operation outside Scribe's automation surface.
    /// </summary>
    public TypeShape MustNotBeNamed(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE030",
            defaultTitle: "Type name must not match forbidden pattern",
            defaultMessage: "Type '{0}' name matches forbidden pattern '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.None,
            predicate: (sym, _, _) => !regex.IsMatch(sym.Name),
            messageArgs: sym => EquatableArray.Create(sym.Name, pattern),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>static</c> modifier on the type.</summary>
    public TypeShape MustNotBeStatic(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE018",
            defaultTitle: "Type must not be static",
            defaultMessage: "Type '{0}' must not be declared 'static'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.RemoveStaticModifier,
            predicate: static (sym, _, _) => !sym.IsStatic,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the type from extending <typeparamref name="T"/>.</summary>
    public TypeShape MustNotExtend<T>(DiagnosticSpec? spec = null)
        where T : class
        => MustNotExtend(typeof(T).FullName!, spec);

    /// <summary>Forbid the type from extending the base class named by <paramref name="metadataName"/>.</summary>
    public TypeShape MustNotExtend(string metadataName, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(metadataName))
        {
            throw new ArgumentException("Metadata name must not be empty.", nameof(metadataName));
        }

        var interned = InternPool.Intern(metadataName);
        AddCheck(
            defaultId: "SCRIBE010",
            defaultTitle: "Type must not extend forbidden base class",
            defaultMessage: "Type '{0}' must not extend base class '{1}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.BaseList,
            defaultFix: FixKind.RemoveFromBaseList,
            predicate: (sym, compilation, _) => !ExtendsByMetadataName(sym, compilation, interned),
            messageArgs: sym => EquatableArray.Create(sym.Name, interned),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("baseClass", interned));
        return this;
    }

    /// <summary>
    ///     Forbid the type's containing namespace from matching <paramref name="pattern"/>.
    ///     No auto-fix — moving a file is outside Scribe's automation surface.
    /// </summary>
    public TypeShape MustNotBeInNamespace(string pattern, DiagnosticSpec? spec = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
        }

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        AddCheck(
            defaultId: "SCRIBE028",
            defaultTitle: "Type must not be in forbidden namespace",
            defaultMessage: "Type '{0}' is in namespace '{1}' which matches forbidden pattern '{2}'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ContainingNamespace,
            defaultFix: FixKind.None,
            predicate: (sym, _, _) => !regex.IsMatch(sym.ContainingNamespace?.ToDisplayString() ?? string.Empty),
            messageArgs: sym => EquatableArray.Create(
                sym.Name,
                sym.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                pattern),
            spec: spec);
        return this;
    }

    /// <summary>Require the type to declare at least one generic type parameter.</summary>
    public TypeShape MustBeGeneric(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE023",
            defaultTitle: "Type must be generic",
            defaultMessage: "Type '{0}' must declare at least one generic type parameter",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.IsGenericType,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    // ───────────────────────────────────────────────────────────────
    //  Phase 8.5 — visibility
    // ───────────────────────────────────────────────────────────────

    /// <summary>Require the type to be declared <c>public</c>.</summary>
    public TypeShape MustBePublic(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE011",
            defaultTitle: "Type must be public",
            defaultMessage: "Type '{0}' must be declared 'public'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.SetVisibility,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility == Accessibility.Public,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("visibility", "public"));
        return this;
    }

    /// <summary>Require the type to be declared <c>internal</c>.</summary>
    public TypeShape MustBeInternal(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE013",
            defaultTitle: "Type must be internal",
            defaultMessage: "Type '{0}' must be declared 'internal'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.SetVisibility,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility == Accessibility.Internal,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("visibility", "internal"));
        return this;
    }

    /// <summary>
    ///     Require a nested type to be declared <c>private</c>. Top-level types cannot be
    ///     <c>private</c> — use <see cref="MustBeInternal"/> for that case.
    /// </summary>
    public TypeShape MustBePrivate(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE025",
            defaultTitle: "Type must be private",
            defaultMessage: "Type '{0}' must be declared 'private'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.SetVisibility,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility == Accessibility.Private,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec,
            fixProperties: _ => ImmutableDictionary<string, string?>.Empty
                .Add("visibility", "private"));
        return this;
    }

    /// <summary>Forbid the <c>public</c> visibility modifier on the type.</summary>
    public TypeShape MustNotBePublic(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE012",
            defaultTitle: "Type must not be public",
            defaultMessage: "Type '{0}' must not be declared 'public'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility != Accessibility.Public,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>internal</c> visibility modifier on the type.</summary>
    public TypeShape MustNotBeInternal(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE014",
            defaultTitle: "Type must not be internal",
            defaultMessage: "Type '{0}' must not be declared 'internal'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility != Accessibility.Internal,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>private</c> visibility modifier on the type.</summary>
    public TypeShape MustNotBePrivate(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE026",
            defaultTitle: "Type must not be private",
            defaultMessage: "Type '{0}' must not be declared 'private'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.ModifierList,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.DeclaredAccessibility != Accessibility.Private,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    // ───────────────────────────────────────────────────────────────
    //  Phase 8.5 — declaration kind
    // ───────────────────────────────────────────────────────────────

    /// <summary>Require the type to be a <c>record</c> (class-record or record-struct).</summary>
    public TypeShape MustBeRecord(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE019",
            defaultTitle: "Type must be a record",
            defaultMessage: "Type '{0}' must be declared as a 'record'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.IsRecord,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the <c>record</c> keyword on the type.</summary>
    public TypeShape MustNotBeRecord(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE020",
            defaultTitle: "Type must not be a record",
            defaultMessage: "Type '{0}' must not be declared as a 'record'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => !sym.IsRecord,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Require the type to be a value type (<c>struct</c> or <c>record struct</c>).</summary>
    public TypeShape MustBeValueType(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE021",
            defaultTitle: "Type must be a value type",
            defaultMessage: "Type '{0}' must be a value type",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.IsValueType,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>
    ///     Require the type to be a <c>record struct</c>. Fires a single diagnostic
    ///     when the declaration is any other kind (class, record class, plain struct).
    ///     No auto-fix — rewriting to a record struct changes the type's identity and
    ///     semantics enough that the repair is left to the user.
    /// </summary>
    public TypeShape MustBeRecordStruct(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE032",
            defaultTitle: "Type must be a record struct",
            defaultMessage: "Type '{0}' must be declared as 'record struct'",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.IsRecord && sym.IsValueType,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>
    ///     Require the type to be declared <c>readonly</c>. Vacuously satisfied for
    ///     reference types — the <c>readonly</c> modifier is a value-type concept, so
    ///     non-value-types pass this check. Pair with <see cref="MustBeValueType"/>
    ///     or a kind filter if you want a non-struct to also be rejected.
    /// </summary>
    public TypeShape MustBeReadOnly(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE033",
            defaultTitle: "Type must be readonly",
            defaultMessage: "Type '{0}' must be declared 'readonly'",
            defaultSeverity: DiagnosticSeverity.Warning,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.AddReadOnlyModifier,
            predicate: static (sym, _, _) => !sym.IsValueType || sym.IsReadOnly,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>
    ///     Forbid the type from being nested inside another type. No auto-fix —
    ///     moving a declaration out of its containing type has too many user-level
    ///     consequences (accessibility, references, file layout) for Scribe to
    ///     automate.
    /// </summary>
    public TypeShape MustNotBeNested(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE034",
            defaultTitle: "Type must not be nested",
            defaultMessage: "Type '{0}' must be declared at the top level, not nested inside another type",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => sym.ContainingType is null,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    /// <summary>Forbid the type from being a value type.</summary>
    public TypeShape MustNotBeValueType(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE022",
            defaultTitle: "Type must not be a value type",
            defaultMessage: "Type '{0}' must not be a value type",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.TypeKeyword,
            defaultFix: FixKind.None,
            predicate: static (sym, _, _) => !sym.IsValueType,
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    // ───────────────────────────────────────────────────────────────
    //  Phase 8.5 — constructors
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Require a public parameterless constructor. Satisfied by the implicit
    ///     default constructor on classes with no other instance constructors; for
    ///     value types the default ctor is always present. Primary constructors on
    ///     records and structs are not considered parameterless.
    /// </summary>
    public TypeShape MustHaveParameterlessConstructor(DiagnosticSpec? spec = null)
    {
        AddCheck(
            defaultId: "SCRIBE031",
            defaultTitle: "Type must have a public parameterless constructor",
            defaultMessage: "Type '{0}' must declare a public parameterless constructor",
            defaultSeverity: DiagnosticSeverity.Error,
            defaultSquiggle: SquiggleAt.Identifier,
            defaultFix: FixKind.AddParameterlessConstructor,
            predicate: static (sym, _, _) => HasPublicParameterlessCtor(sym),
            messageArgs: static sym => EquatableArray.Create(sym.Name),
            spec: spec);
        return this;
    }

    // ───────────────────────────────────────────────────────────────
    //  Predicate helpers (static, closure-free)
    // ───────────────────────────────────────────────────────────────

    internal static bool IsPartial(INamedTypeSymbol symbol, CancellationToken ct)
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

    private static bool HasPublicParameterlessCtor(INamedTypeSymbol symbol)
    {
        // Value types always have a default parameterless constructor.
        if (symbol.IsValueType)
        {
            return true;
        }

        // Static classes never need a constructor.
        if (symbol.IsStatic)
        {
            return true;
        }

        // Roslyn surfaces the compiler-synthesised public default constructor in
        // InstanceConstructors, so a single check covers implicit + explicit ctors.
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0
                && ctor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasAttribute(INamedTypeSymbol symbol, string metadataName)
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

    // ───────────────────────────────────────────────────────────────
    //  Escape hatches — custom type-level check, member-level iteration
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Register a custom type-level check whose predicate the primitive catalog
    ///     does not cover. Returns <see langword="true"/> when the shape is
    ///     satisfied; <see langword="false"/> reports one diagnostic per matched type.
    /// </summary>
    /// <param name="predicate">
    ///     Positive predicate — <see langword="true"/> means the shape is satisfied.
    /// </param>
    /// <param name="id">Diagnostic ID (e.g. <c>WORD9000</c>).</param>
    /// <param name="title">One-line diagnostic title.</param>
    /// <param name="message">Message format — first arg <c>{0}</c> is the type name.</param>
    /// <param name="severity">Severity (defaults to <see cref="DiagnosticSeverity.Warning"/>).</param>
    /// <param name="squiggle">Anchor where the diagnostic lands.</param>
    /// <param name="fix">Auto-fix kind. <see cref="FixKind.Custom"/> pairs with <paramref name="customFixTag"/>.</param>
    /// <param name="customFixTag">Tag for lookup when <paramref name="fix"/> is <see cref="FixKind.Custom"/>.</param>
    /// <param name="messageArgs">
    ///     Additional message arguments beyond the type name (<c>{1}</c>, <c>{2}</c>, ...).
    ///     Optional — default yields just the type name as <c>{0}</c>.
    /// </param>
    /// <param name="properties">
    ///     Optional factory producing custom <see cref="Diagnostic.Properties"/>. Invoked
    ///     once per reported diagnostic with the offending symbol. Useful when a paired
    ///     code fixer needs structured context (e.g. conflicting attribute names, target
    ///     type names) that cannot be recovered from the squiggle location alone.
    ///     Reserved keys (<c>fixKind</c>, <c>squiggleAt</c>, <c>customFixTag</c>) are
    ///     added by the analyzer host after the user-supplied properties and win on
    ///     collision.
    /// </param>
    public TypeShape Check(
        Func<INamedTypeSymbol, Compilation, CancellationToken, bool> predicate,
        string id,
        string title,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Warning,
        SquiggleAt squiggle = SquiggleAt.Identifier,
        FixKind fix = FixKind.None,
        string? customFixTag = null,
        Func<INamedTypeSymbol, EquatableArray<string>>? messageArgs = null,
        Func<INamedTypeSymbol, ImmutableDictionary<string, string?>>? properties = null)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Diagnostic id must not be empty.", nameof(id));
        }

        var args = messageArgs ?? (static sym => EquatableArray.Create(sym.Name));

        Func<TypeFocus, ImmutableDictionary<string, string?>>? fixPropsDelegate = null;
        if (properties is not null)
        {
            fixPropsDelegate = focus => properties(focus.Symbol);
        }
        else if (fix == FixKind.Custom && !string.IsNullOrEmpty(customFixTag))
        {
            var tagProps = ImmutableDictionary<string, string?>.Empty.Add("customFixTag", customFixTag);
            fixPropsDelegate = _ => tagProps;
        }

        _checks.Add(new ShapeCheck(
            Id: InternPool.Intern(id),
            Title: title,
            MessageFormat: message,
            Severity: severity,
            SquiggleAt: squiggle,
            FixKind: fix,
            Predicate: (focus, comp, ct) => predicate(focus.Symbol, comp, ct),
            MessageArgs: focus => args(focus.Symbol),
            FixProperties: fixPropsDelegate));
        return this;
    }

    /// <summary>
    ///     Iterate declared members of each matched type. For each member satisfying
    ///     <paramref name="match"/>, emit one diagnostic (anchored per
    ///     <see cref="MemberDiagnosticSpec.Squiggle"/> on that member) with the
    ///     message arguments returned by <paramref name="messageArgs"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="match"/> is an <em>offender</em> predicate —
    ///         <see langword="true"/> means "this member violates the shape". Contrast
    ///         with type-level checks where the predicate is positive.
    ///     </para>
    ///     <para>
    ///         Implicitly-declared members (synthesized record constructor parameters,
    ///         value-equality backing, etc.) are filtered before <paramref name="match"/>
    ///         runs. Members without source syntax references are also skipped.
    ///     </para>
    /// </remarks>
    /// <param name="match">Offender predicate — <see langword="true"/> reports a diagnostic.</param>
    /// <param name="spec">Diagnostic descriptor.</param>
    /// <param name="messageArgs">Message argument builder. <c>{0}</c> defaults to the type name; the user supplies any remaining args (typically the member name).</param>
    /// <param name="properties">
    ///     Optional factory producing custom <see cref="Diagnostic.Properties"/>. Invoked
    ///     once per reported diagnostic with the declaring type symbol and the offending
    ///     member symbol. Reserved keys (<c>fixKind</c>, <c>memberSquiggleAt</c>,
    ///     <c>memberName</c>, <c>customFixTag</c>) are added by the analyzer host after
    ///     the user-supplied properties and win on collision.
    /// </param>
    public TypeShape ForEachMember(
        Func<ISymbol, bool> match,
        MemberDiagnosticSpec spec,
        Func<INamedTypeSymbol, ISymbol, EquatableArray<string>>? messageArgs = null,
        Func<INamedTypeSymbol, ISymbol, ImmutableDictionary<string, string?>>? properties = null)
    {
        if (match is null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        if (string.IsNullOrEmpty(spec.Id))
        {
            throw new ArgumentException("MemberDiagnosticSpec.Id must not be empty.", nameof(spec));
        }

        var args = messageArgs ?? (static (type, member) => EquatableArray.Create(type.Name, member.Name));

        Func<INamedTypeSymbol, ISymbol, ImmutableDictionary<string, string?>>? fixProps = properties;
        if (fixProps is null && spec.Fix == FixKind.Custom && !string.IsNullOrEmpty(spec.CustomFixTag))
        {
            var tag = spec.CustomFixTag!;
            fixProps = (_, _) => ImmutableDictionary<string, string?>.Empty.Add("customFixTag", tag);
        }

        _memberChecks.Add(new MemberCheck(
            Id: InternPool.Intern(spec.Id),
            Title: spec.Title,
            MessageFormat: spec.Message,
            Severity: spec.Severity,
            SquiggleAt: spec.Squiggle,
            FixKind: spec.Fix,
            CustomFixTag: spec.CustomFixTag,
            Match: match,
            MessageArgs: args,
            FixProperties: fixProps));
        return this;
    }
}
