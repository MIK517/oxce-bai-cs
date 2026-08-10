using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Xunit;

namespace Oxce.UnitTests.Fixtures;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void NormalizeSortsPropertiesAndNormalizesNumbers()
    {
        var input = Encoding.UTF8.GetBytes("{\"z\":1.50,\"a\":true}");

        var normalized = CanonicalJson.Normalize(input);

        Assert.Equal("{\n  \"a\": true,\n  \"z\": 1.5\n}\n", normalized);
    }

    [Fact]
    public void SemanticallyEqualsIgnoresObjectPropertyOrderAndNumberSpelling()
    {
        var expected = Encoding.UTF8.GetBytes("{\"a\":1.5,\"b\":[true,null]}");
        var actual = Encoding.UTF8.GetBytes("{\"b\":[true,null],\"a\":1.50}");

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    [Fact]
    public void NormalizeRejectsDuplicateProperties()
    {
        var input = Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}");

        Assert.Throws<JsonException>(() => CanonicalJson.Normalize(input));
    }
}
