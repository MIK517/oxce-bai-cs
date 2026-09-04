using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Presentation;

internal sealed class InterfaceRuleLoader : IdOnlyTypedRuleFamilyLoader<InterfaceRuleBuilder, InterfaceRule>
{
    public InterfaceRuleLoader() : base(GetSection("interfaces")) { }

    protected override InterfaceRuleBuilder Create(string id) => new(id);

    protected override void Apply(InterfaceRuleBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.Palette = reader.ReadString("palette", builder.Palette);
        builder.Parent = reader.ReadString("parent", builder.Parent);
        builder.BackgroundImage = reader.ReadString("backgroundImage", builder.BackgroundImage);
        builder.AlternateBackgroundImage = reader.ReadString("altBackgroundImage", builder.AlternateBackgroundImage);
        builder.Music = reader.ReadString("music", builder.Music);
        if (reader.TryGet("upgBackgroundImage", out var upgrades))
        {
            builder.UpgradedBackgroundImages = YamlValueReader.ReadSequence(upgrades!, item =>
            {
                var pair = YamlValueReader.ReadPair(item, YamlValueReader.ReadString, YamlValueReader.ReadString);
                return new KeyValuePair<string, string>(pair.First, pair.Second);
            }).ToList();
        }

        if (reader.TryGet("sound", out var sound))
        {
            builder.Sound = PresentationYaml.ReadIndexReference(sound!, reader.Source.ModId);
        }

        reader.ApplyMappingSequence("elements", element => ApplyElement(builder, element));
    }

    protected override InterfaceRule Freeze(InterfaceRuleBuilder builder)
    {
        var elements = builder.Elements.ToDictionary(
            pair => pair.Key,
            pair => new InterfaceElement(
                pair.Value.X, pair.Value.Y, pair.Value.Width, pair.Value.Height,
                pair.Value.Color, pair.Value.Color2, pair.Value.Border, pair.Value.Custom, pair.Value.TftdMode),
            StringComparer.Ordinal);
        return new InterfaceRule(
            builder.Palette,
            builder.Parent,
            builder.BackgroundImage,
            builder.AlternateBackgroundImage,
            builder.UpgradedBackgroundImages.AsReadOnly(),
            builder.Music,
            builder.Sound,
            new ReadOnlyDictionary<string, InterfaceElement>(elements));
    }

    private static void ApplyElement(InterfaceRuleBuilder builder, RulePropertyReader reader)
    {
        var id = reader.ReadString("id", string.Empty);
        if (id.Length == 0)
        {
            throw new YamlFormatException("Interface element is missing 'id'.", reader.Span);
        }

        if (!builder.Elements.TryGetValue(id, out var element))
        {
            element = new InterfaceElementBuilder();
            builder.Elements.Add(id, element);
        }

        if (reader.TryGet("size", out var size))
        {
            (element.Width, element.Height) = PresentationYaml.ReadIntPair(size!);
        }

        if (reader.TryGet("pos", out var position))
        {
            (element.X, element.Y) = PresentationYaml.ReadIntPair(position!);
        }

        element.Color = reader.ReadInt32("color", element.Color);
        element.Color2 = reader.ReadInt32("color2", element.Color2);
        element.Border = reader.ReadInt32("border", element.Border);
        element.Custom = reader.ReadInt32("custom", element.Custom);
        element.TftdMode = reader.ReadBoolean("TFTDMode", element.TftdMode);
    }

