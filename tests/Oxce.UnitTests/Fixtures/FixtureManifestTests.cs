using Oxce.FixtureSupport;
using Xunit;

namespace Oxce.UnitTests.Fixtures;

public sealed class FixtureManifestTests
{
    [Fact]
    public void ValidateRejectsParentDirectoryTraversal()
    {
        var manifest = CreateManifest("../private/input.json");

        Assert.Throws<InvalidDataException>(() => FixtureManifestLoader.Validate(manifest));
    }

    [Fact]
    public void ValidateRequiresFullCommitForCppReference()
    {
        var manifest = CreateManifest("fixtures/public/input.json", "cpp-reference", "abc123");

        Assert.Throws<InvalidDataException>(() => FixtureManifestLoader.Validate(manifest));
    }

    private static FixtureManifest CreateManifest(
        string inputPath,
        string referenceKind = "tool-self-test",
        string commit = "") =>
        new()
        {
            SchemaVersion = 1,
            Id = "manifest-test",
            Description = "Synthetic manifest used by unit tests.",
            Reference = new ReferenceMetadata { Kind = referenceKind, Commit = commit },
            Inputs =
            [
                new FixtureInput
                {
                    Path = inputPath,
                    Size = 0,
                    Sha256 = new string('0', 64),
                },
            ],
            Expected = "fixtures/expected/output.json",
        };
}
