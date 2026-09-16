using Oxce.Core.Diagnostics;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;

internal sealed record CampaignUiAssets(IndexedSpriteFont Font, Func<string, string> Localize)
{
    /// <summary>
    /// Loads UI strings and the small font through the layered virtual files, which already
    /// map <c>common</c> below the game data and mods (FileMap::getSlice/getYAML).
    /// </summary>
    public static CampaignUiAssets Load(VirtualFileCatalog files, RuntimePresentationContent special,
        IDiagnosticSink? diagnostics = null, string language = "en-US")
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var locale in new[] { "en-US", language }.Distinct(StringComparer.Ordinal))
        {
            foreach (var file in files.GetSlice("Language/" + locale + ".yml").OfType<VirtualFileEntry>())
            {
                using var input = file.OpenRead();
                ReadLanguage(input, file.SourcePath);
            }
            if (special.Strings.TryGetValue(locale, out var extra))
                foreach (var pair in extra) labels[pair.Key] = pair.Value;
        }
        var font = LoadFont();
        return new(font, value => labels.GetValueOrDefault(value, value));

        void ReadLanguage(Stream input, string name)
        {
            var root = ReadMap(input, name, diagnostics);
            var map = root.Entries.Count > 0 && root.Entries[0].Value is YamlMappingNode wrapped ? wrapped : root;
            foreach (var entry in map.Entries)
            {
                if (entry.ScalarKey is not { } key || entry.Value is not YamlScalarNode) continue;
                var value = YamlValueReader.ReadString(entry.Value);
                if (value.Length != 0) labels[key] = value.Replace("{NEWLINE}", " ", StringComparison.Ordinal)
                    .Replace("{SMALLLINE}", " ", StringComparison.Ordinal).Replace("{ALT}", "", StringComparison.Ordinal);
            }
        }

        IndexedSpriteFont LoadFont()
        {
            var fontPath = "Language/" + special.FontName;
            using var input = Open(fontPath);
            if (input is null) return IndexedInterfaceFont.Create();
            var root = ReadMap(input, fontPath, diagnostics);
            if (!root.TryGet("fonts", out var node) || node is not YamlSequenceNode sequence)
                throw new InvalidDataException("Font.dat requires a fonts sequence.");
            var definition = sequence.Items.OfType<YamlMappingNode>().FirstOrDefault(m => String(m, "id") == "FONT_SMALL");
            if (definition is null) return IndexedInterfaceFont.Create();
            var width = Integer(definition, "width", 0);
            var height = Integer(definition, "height", 0);
            var spacing = Integer(definition, "spacing", 0);
            if (!definition.TryGet("images", out node) || node is not YamlSequenceNode images)
                throw new InvalidDataException("FONT_SMALL requires an images sequence.");
            var projected = new List<IndexedSpriteFontImage>();
            foreach (var image in images.Items)
            {
                var map = image as YamlMappingNode ?? throw new InvalidDataException("Font images must be mappings.");
                var path = "Language/" + String(map, "file");
                using var bytes = Open(path) ?? throw new FileNotFoundException("Font image is missing.", path);
                var data = BinaryDataReader.FromStream(bytes);
                var decoded = path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                    ? IndexedBmpCodec.Decode(data) : IndexedPngCodec.Decode(data);
                var surface = new IndexedSurface(decoded.Width, decoded.Height);
                decoded.Pixels.Span.CopyTo(surface.Pixels);
                projected.Add(new(surface, Integer(map, "width", width), Integer(map, "height", height),
                    Integer(map, "spacing", spacing), String(map, "chars").TrimEnd('\r', '\n')));
            }
            var mono = definition.TryGet("monospace", out var monospace) && YamlValueReader.ReadBoolean(monospace!);
            return new(projected, mono);
        }

        Stream? Open(string relativePath)
        {
            return files.TryGet(relativePath, out var file) ? file!.OpenRead() : null;
        }
    }

    private static YamlMappingNode ReadMap(Stream input, string name, IDiagnosticSink? diagnostics)
    {
        var document = YamlCompatibilityReader.Parse(input, name);
        YamlCompatibilityReader.ReportLegacyEncoding(document, diagnostics);
        return document.Documents.Count == 1 && document.Documents[0].Root is YamlMappingNode map
            ? map : throw new InvalidDataException($"'{name}' requires one YAML mapping.");
    }

    private static int Integer(YamlMappingNode map, string key, int fallback) => map.TryGet(key, out var node)
        ? YamlValueReader.ReadInt32(node!) : fallback;
    private static string String(YamlMappingNode map, string key) => map.TryGet(key, out var node)
        ? YamlValueReader.ReadString(node!) : "";
}
