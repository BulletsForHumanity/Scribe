namespace Scribe.Tests;

public class NamingTests
{
    // ───────────────────────────────────────────────────────────────
    //  ToKebabCase
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("Hello", "hello")]
    [InlineData("ManifestIndex", "manifest-index")]
    [InlineData("ManifestDailyLog", "manifest-daily-log")]
    [InlineData("A", "a")]
    // All-caps words — the core bug
    [InlineData("EMANATE", "emanate")]
    [InlineData("EMANATED", "emanated")]
    [InlineData("IO", "io")]
    // Acronym followed by PascalCase word
    [InlineData("HTTPServer", "http-server")]
    [InlineData("XMLParser", "xml-parser")]
    [InlineData("IOStream", "io-stream")]
    // PascalCase word followed by acronym
    [InlineData("ParseXML", "parse-xml")]
    [InlineData("GetHTTPClient", "get-http-client")]
    [InlineData("ReadIO", "read-io")]
    // Acronym in the middle
    [InlineData("ParseXMLDocument", "parse-xml-document")]
    [InlineData("GetHTTPSConnection", "get-https-connection")]
    [InlineData("UseAOTCompiler", "use-aot-compiler")]
    // Single-letter boundaries
    [InlineData("AValue", "a-value")]
    [InlineData("ValueA", "value-a")]
    [InlineData("GetAValue", "get-a-value")]
    // All-caps followed by PascalCase word
    [InlineData("EMANATEUser", "emanate-user")]
    // Single letter between words
    [InlineData("MakeAMish", "make-a-mish")]
    // Already lowercase
    [InlineData("already", "already")]
    // Numbers
    [InlineData("Page1", "page1")]
    [InlineData("Page10Size", "page10-size")]
    [InlineData("Get2ndItem", "get2nd-item")]
    // Null
    [InlineData(null, null)]
    public void ToKebabCase(string? input, string? expected)
        => Naming.ToKebabCase(input!).ShouldBe(expected);

    // ───────────────────────────────────────────────────────────────
    //  ToTitleWords
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "hello")]
    [InlineData("Hello", "Hello")]
    [InlineData("ManifestIndex", "Manifest Index")]
    [InlineData("ManifestDailyLog", "Manifest Daily Log")]
    [InlineData("A", "A")]
    // All-caps words
    [InlineData("EMANATE", "EMANATE")]
    [InlineData("EMANATED", "EMANATED")]
    [InlineData("IO", "IO")]
    // Acronym followed by PascalCase word
    [InlineData("HTTPServer", "HTTP Server")]
    [InlineData("XMLParser", "XML Parser")]
    [InlineData("IOStream", "IO Stream")]
    // PascalCase word followed by acronym
    [InlineData("ParseXML", "Parse XML")]
    [InlineData("GetHTTPClient", "Get HTTP Client")]
    // Acronym in the middle
    [InlineData("ParseXMLDocument", "Parse XML Document")]
    [InlineData("GetHTTPSConnection", "Get HTTPS Connection")]
    // All-caps followed by PascalCase word
    [InlineData("EMANATEUser", "EMANATE User")]
    // Single letter between words
    [InlineData("MakeAMish", "Make A Mish")]
    // Single-letter boundaries
    [InlineData("AValue", "A Value")]
    [InlineData("ValueA", "Value A")]
    [InlineData("GetAValue", "Get A Value")]
    // Already lowercase
    [InlineData("already", "already")]
    // Numbers
    [InlineData("Page1", "Page1")]
    [InlineData("Page10Size", "Page10 Size")]
    // Null
    [InlineData(null, null)]
    public void ToTitleWords(string? input, string? expected)
        => Naming.ToTitleWords(input!).ShouldBe(expected);

    // ───────────────────────────────────────────────────────────────
    //  ToPascalCase
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "Hello")]
    [InlineData("daily-log", "DailyLog")]
    [InlineData("manifest-daily-log", "ManifestDailyLog")]
    [InlineData("a", "A")]
    [InlineData("a-b-c", "ABC")]
    [InlineData("already", "Already")]
    // Single segment, no hyphens
    [InlineData("word", "Word")]
    // Trailing/leading hyphens
    [InlineData("-leading", "Leading")]
    [InlineData("trailing-", "Trailing")]
    [InlineData("-both-", "Both")]
    // Double hyphens
    [InlineData("double--hyphen", "DoubleHyphen")]
    // Preserves existing uppercase within segments
    [InlineData("parse-XML", "ParseXML")]
    // Null
    [InlineData(null, null)]
    public void ToPascalCase(string? input, string? expected)
        => Naming.ToPascalCase(input!).ShouldBe(expected);

    // ───────────────────────────────────────────────────────────────
    //  ExtractXmlSummary
    // ───────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractXmlSummary_ReturnsEmpty_WhenNull()
        => Naming.ExtractXmlSummary(null).ShouldBe("");

    [Fact]
    public void ExtractXmlSummary_ReturnsEmpty_WhenEmpty()
        => Naming.ExtractXmlSummary("").ShouldBe("");

    [Fact]
    public void ExtractXmlSummary_ReturnsEmpty_WhenNoSummaryTag()
        => Naming.ExtractXmlSummary("<remarks>hello</remarks>").ShouldBe("");

    [Fact]
    public void ExtractXmlSummary_ExtractsSimpleSummary()
        => Naming.ExtractXmlSummary("<summary>Hello world</summary>").ShouldBe("Hello world");

    [Fact]
    public void ExtractXmlSummary_TrimsWhitespaceAndNewlines()
        => Naming.ExtractXmlSummary("<summary>\r\n    Some text\r\n    more text\r\n</summary>")
           .ShouldBe("Some text more text");

    // ───────────────────────────────────────────────────────────────
    //  EscapeStringLiteral
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("path\\to\\file", "path\\\\to\\\\file")]
    [InlineData("line1\r\nline2", "line1\\nline2")]
    [InlineData("line1\nline2", "line1\\nline2")]
    [InlineData("line1\rline2", "line1\\nline2")]
    public void EscapeStringLiteral(string input, string expected)
        => Naming.EscapeStringLiteral(input).ShouldBe(expected);

    // ───────────────────────────────────────────────────────────────
    //  Roundtrip: PascalCase → kebab → PascalCase
    // ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ManifestIndex")]
    [InlineData("DailyLog")]
    [InlineData("ManifestDailyLog")]
    [InlineData("Hello")]
    public void KebabRoundtrip_PascalCase(string pascal)
        => Naming.ToPascalCase(Naming.ToKebabCase(pascal)).ShouldBe(pascal);
}
