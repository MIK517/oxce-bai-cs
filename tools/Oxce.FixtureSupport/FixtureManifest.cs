using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Oxce.FixtureSupport;

public sealed class FixtureManifest
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public ReferenceMetadata Reference { get; init; } = new();

    public IReadOnlyList<FixtureInput> Inputs { get; init; } = [];

    public IReadOnlyList<string> Normalization { get; init; } = [];

    public string Expected { get; init; } = string.Empty;
}

public sealed class ReferenceMetadata
{
    public string Kind { get; init; } = string.Empty;

    public string Repository { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> BuildOptions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed class FixtureInput
{
    public string Path { get; init; } = string.Empty;

    public long Size { get; init; }

    public string Sha256 { get; init; } = string.Empty;
}

public static partial class FixtureManifestLoader
{
    private const int MaximumManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static FixtureManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(
            FixtureFile.ReadAllBytes(path, MaximumManifestBytes),
            SerializerOptions)
            ?? throw new JsonException("Fixture manifest must contain a JSON object.");
        Validate(manifest);
        return manifest;
    }

    public static void Validate(FixtureManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported fixture schema version {manifest.SchemaVersion}.");
        }

        if (!FixtureIdPattern().IsMatch(manifest.Id))
        {
            throw new InvalidDataException("Fixture id must use lowercase ASCII letters, digits, '.', '_', or '-'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Description))
        {
            throw new InvalidDataException("Fixture description is required.");
        }

        if (manifest.Reference.Kind is not ("cpp-reference" or "tool-self-test"))
        {
            throw new InvalidDataException($"Unsupported fixture reference kind '{manifest.Reference.Kind}'.");
        }

        if (manifest.Inputs.Count > 256)
        {
            throw new InvalidDataException("A fixture manifest cannot contain more than 256 inputs.");
        }

        if (manifest.Normalization.Count > 64)
        {
            throw new InvalidDataException("A fixture manifest cannot contain more than 64 normalization rules.");
        }

        ValidateRelativePath(manifest.Expected, "Expected output");
        foreach (var input in manifest.Inputs)
        {
            ValidateRelativePath(input.Path, "Input");
            if (input.Size < 0)
            {
                throw new InvalidDataException($"Input '{input.Path}' has a negative size.");
            }

            if (!Sha256Pattern().IsMatch(input.Sha256))
            {
                throw new InvalidDataException($"Input '{input.Path}' does not have a lowercase SHA-256 hash.");
            }
        }

        var duplicateInput = manifest.Inputs
            .GroupBy(static input => input.Path, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicateInput is not null)
        {
            throw new InvalidDataException($"Fixture input '{duplicateInput.Key}' is listed more than once.");
        }

        if (string.Equals(manifest.Reference.Kind, "cpp-reference", StringComparison.Ordinal) &&
            !CommitPattern().IsMatch(manifest.Reference.Commit))
        {
            throw new InvalidDataException("C++ reference fixtures must record a full 40-character commit hash.");
        }
    }

    private static void ValidateRelativePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"{label} path must be non-empty and relative.");
        }

        var segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(static segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"{label} path contains an invalid segment.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FixtureIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}

public static class FixtureManifestVerifier
{
    public static void VerifyFiles(FixtureManifest manifest, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);

        foreach (var input in manifest.Inputs)
        {
            var path = ResolveWithinRoot(root, input.Path);
            var digest = FileDigest.Calculate(path);
            if (digest.Size != input.Size || !string.Equals(digest.Sha256, input.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Fixture input '{input.Path}' does not match its recorded size and hash.");
            }
        }

        var expected = ResolveWithinRoot(root, manifest.Expected);
        if (!File.Exists(expected))
        {
            throw new FileNotFoundException("Fixture expected output was not found.", expected);
        }
    }

    private static string ResolveWithinRoot(string root, string relativePath)
    {
        var path = Path.GetFullPath(relativePath, root);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." ||
            relative.StartsWith(string.Concat("..", Path.DirectorySeparatorChar), StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"Fixture path '{relativePath}' escapes the repository root.");
        }

        return path;
    }
}
