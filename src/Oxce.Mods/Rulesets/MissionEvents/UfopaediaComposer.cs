using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.MissionEvents;

internal static class UfopaediaComposer
{
    public static IReadOnlyDictionary<string, UfopaediaArticleRule> Compose(ModLoadPlan plan, RulesetCompositionOptions options)
    {
        var articles = new Dictionary<string, ArticleBuilder>(StringComparer.Ordinal); var listOrder = 0; var operations = 0;
        foreach (var group in plan.Groups) foreach (var file in group.Rulesets)
        {
            using var input = file.OpenRead(); var stream = YamlCompatibilityReader.Parse(input, file.SourcePath, options.Yaml);
            if (stream.Documents.Count == 0) continue;
            if (stream.Documents.Count != 1) throw Error(stream.Documents[1].Span, "Ruleset files must contain exactly one YAML document.");
            if (stream.Documents[0].Root is YamlNullNode) continue;
            if (stream.Documents[0].Root is not YamlMappingNode root) throw Error(stream.Documents[0].Root.Span, "Ruleset document root must be a mapping.");
            if (!root.TryGet("ufopaedia", out var node)) continue;
            if (node is not YamlSequenceNode sequence) throw Error(node!.Span, "Rule section 'ufopaedia' must be a sequence.");
            foreach (var item in sequence.Items)
            {
                if (++operations > options.MaximumRuleOperations) throw Error(item.Span, $"Ruleset input exceeds the {options.MaximumRuleOperations}-operation limit.");
                if (item is not YamlMappingNode map) throw Error(item.Span, "Ufopaedia entries must be mappings.");
                if (map.TryGet("delete", out var deleted)) { articles.Remove(YamlValueReader.ReadString(deleted!)); continue; }
                if (!map.TryGet("id", out var idNode)) throw Error(item.Span, "Ufopaedia entry requires id or delete.");
                var id = YamlValueReader.ReadString(idNode!); listOrder = checked(listOrder + 100);
                if (!articles.TryGetValue(id, out var article))
                {
                    if (!map.TryGet("type_id", out var typeNode)) throw Error(item.Span, $"Ufopaedia article '{id}' is missing type_id.");
                    var type = YamlValueReader.ReadInt32(typeNode!);
                    if (type is < 1 or > 19) throw Error(typeNode!.Span, $"Unsupported ufopaedia type_id {type}.");
                    articles[id] = article = new(id, type);
                }
                Apply(article, map, listOrder);
                article.Source = new(file.Provenance.LayerId, group.Mod.Metadata.Id, file.SourcePath, item.Span);
            }
        }
        return new ReadOnlyDictionary<string, UfopaediaArticleRule>(articles.ToDictionary(pair => pair.Key, pair => pair.Value.Freeze(), StringComparer.Ordinal));
    }

    private static void Apply(ArticleBuilder target, YamlMappingNode map, int defaultOrder)
    {
        target.Section = ReadString(map, "section", target.Section); target.Requires = ReadStrings(map, "requires", target.Requires);
        if (target.TypeId is >= 10 and <= 17 && map.TryGet("type_id", out var updatedType)) target.TypeId = YamlValueReader.ReadInt32(updatedType!);
        target.DisabledBy = ReadStrings(map, "disabledBy", target.DisabledBy); target.Hidden = ReadBool(map, "hiddenCommendation", target.Hidden);
        target.ListOrder = ReadInt(map, "listOrder", target.ListOrder); if (target.ListOrder == 0) target.ListOrder = defaultOrder;
        target.Pages[0].Title = target.Id; ApplyPage(target.Pages[0], map, target.Id);
        if (map.TryGet("pages", out var pagesNode))
        {
            if (pagesNode is not YamlSequenceNode pages) throw Error(pagesNode!.Span, $"Unsupported pages node for article '{target.Id}'.");
            var first = target.Pages[0]; target.Pages = Enumerable.Range(0, Math.Max(1, pages.Items.Count)).Select(_ => first with { }).ToList();
            for (var i = 0; i < pages.Items.Count; i++)
            { if (pages.Items[i] is not YamlMappingNode page) throw Error(pages.Items[i].Span, "Article pages must be mappings."); ApplyPage(target.Pages[i], page, target.Id); }
        }
        foreach (var key in new[] { "image_id", "weapon" }) target.Strings[key] = ReadString(map, key, target.Strings.GetValueOrDefault(key, ""));
        foreach (var key in new[] { "unit_mode", "psi_skill_mode", "text_width" }) if (map.TryGet(key, out var value)) target.Integers[key] = YamlValueReader.ReadInt32(value!);
        if (target.TypeId is >= 10 and <= 17 && !map.TryGet("text_width", out _)) target.Integers["text_width"] = 157;
        if (map.TryGet("align_bottom", out var align)) target.Booleans["align_bottom"] = YamlValueReader.ReadBoolean(align!);
        foreach (var key in new[] { "rect_stats", "rect_armor", "rect_text" }) if (map.TryGet(key, out var structured)) target.Structured[key] = structured!;
        target.CustomPalette = target.Strings.GetValueOrDefault("image_id", "").Contains("_CPAL", StringComparison.Ordinal);
    }
    private static void ApplyPage(UfopaediaPageBuilder target, YamlMappingNode map, string id)
    { target.Title = ReadString(map, "title", target.Title.Length == 0 ? id : target.Title); target.Text = ReadString(map, "text", target.Text); target.AmmoSlot = ReadInt(map, "ammoSlot", target.AmmoSlot); if (target.AmmoSlot is < 0 or > 3) throw Error(map.Span, "Article ammoSlot must be between 0 and 3."); }
    private static string ReadString(YamlMappingNode map, string key, string value) => map.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : value;
    private static int ReadInt(YamlMappingNode map, string key, int value) => map.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : value;
    private static bool ReadBool(YamlMappingNode map, string key, bool value) => map.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : value;
    private static List<string> ReadStrings(YamlMappingNode map, string key, List<string> value) => map.TryGet(key, out var node) ? MissionEventYaml.Strings(node!, key) : value;
    private static YamlFormatException Error(SourceSpan span, string message) => new(message, span);
    private static SourceSpan Unknown(string path) { var p = new SourcePosition(1, 1, 0); return new(path, p, p); }

    private sealed class ArticleBuilder(string id, int type)
    {
        public string Id = id, Section = ""; public int TypeId = type, ListOrder; public bool Hidden, CustomPalette;
        public List<string> Requires = [], DisabledBy = []; public List<UfopaediaPageBuilder> Pages = [new()];
        public Dictionary<string, string> Strings = new(StringComparer.Ordinal); public Dictionary<string, int> Integers = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Booleans = new(StringComparer.Ordinal); public Dictionary<string, YamlNode> Structured = new(StringComparer.Ordinal);
        public RuleOperationSource Source = null!;
        public UfopaediaArticleRule Freeze() => new(TypeId, Section, Requires.AsReadOnly(), DisabledBy.AsReadOnly(), Hidden, ListOrder,
            Array.AsReadOnly(Pages.Select(page => new UfopaediaPageRule(page.Title, page.Text, page.AmmoSlot)).ToArray()),
            MissionReadOnly.Dictionary(Strings), MissionReadOnly.Dictionary(Integers), MissionReadOnly.Dictionary(Booleans),
            MissionReadOnly.Dictionary(Structured), CustomPalette, Source);
    }
    private sealed record UfopaediaPageBuilder { public string Title = "", Text = ""; public int AmmoSlot; }
}