    private static RuleSectionDefinition GetSection(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();
}

internal sealed class MusicRuleLoader : IdOnlyTypedRuleFamilyLoader<MusicRuleBuilder, MusicRule>
{
    public MusicRuleLoader() : base(GetSection()) { }
    protected override MusicRuleBuilder Create(string id) => new(id);
    protected override void Apply(MusicRuleBuilder builder, RulePropertyReader reader)
    {
        builder.Name = reader.ReadString("name", builder.Name);
        builder.CatalogPosition = reader.ReadInt32("catPos", builder.CatalogPosition);
        builder.Normalization = reader.ReadSingle("normalization", builder.Normalization);
    }
    protected override MusicRule Freeze(MusicRuleBuilder builder) =>
        new(builder.Name, builder.CatalogPosition, builder.Normalization);
    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("musics", out var section)
        ? section! : throw new InvalidOperationException();
}

internal sealed class SoundDefinitionRuleLoader : IdOnlyTypedRuleFamilyLoader<SoundDefinitionRuleBuilder, SoundDefinitionRule>
{
    public SoundDefinitionRuleLoader() : base(GetSection()) { }
    protected override SoundDefinitionRuleBuilder Create(string id) => new(id);
    protected override void Apply(SoundDefinitionRuleBuilder builder, RulePropertyReader reader)
    {
        builder.File = reader.ReadString("file", builder.File);
        if (reader.TryGet("soundRanges", out var ranges))
        {
            builder.SoundRanges.AddRange(YamlValueReader.ReadSequence(ranges!, PresentationYaml.ReadIntPair));
        }
        if (reader.TryGet("sounds", out var sounds))
        {
            builder.Sounds.AddRange(YamlValueReader.ReadSequence(sounds!, YamlValueReader.ReadInt32));
        }
    }
    protected override SoundDefinitionRule Freeze(SoundDefinitionRuleBuilder builder) =>
        new(builder.File, builder.SoundRanges.AsReadOnly(), builder.Sounds.AsReadOnly());
    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("soundDefs", out var section)
        ? section! : throw new InvalidOperationException();
}

internal sealed class CustomPaletteRuleLoader : IdOnlyTypedRuleFamilyLoader<CustomPaletteRuleBuilder, CustomPaletteRule>
{
    public CustomPaletteRuleLoader() : base(GetSection()) { }
    protected override CustomPaletteRuleBuilder Create(string id) => new(id);
    protected override void Apply(CustomPaletteRuleBuilder builder, RulePropertyReader reader)
    {
        builder.Target = reader.ReadString("target", builder.Target);
        builder.File = reader.ReadString("file", builder.File);
        if (reader.TryGet("palette", out var palette))
        {
            builder.Palette = YamlValueReader.ReadMap(
                palette!,
                YamlValueReader.ReadInt32,
                item =>
                {
                    var color = YamlValueReader.ReadTuple(
                        item, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32);
                    return new PaletteColor(color.First, color.Second, color.Third);
                });
        }
    }
    protected override CustomPaletteRule Freeze(CustomPaletteRuleBuilder builder) =>
        new(builder.Target, builder.File, new ReadOnlyDictionary<int, PaletteColor>(builder.Palette));
    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("customPalettes", out var section)
        ? section! : throw new InvalidOperationException();
}

internal sealed class VideoRuleLoader : IdOnlyTypedRuleFamilyLoader<VideoRuleBuilder, VideoRule>
{
    public VideoRuleLoader() : base(GetSection()) { }
    protected override VideoRuleBuilder Create(string id) => new(id);
    protected override void Apply(VideoRuleBuilder builder, RulePropertyReader reader)
    {
        builder.UseUfoAudioSequence = reader.ReadBoolean("useUfoAudioSequence", false);
        builder.WinGame = reader.ReadBoolean("winGame", builder.WinGame);
        builder.LoseGame = reader.ReadBoolean("loseGame", builder.LoseGame);
        if (reader.TryGet("videos", out var videos)) builder.Videos.AddRange(PresentationYaml.ReadStrings(videos!));
        if (reader.TryGet("audioTracks", out var tracks)) builder.AudioTracks.AddRange(PresentationYaml.ReadStrings(tracks!));
        reader.ApplyMapping("slideshow", slideshow =>
        {
            builder.SlideshowMusicId = slideshow.ReadString("musicId", string.Empty);
            builder.SlideshowTransitionSeconds = slideshow.ReadInt32("transitionSeconds", 30);
            slideshow.ApplyMappingSequence("slides", slide => builder.Slides.Add(ReadSlide(slide)));
        });
    }
    protected override VideoRule Freeze(VideoRuleBuilder builder) => new(
        builder.UseUfoAudioSequence,
        builder.WinGame,
        builder.LoseGame,
        builder.Videos.AsReadOnly(),
        builder.AudioTracks.AsReadOnly(),
        new SlideshowHeader(builder.SlideshowMusicId, builder.SlideshowTransitionSeconds),
        builder.Slides.AsReadOnly());
    private static SlideshowSlide ReadSlide(RulePropertyReader reader)
    {
        var width = 320;
        var height = 200;
        var x = 0;
        var y = 0;
        if (reader.TryGet("captionSize", out var size)) (width, height) = PresentationYaml.ReadIntPair(size!);
        if (reader.TryGet("captionPos", out var position)) (x, y) = PresentationYaml.ReadIntPair(position!);
        return new SlideshowSlide(
            reader.ReadString("imagePath", string.Empty),
            reader.ReadString("caption", string.Empty),
            width,
            height,
            x,
            y,
            reader.ReadInt32("captionColor", int.MaxValue),
            reader.ReadInt32("transitionSeconds", 0),
            reader.ReadInt32("captionAlign", 0),
            reader.ReadInt32("captionVerticalAlign", 0));
    }
    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("cutscenes", out var section)
        ? section! : throw new InvalidOperationException();
}
