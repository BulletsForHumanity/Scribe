using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Scribe.Ink.Shapes;

/// <summary>
///     SCRIBE101 — forbid Roslyn reference types on any TModel passed to
///     <c>Shape&lt;TModel&gt;.Project&lt;TModel&gt;</c>. Holding an <c>ISymbol</c>,
///     <c>SyntaxNode</c>, <c>Compilation</c>, <c>SemanticModel</c>, <c>SyntaxTree</c>,
///     <c>Location</c>, or <c>AttributeData</c> in a cached model defeats the
///     incremental generator cache: those objects identity-compare per compilation.
///     Extract the primitive data you need into strings, equatable arrays, or
///     <see cref="Scribe.Cache.LocationInfo"/> instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CacheCorrectnessAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SCRIBE101";

    private static readonly DiagnosticDescriptor Descriptor = new(
        id: DiagnosticId,
        title: "Cache-hostile type in Shape projection model",
        messageFormat: "Member '{0}' of type '{1}' is a Roslyn reference type ('{2}') — storing it in a Shape projection model defeats incremental caching. Extract primitives or use LocationInfo instead.",
        category: "Scribe.Cache",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Descriptor);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var shapeBuilder = context.Compilation.GetTypeByMetadataName("Scribe.Shapes.ShapeBuilder");
        if (shapeBuilder is null)
        {
            return;
        }

        var forbidden = CollectForbiddenTypes(context.Compilation);
        if (forbidden.IsEmpty)
        {
            return;
        }

        context.RegisterOperationAction(
            ctx => AnalyzeInvocation(ctx, shapeBuilder, forbidden),
            OperationKind.Invocation);
    }

    private static ImmutableArray<INamedTypeSymbol> CollectForbiddenTypes(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        void Add(string metadataName)
        {
            var t = compilation.GetTypeByMetadataName(metadataName);
            if (t is not null)
            {
                builder.Add(t);
            }
        }

        Add("Microsoft.CodeAnalysis.ISymbol");
        Add("Microsoft.CodeAnalysis.SyntaxNode");
        Add("Microsoft.CodeAnalysis.SyntaxTree");
        Add("Microsoft.CodeAnalysis.SemanticModel");
        Add("Microsoft.CodeAnalysis.Compilation");
        Add("Microsoft.CodeAnalysis.Location");
        Add("Microsoft.CodeAnalysis.AttributeData");

        return builder.ToImmutable();
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol shapeBuilder,
        ImmutableArray<INamedTypeSymbol> forbidden)
    {
        if (context.Operation is not IInvocationOperation invocation)
        {
            return;
        }

        var method = invocation.TargetMethod;
        if (method.Name != "Project"
            || method.TypeArguments.Length != 1
            || !SymbolEqualityComparer.Default.Equals(method.OriginalDefinition.ContainingType, shapeBuilder))
        {
            return;
        }

        if (method.TypeArguments[0] is not INamedTypeSymbol model)
        {
            return;
        }

        foreach (var member in model.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            var memberType = member switch
            {
                IFieldSymbol f when !f.IsConst && !f.IsImplicitlyDeclared => f.Type,
                IPropertySymbol p when !p.IsIndexer => p.Type,
                _ => null,
            };

            if (memberType is null)
            {
                continue;
            }

            var offender = FindForbidden(memberType, forbidden);
            if (offender is null)
            {
                continue;
            }

            var location = member.Locations.Length > 0 ? member.Locations[0] : invocation.Syntax.GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                location,
                member.Name,
                model.Name,
                offender.ToDisplayString()));
        }
    }

    private static ITypeSymbol? FindForbidden(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> forbidden)
    {
        var current = type;
        while (current is not null)
        {
            foreach (var banned in forbidden)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, banned))
                {
                    return banned;
                }
            }

            foreach (var iface in current.AllInterfaces)
            {
                foreach (var banned in forbidden)
                {
                    if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, banned))
                    {
                        return banned;
                    }
                }
            }

            current = current.BaseType;
        }

        return null;
    }
}
