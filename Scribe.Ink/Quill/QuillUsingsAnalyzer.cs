using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Scribe.Ink;

/// <summary>
///     SCRIBE300 — flags <c>global::Ns.Type</c> references emitted into a Quill
///     builder when no <c>Using(...)</c> registration on the same containing type
///     covers <c>Ns</c>. Quill's <c>ResolveGlobalReferences</c> only shortens
///     prefixes that match a registered namespace; unmatched references survive
///     verbatim into the generated output. This analyzer catches the oversight
///     at edit time.
///     <para>
///         Scope is per containing <c>INamedTypeSymbol</c>: registered usings and
///         emitted globals from any method on the type are aggregated, then matched.
///         Types with no local <c>new Quill()</c> creation site are skipped — the
///         Quill came from outside, and we cannot reason about its registered usings.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QuillUsingsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SCRIBE300";

    private static readonly DiagnosticDescriptor Descriptor = new(
        id: DiagnosticId,
        title: "global:: reference will not be shortened by Quill",
        messageFormat: "'global::{0}' is not covered by any registered Using on this Quill instance — Quill will emit it verbatim. Add q.Using(\"{1}\") to the builder.",
        category: "Scribe.Quill.Correctness",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Quill's ResolveGlobalReferences shortens 'global::Ns.Type' to 'Type' only when 'Ns' is registered via Using() or Usings(). Unregistered references survive into the generated file as fully-qualified names.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptor);

    /// <summary>
    ///     Pattern for <c>global::Ns.Type</c> references. Requires at least one
    ///     dot after <c>global::</c> — references like <c>global::Foo</c> with no
    ///     namespace segment cannot be covered by any using and are out of scope.
    /// </summary>
    private static readonly Regex GlobalRefPattern = new(
        @"global::(?<path>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)",
        RegexOptions.Compiled);

    private static readonly string[] QuillContentMethodNames =
    {
        "Line", "Lines", "AppendRaw", "Comment", "Header",
        "Block", "SwitchExpr", "Case", "Region", "ListInit",
        "Summary", "Remarks", "Param", "Returns",
    };

    private static readonly string[] BlockScopeContentMethodNames =
    {
        "Summary", "Remarks", "Param", "TypeParam", "Returns",
        "InheritDoc", "Example", "Exception", "SeeAlso", "Attribute",
    };

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var quillType = context.Compilation.GetTypeByMetadataName("Scribe.Quill");
        if (quillType is null)
        {
            return;
        }

        var blockScopeType = context.Compilation.GetTypeByMetadataName("Scribe.Quill+BlockScope");

        var registrationBuilder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        registrationBuilder.UnionWith(quillType.GetMembers("Using").OfType<IMethodSymbol>());
        registrationBuilder.UnionWith(quillType.GetMembers("Usings").OfType<IMethodSymbol>());
        var registrationSet = registrationBuilder.ToImmutable();

        var quillContentSet = BuildMethodSet(quillType, QuillContentMethodNames);
        var blockScopeContentSet = blockScopeType is null
            ? ImmutableHashSet<ISymbol>.Empty
            : BuildMethodSet(blockScopeType, BlockScopeContentMethodNames);

        context.RegisterSymbolStartAction(symbolStart =>
        {
            if (symbolStart.Symbol is not INamedTypeSymbol)
            {
                return;
            }

            var facts = new TypeQuillFacts();

            symbolStart.RegisterOperationAction(
                ctx => OnInvocation(ctx, facts, quillType, blockScopeType, registrationSet, quillContentSet, blockScopeContentSet),
                OperationKind.Invocation);

            symbolStart.RegisterOperationAction(
                ctx => OnObjectCreation(ctx, facts, quillType),
                OperationKind.ObjectCreation);

            symbolStart.RegisterSymbolEndAction(end => facts.Report(end));
        }, SymbolKind.NamedType);
    }

    private static ImmutableHashSet<ISymbol> BuildMethodSet(INamedTypeSymbol type, string[] names)
    {
        var builder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var name in names)
        {
            foreach (var member in type.GetMembers(name).OfType<IMethodSymbol>())
            {
                builder.Add(member);
            }
        }

        return builder.ToImmutable();
    }

    private static void OnInvocation(
        OperationAnalysisContext context,
        TypeQuillFacts facts,
        INamedTypeSymbol quillType,
        INamedTypeSymbol? blockScopeType,
        ImmutableHashSet<ISymbol> registrationSet,
        ImmutableHashSet<ISymbol> quillContentSet,
        ImmutableHashSet<ISymbol> blockScopeContentSet)
    {
        if (context.Operation is not IInvocationOperation invocation)
        {
            return;
        }

        var receiverType = invocation.Instance?.Type;
        if (receiverType is null)
        {
            return;
        }

        var orig = invocation.TargetMethod.OriginalDefinition;

        var isQuill = SymbolEqualityComparer.Default.Equals(receiverType, quillType);
        var isBlockScope = blockScopeType is not null
            && SymbolEqualityComparer.Default.Equals(receiverType, blockScopeType);

        if (!isQuill && !isBlockScope)
        {
            return;
        }

        // Registration methods live on Quill only.
        if (isQuill && registrationSet.Contains(orig))
        {
            foreach (var arg in invocation.Arguments)
            {
                CollectRegistrationLiterals(arg.Value, facts);
            }

            return;
        }

        var contentSet = isQuill ? quillContentSet : blockScopeContentSet;
        if (!contentSet.Contains(orig))
        {
            return;
        }

        foreach (var arg in invocation.Arguments)
        {
            ScanContentArgument(arg.Value, facts);
        }
    }

    private static void OnObjectCreation(
        OperationAnalysisContext context,
        TypeQuillFacts facts,
        INamedTypeSymbol quillType)
    {
        if (context.Operation is not IObjectCreationOperation creation)
        {
            return;
        }

        if (creation.Type is null)
        {
            return;
        }

        if (SymbolEqualityComparer.Default.Equals(creation.Type, quillType))
        {
            facts.HasLocalCreation = true;
        }
    }

    private static void CollectRegistrationLiterals(IOperation argValue, TypeQuillFacts facts)
    {
        switch (argValue)
        {
            case ILiteralOperation lit
                when lit.ConstantValue.HasValue && lit.ConstantValue.Value is string s:
                // Mirror Quill.ResolveGlobalReferences's filter at Quill.Usings.cs:120 —
                // alias-formatted entries ("AliasName = Ns.Type") are skipped during resolution
                // and so should not count as registered namespaces.
                if (s.IndexOf('=') < 0)
                {
                    facts.RegisteredUsings.Add(s);
                }

                break;
            case IArrayCreationOperation array when array.Initializer is not null:
                foreach (var element in array.Initializer.ElementValues)
                {
                    CollectRegistrationLiterals(element, facts);
                }

                break;
            case IConversionOperation conv:
                CollectRegistrationLiterals(conv.Operand, facts);
                break;
            default:
                // Computed namespace argument — we cannot statically reason about
                // what was registered. Silence rather than risk false positives.
                facts.Tainted = true;
                break;
        }
    }

    private static void ScanContentArgument(IOperation argValue, TypeQuillFacts facts)
    {
        foreach (var (text, location) in ExtractTextPortions(argValue))
        {
            foreach (Match match in GlobalRefPattern.Matches(text))
            {
                if (match.Groups["path"] is not { Success: true } pathGroup)
                {
                    continue;
                }

                facts.EmittedGlobals.Add((pathGroup.Value, location));
            }
        }
    }

    private static IEnumerable<(string Text, Location Location)> ExtractTextPortions(IOperation op)
    {
        switch (op)
        {
            case ILiteralOperation lit
                when lit.ConstantValue.HasValue && lit.ConstantValue.Value is string s:
                yield return (s, lit.Syntax.GetLocation());
                break;

            case IInterpolatedStringOperation interp:
                foreach (var part in interp.Parts)
                {
                    if (part is not IInterpolatedStringTextOperation textPart)
                    {
                        // IInterpolationOperation children are dynamic — skip.
                        continue;
                    }

                    if (textPart.Text is ILiteralOperation textLit
                        && textLit.ConstantValue.HasValue
                        && textLit.ConstantValue.Value is string ts)
                    {
                        yield return (ts, textPart.Syntax.GetLocation());
                    }
                }

                break;

            case IArrayCreationOperation array when array.Initializer is not null:
                foreach (var element in array.Initializer.ElementValues)
                {
                    foreach (var item in ExtractTextPortions(element))
                    {
                        yield return item;
                    }
                }

                break;

            case IConversionOperation conv:
                foreach (var item in ExtractTextPortions(conv.Operand))
                {
                    yield return item;
                }

                break;
        }
    }

    private sealed class TypeQuillFacts
    {
        public HashSet<string> RegisteredUsings { get; } = new(StringComparer.Ordinal);

        public List<(string Path, Location Location)> EmittedGlobals { get; } = new();

        public bool HasLocalCreation { get; set; }

        public bool Tainted { get; set; }

        public void Report(SymbolAnalysisContext context)
        {
            if (!HasLocalCreation || Tainted)
            {
                return;
            }

            // Mirror Quill.ResolveGlobalReferences sort: longest namespace (most dots) first,
            // then by length. Either ordering would suffice for "any prefix matches" — sorted
            // here for symmetry with Quill's own resolution algorithm.
            var sorted = RegisteredUsings
                .OrderByDescending(s => CountDots(s))
                .ThenByDescending(s => s.Length)
                .ToList();

            foreach (var (path, location) in EmittedGlobals)
            {
                if (IsCovered(path, sorted))
                {
                    continue;
                }

                var lastDot = path.LastIndexOf('.');
                var suggested = lastDot > 0 ? path.Substring(0, lastDot) : path;
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, path, suggested));
            }
        }

        private static bool IsCovered(string path, List<string> sortedUsings)
        {
            foreach (var reg in sortedUsings)
            {
                // Quill matches "global::<reg>." against the body — i.e. <reg> must be a
                // strict namespace prefix of the captured path with a trailing dot. This
                // mirrors the Replace("global::" + ns + ".", "") at Quill.Usings.cs:139.
                if (path.StartsWith(reg + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountDots(string s)
        {
            var count = 0;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '.')
                {
                    count++;
                }
            }

            return count;
        }
    }
}
