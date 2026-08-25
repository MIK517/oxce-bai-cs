using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Presentation;

public sealed record RuleIndexReference(int Index, string ModId);

public sealed record InterfaceElement(
    int X,
    int Y,
    int Width,
    int Height,
    int Color,
    int Color2,
    int Border,
    int Custom,
    bool TftdMode);

public sealed record InterfaceRule(
    string Palette,
    string Parent,
    string BackgroundImage,
    string AlternateBackgroundImage,
    IReadOnlyList<KeyValuePair<string, string>> UpgradedBackgroundImages,
    string Music,
    RuleIndexReference? Sound,
    IReadOnlyDictionary<string, InterfaceElement> Elements);

public sealed record MusicRule(string Name, int CatalogPosition, float Normalization)
{
    public string ResolveName(string id) => Name.Length == 0 ? id : Name;
}

public sealed record SoundDefinitionRule(
    string File,
    IReadOnlyList<(int First, int Last)> SoundRanges,
    IReadOnlyList<int> Sounds);

public sealed record PaletteColor(int Red, int Green, int Blue);

public sealed record CustomPaletteRule(
    string Target,
    string File,
    IReadOnlyDictionary<int, PaletteColor> Palette);

public sealed record SlideshowHeader(string MusicId, int TransitionSeconds);

public sealed record SlideshowSlide(
    string ImagePath,
    string Caption,
    int Width,
    int Height,
    int X,
    int Y,
    int Color,
    int TransitionSeconds,
    int HorizontalAlignment,
    int VerticalAlignment);

public sealed record VideoRule(
    bool UseUfoAudioSequence,
    bool WinGame,
    bool LoseGame,
    IReadOnlyList<string> Videos,
    IReadOnlyList<string> AudioTracks,
    SlideshowHeader Slideshow,
    IReadOnlyList<SlideshowSlide> Slides);

internal sealed class InterfaceRuleBuilder(string id)
{
    public string Id { get; } = id;
    public string Palette { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string BackgroundImage { get; set; } = string.Empty;
    public string AlternateBackgroundImage { get; set; } = string.Empty;
    public List<KeyValuePair<string, string>> UpgradedBackgroundImages { get; set; } = [];
    public string Music { get; set; } = string.Empty;
    public RuleIndexReference? Sound { get; set; }
    public Dictionary<string, InterfaceElementBuilder> Elements { get; } = new(StringComparer.Ordinal);
}

internal sealed class InterfaceElementBuilder
{
    public int X { get; set; } = int.MaxValue;
    public int Y { get; set; } = int.MaxValue;
    public int Width { get; set; } = int.MaxValue;
    public int Height { get; set; } = int.MaxValue;
    public int Color { get; set; } = int.MaxValue;
    public int Color2 { get; set; } = int.MaxValue;
    public int Border { get; set; } = int.MaxValue;
    public int Custom { get; set; }
    public bool TftdMode { get; set; }
}

internal sealed class MusicRuleBuilder(string id)
{
    public string Id { get; } = id;
    public string Name { get; set; } = string.Empty;
    public int CatalogPosition { get; set; } = int.MaxValue;
    public float Normalization { get; set; } = 0.76f;
}

internal sealed class SoundDefinitionRuleBuilder(string id)
{
    public string Id { get; } = id;
    public string File { get; set; } = string.Empty;
    public List<(int First, int Last)> SoundRanges { get; } = [];
    public List<int> Sounds { get; } = [];
}

internal sealed class CustomPaletteRuleBuilder(string id)
{
    public string Id { get; } = id;
    public string Target { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public SortedDictionary<int, PaletteColor> Palette { get; set; } = [];
}

internal sealed class VideoRuleBuilder(string id)
{
    public string Id { get; } = id;
    public bool UseUfoAudioSequence { get; set; }
    public bool WinGame { get; set; } = id == "winGame";
    public bool LoseGame { get; set; } = id == "loseGame";
    public List<string> Videos { get; } = [];
    public List<string> AudioTracks { get; } = [];
    public string SlideshowMusicId { get; set; } = string.Empty;
    public int SlideshowTransitionSeconds { get; set; } = 30;
    public List<SlideshowSlide> Slides { get; } = [];
}

internal static class PresentationYaml
{
    public static RuleIndexReference ReadIndexReference(YamlNode node, string currentModId)
    {
        if (node is YamlMappingNode mapping)
        {
            var index = YamlValueReader.ReadInt32(mapping.TryGet("index", out var value)
                ? value!
                : throw new YamlFormatException("Index reference is missing 'index'.", node.Span));
            var mod = YamlValueReader.ReadString(mapping.TryGet("mod", out value)
                ? value!
                : throw new YamlFormatException("Index reference is missing 'mod'.", node.Span));
            return new RuleIndexReference(index, mod == "current" ? currentModId : mod);
        }

        return new RuleIndexReference(YamlValueReader.ReadInt32(node), currentModId);
    }

    public static (int First, int Second) ReadIntPair(YamlNode node) =>
        YamlValueReader.ReadPair(node, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32);

    public static string[] ReadStrings(YamlNode node) =>
        YamlValueReader.ReadSequence(node, YamlValueReader.ReadString);

}
