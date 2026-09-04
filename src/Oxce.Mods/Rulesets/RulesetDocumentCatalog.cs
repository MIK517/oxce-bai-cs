using Oxce.Formats.Yaml;
using Oxce.Mods.Discovery;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets;

public sealed class RulesetDocumentCatalog
{
    private readonly RulesetDocument[] _documents;

    private RulesetDocumentCatalog(RulesetDocument[] documents, int parsedFileCount)
    {
        _documents = documents;
        ParsedFileCount = parsedFileCount;
    }

    public int ParsedFileCount { get; }

    internal IReadOnlyList<RulesetDocument> Documents => _documents;

    internal RulesetDocumentCatalog Filter(Func<ModCandidate, VirtualFileEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new RulesetDocumentCatalog(
            _documents.Where(document => predicate(document.Mod, document.File)).ToArray(),
            ParsedFileCount);
    }

    public static RulesetDocumentCatalog Parse(
        ModLoadPlan plan,
        RulesetCompositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            throw new ArgumentException("Cannot parse rules from an invalid mod load plan.", nameof(plan));
        }

        options ??= new RulesetCompositionOptions();
        options.Validate();
        var documents = new List<RulesetDocument>();
        var parsedFileCount = 0;
        foreach (var group in plan.Groups)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            foreach (var file in group.Rulesets)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                parsedFileCount = checked(parsedFileCount + 1);
                using var input = file.OpenRead();
                var stream = YamlCompatibilityReader.Parse(input, file.SourcePath, options.Yaml);
                options.CancellationToken.ThrowIfCancellationRequested();
                if (stream.Documents.Count == 0)
                {
                    continue;
                }

                if (stream.Documents.Count != 1)
                {
                    throw new YamlFormatException(
                        "Ruleset files must contain exactly one YAML document.",
                        stream.Documents[1].Span);
                }

                if (stream.Documents[0].Root is YamlNullNode)
                {
                    continue;
                }

                if (stream.Documents[0].Root is not YamlMappingNode root)
                {
                    throw new YamlFormatException(
                        "Ruleset document root must be a mapping.",
                        stream.Documents[0].Root.Span);
                }

                documents.Add(new RulesetDocument(parsedFileCount - 1, group.Mod, file, root));
            }
        }

        return new RulesetDocumentCatalog(documents.ToArray(), parsedFileCount);
    }
}

internal sealed record RulesetDocument(
    int DocumentId,
    ModCandidate Mod,
    VirtualFileEntry File,
    YamlMappingNode Root);
