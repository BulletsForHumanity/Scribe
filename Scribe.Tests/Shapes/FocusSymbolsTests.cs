using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scribe.Cache;
using Scribe.Shapes;
using Shouldly;
using Xunit;

namespace Scribe.Tests.Shapes;

/// <summary>
///     B.7 — exercises <see cref="FocusSymbols"/> cross-focus symbol comparison
///     helpers directly. Callers wire these into <c>Satisfy</c> predicates or
///     lens-configure callbacks to assert cross-focus identity (e.g. "the
///     navigated type argument equals the outer type being matched").
/// </summary>
public class FocusSymbolsTests
{
    [Fact]
    public void SymbolEquals_returns_true_for_same_symbol_reference()
    {
        var (widget, _) = CompileAndGetTypes();
        FocusSymbols.SymbolEquals(widget, widget).ShouldBeTrue();
    }

    [Fact]
    public void SymbolEquals_returns_false_for_different_types()
    {
        var (widget, other) = CompileAndGetTypes();
        FocusSymbols.SymbolEquals(widget, other).ShouldBeFalse();
    }

    [Fact]
    public void SymbolEquals_returns_false_when_either_side_is_null()
    {
        var (widget, _) = CompileAndGetTypes();
        FocusSymbols.SymbolEquals(null, widget).ShouldBeFalse();
        FocusSymbols.SymbolEquals(widget, null).ShouldBeFalse();
        FocusSymbols.SymbolEquals(null, null).ShouldBeFalse();
    }

    [Fact]
    public void SameOriginalDefinition_matches_closed_generic_against_open_definition()
    {
        // List<int> and List<string> share an OriginalDefinition but differ under SymbolEquals.
        var source = @"
using System.Collections.Generic;
public class Widget {
    public List<int> A;
    public List<string> B;
}
";
        var compilation = Compile(source);
        var widget = compilation.GetTypeByMetadataName("Widget")!;
        var listOfInt = ((IFieldSymbol)widget.GetMembers("A").Single()).Type;
        var listOfString = ((IFieldSymbol)widget.GetMembers("B").Single()).Type;

        FocusSymbols.SymbolEquals(listOfInt, listOfString).ShouldBeFalse();
        FocusSymbols.SameOriginalDefinition(listOfInt, listOfString).ShouldBeTrue();
    }

    [Fact]
    public void SameOriginalDefinition_returns_false_for_unrelated_types()
    {
        var (widget, other) = CompileAndGetTypes();
        FocusSymbols.SameOriginalDefinition(widget, other).ShouldBeFalse();
    }

    [Fact]
    public void SameOriginalDefinition_returns_false_when_either_side_is_null()
    {
        var (widget, _) = CompileAndGetTypes();
        FocusSymbols.SameOriginalDefinition(null, widget).ShouldBeFalse();
        FocusSymbols.SameOriginalDefinition(widget, null).ShouldBeFalse();
        FocusSymbols.SameOriginalDefinition(null, null).ShouldBeFalse();
    }

    [Fact]
    public void TypeFocus_extension_compares_by_original_definition()
    {
        var (widget, other) = CompileAndGetTypes();
        var a = new TypeFocus(widget, widget.ToDisplayString(), origin: null);
        var b = new TypeFocus(widget, widget.ToDisplayString(), origin: null);
        var c = new TypeFocus(other, other.ToDisplayString(), origin: null);

        a.SameOriginalDefinition(b).ShouldBeTrue();
        a.SameOriginalDefinition(c).ShouldBeFalse();
    }

    [Fact]
    public void TypeArgFocus_extension_compares_against_TypeFocus()
    {
        var (widget, _) = CompileAndGetTypes();
        var typeFocus = new TypeFocus(widget, widget.ToDisplayString(), origin: null);
        var matchingArg = new TypeArgFocus(widget, widget.ToDisplayString(), index: 0, parentOrigin: null, origin: null);

        typeFocus.SameOriginalDefinition(matchingArg).ShouldBeTrue();
        matchingArg.SameOriginalDefinition(typeFocus).ShouldBeTrue();
    }

    [Fact]
    public void TypeArgFocus_extension_returns_false_for_mismatched_arg()
    {
        var (widget, other) = CompileAndGetTypes();
        var typeFocus = new TypeFocus(widget, widget.ToDisplayString(), origin: null);
        var wrongArg = new TypeArgFocus(other, other.ToDisplayString(), index: 0, parentOrigin: null, origin: null);

        typeFocus.SameOriginalDefinition(wrongArg).ShouldBeFalse();
    }

    private static (INamedTypeSymbol Widget, INamedTypeSymbol Other) CompileAndGetTypes()
    {
        var compilation = Compile("public class Widget { } public class Other { }");
        return (
            compilation.GetTypeByMetadataName("Widget")!,
            compilation.GetTypeByMetadataName("Other")!);
    }

    private static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
