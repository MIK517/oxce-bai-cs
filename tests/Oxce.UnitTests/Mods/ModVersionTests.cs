using Oxce.Mods.Metadata;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ModVersionTests
{
    [Theory]
    [InlineData("1.10", "1.2")]
    [InlineData("2A", "2")]
    [InlineData("release3", "RELEASE2")]
    [InlineData("1.0", "1")]
    public void ReferenceNormalizationOrdersProvidedVersionAtOrAboveRequired(string provided, string required)
    {
        Assert.True(ModVersion.Parse(provided).Satisfies(ModVersion.Parse(required)));
    }

    [Theory]
    [InlineData("1..2", "duplicated dots")]
    [InlineData("release 3", "unexpected symbol")]
    [InlineData("12345678901", "unsupported number length")]
    public void InvalidReferenceSpellingsRetainTextAndReason(string text, string reason)
    {
        var version = ModVersion.Parse(text);

        Assert.False(version.IsValid);
        Assert.Equal(text, version.Text);
        Assert.Equal(reason, version.Error);
        Assert.True(version.Satisfies(ModVersion.Parse(text)));
    }
}
