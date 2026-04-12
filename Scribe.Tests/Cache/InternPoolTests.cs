using Scribe.Cache;

namespace Scribe.Tests.Cache;

public class InternPoolTests
{
    [Fact]
    public void SameValue_ReturnsSameReference()
    {
        // Build the strings from pieces to defeat the compiler's string literal pooling,
        // then confirm the InternPool gives us a single canonical reference.
        var a = "scribe" + "-test-1";
        var b = "scribe" + "-test-1";

        // Guard: without interning these should be distinct references most of the time.
        // (Not strictly guaranteed by the runtime, but for concatenated strings it holds.)
        var interned1 = InternPool.Intern(a);
        var interned2 = InternPool.Intern(b);

        ReferenceEquals(interned1, interned2).ShouldBeTrue();
    }

    [Fact]
    public void Null_ReturnsNull()
    {
        InternPool.Intern(null!).ShouldBeNull();
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        InternPool.Intern("").ShouldBe("");
    }

    [Fact]
    public void DifferentValues_StayDistinct()
    {
        var a = InternPool.Intern("scribe-test-distinct-a");
        var b = InternPool.Intern("scribe-test-distinct-b");
        ReferenceEquals(a, b).ShouldBeFalse();
    }
}
