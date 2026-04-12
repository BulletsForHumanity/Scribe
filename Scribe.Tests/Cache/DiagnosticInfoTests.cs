using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Scribe.Cache;

namespace Scribe.Tests.Cache;

public class DiagnosticInfoTests
{
    private static readonly DiagnosticDescriptor TestDescriptor = new(
        id: "SCRIBE001",
        title: "Test rule",
        messageFormat: "Type '{0}' must be partial",
        category: "Scribe.Test",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    [Fact]
    public void Materialize_ProducesDiagnostic_WithSameId()
    {
        var info = new DiagnosticInfo(
            Id: "SCRIBE001",
            Severity: DiagnosticSeverity.Error,
            MessageArgs: EquatableArray.Create("Foo"),
            Location: null);

        var diag = info.Materialize(TestDescriptor);

        diag.Id.ShouldBe("SCRIBE001");
        diag.Severity.ShouldBe(DiagnosticSeverity.Error);
    }

    [Fact]
    public void Materialize_FormatsMessageArgs()
    {
        var info = new DiagnosticInfo(
            Id: "SCRIBE001",
            Severity: DiagnosticSeverity.Error,
            MessageArgs: EquatableArray.Create("Bar"),
            Location: null);

        var diag = info.Materialize(TestDescriptor);

        diag.GetMessage(CultureInfo.InvariantCulture).ShouldBe("Type 'Bar' must be partial");
    }

    [Fact]
    public void Materialize_WithNullLocation_UsesLocationNone()
    {
        var info = new DiagnosticInfo(
            Id: "SCRIBE001",
            Severity: DiagnosticSeverity.Error,
            MessageArgs: EquatableArray<string>.Empty,
            Location: null);

        var diag = info.Materialize(TestDescriptor);

        diag.Location.ShouldBe(Location.None);
    }

    [Fact]
    public void Materialize_WithLocation_UsesProvidedLocation()
    {
        var location = new LocationInfo(
            FilePath: "test.cs",
            TextSpan: new TextSpan(0, 5),
            LineSpan: new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 5)));
        var info = new DiagnosticInfo(
            Id: "SCRIBE001",
            Severity: DiagnosticSeverity.Error,
            MessageArgs: EquatableArray<string>.Empty,
            Location: location);

        var diag = info.Materialize(TestDescriptor);

        diag.Location.GetLineSpan().Path.ShouldBe("test.cs");
        diag.Location.SourceSpan.ShouldBe(new TextSpan(0, 5));
    }

    [Fact]
    public void Value_Equality_Works()
    {
        var a = new DiagnosticInfo("SCRIBE001", DiagnosticSeverity.Warning,
            EquatableArray.Create("X"), null);
        var b = new DiagnosticInfo("SCRIBE001", DiagnosticSeverity.Warning,
            EquatableArray.Create("X"), null);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_Ids_Not_Equal()
    {
        var a = new DiagnosticInfo("SCRIBE001", DiagnosticSeverity.Warning,
            EquatableArray<string>.Empty, null);
        var b = new DiagnosticInfo("SCRIBE002", DiagnosticSeverity.Warning,
            EquatableArray<string>.Empty, null);

        a.ShouldNotBe(b);
    }
}
