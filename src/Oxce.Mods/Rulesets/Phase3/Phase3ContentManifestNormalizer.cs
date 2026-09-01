using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Mods.Rulesets.Phase3;

public static class Phase3ContentManifestNormalizer
{
    public const int SchemaVersion = 1;

    public static byte[] NormalizeToUtf8Json(
        ModLoadPlan plan,
        Phase3ContentCatalog catalog,
        RulesetCatalogNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        options ??= new RulesetCatalogNormalizationOptions();
        options.Validate();

        var composed = RulesetComposer.Compose(plan);
        return NormalizeToUtf8Json(catalog, composed, options);
    }

    public static byte[] NormalizeToUtf8Json(
        ContentBuildSession build,
        RulesetCatalogNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        options ??= new RulesetCatalogNormalizationOptions();
        options.Validate();
        return NormalizeToUtf8Json(build.Catalog, build.ComposedRules, options);
    }

    public static byte[] NormalizeToUtf8Json(
        RuntimeContent content,
        ContentAuditArtifact auditArtifact,
        RulesetCatalogNormalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(auditArtifact);
        options ??= new RulesetCatalogNormalizationOptions();
        options.Validate();
        return NormalizeToUtf8Json(content.Catalog, auditArtifact.ComposedRules, options);
    }

    public static byte[] NormalizeToUtf8Json(
        Phase3ContentCatalog catalog,
        UnresolvedRuleCatalog composed,
        RulesetCatalogNormalizationOptions options)
    {
        var digest = ComputeComposedDigest(composed, options);

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("stage", catalog.Capabilities.Has(ContentLoadStage.Linked) ? "linked" : "typed");
            writer.WriteNumber("validationIssueCount", catalog.Validation.IssueCount);
            writer.WriteString("composedSha256", digest);
            writer.WriteStartArray("sections");

            WriteSection(writer, catalog.CampaignStart.Countries, options);
            WriteSection(writer, catalog.CampaignStart.GlobeLabels, options);
            WriteSection(writer, catalog.CampaignStart.Regions, options);
            WriteSection(writer, catalog.CampaignStart.Facilities, options);
            WriteSection(writer, catalog.EquipmentProduction.Crafts, options);
            WriteSection(writer, catalog.EquipmentProduction.CraftWeapons, options);
            WriteSection(writer, catalog.EquipmentProduction.ItemCategories, options);
            WriteSection(writer, catalog.Items.Items, options);
            WriteSection(writer, catalog.EquipmentProduction.WeaponSets, options);
            WriteSection(writer, catalog.EquipmentProduction.Ufos, options);
            WriteSection(writer, catalog.PersonnelTactical.Inventories, options);
            WriteSection(writer, catalog.TerrainDeployment.Terrains, options);
            WriteSection(writer, catalog.PersonnelTactical.Armors, options);
            WriteSection(writer, catalog.PersonnelTactical.Skills, options);
            WriteSection(writer, catalog.PersonnelTactical.Soldiers, options);
            WriteSection(writer, catalog.PersonnelTactical.Units, options);
            WriteSection(writer, catalog.TerrainDeployment.AlienRaces, options);
            WriteSection(writer, catalog.TerrainDeployment.EnviroEffects, options);
            WriteSection(writer, catalog.TerrainDeployment.StartingConditions, options);
            WriteSection(writer, catalog.TerrainDeployment.AlienDeployments, options);
            WriteSection(writer, catalog.EquipmentProduction.Research, options);
            WriteSection(writer, catalog.EquipmentProduction.Manufacture, options);
            WriteSection(writer, catalog.EquipmentProduction.ManufactureShortcuts, options);
            WriteSection(writer, catalog.PersonnelTactical.Bonuses, options);
            WriteSection(writer, catalog.PersonnelTactical.Transformations, options);
            WriteSection(writer, catalog.PersonnelTactical.Commendations, options);
            WriteSection(writer, catalog.MissionEvents.UfoTrajectories, options);
            WriteSection(writer, catalog.MissionEvents.AlienMissions, options);
            WriteSection(writer, catalog.MissionEvents.ArcScripts, options);
            WriteSection(writer, catalog.MissionEvents.EventScripts, options);
            WriteSection(writer, catalog.MissionEvents.Events, options);
            WriteSection(writer, catalog.MissionEvents.MissionScripts, options);
            WriteSection(writer, catalog.MissionEvents.AdhocScripts, options);
            WriteSection(writer, catalog.Presentation.SoundDefinitions, options);
            WriteSection(writer, catalog.Presentation.CustomPalettes, options);
            WriteSection(writer, catalog.Presentation.Interfaces, options);
            WriteSection(writer, catalog.Presentation.Videos, options);
            WriteSection(writer, catalog.Presentation.Music, options);
            WriteSection(writer, catalog.Presentation.ResourceConfigSoundDefinitions, options, "resourceConfig.soundDefs");

            writer.WriteEndArray();
            writer.WriteStartArray("specialSections");
            WriteSpecial(writer, "mapScripts", catalog.TerrainDeployment.MapScripts.Select(pair =>
                new SpecialEntry(pair.Key, pair.Value.LastUpdateSource)), options);
            WriteSpecial(writer, "MCDPatches", catalog.TerrainDeployment.McdPatches.Select(pair =>
                new SpecialEntry(pair.Key, pair.Value.LastUpdateSource)), options);
            WriteSpecial(writer, "ufopaedia", catalog.MissionEvents.Ufopaedia.Select(pair =>
                new SpecialEntry(pair.Key, pair.Value.LastUpdateSource)), options);
            WriteIds(writer, "extraStrings", catalog.Presentation.Special.Strings.Keys);
            WriteIds(writer, "extraSprites", catalog.Presentation.Special.Sprites.Keys);
            WriteIds(writer, "extraSounds", catalog.Presentation.Special.Sounds.Select(sound => sound.Type));
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        if (output.Length >= options.MaximumOutputBytes)
        {
            throw new InvalidOperationException(
                $"Phase 3 content manifest exceeds the {options.MaximumOutputBytes}-byte output limit.");
        }

        output.WriteByte((byte)'\n');
        return output.ToArray();
    }

    public static string NormalizeToJson(
        ModLoadPlan plan,
        Phase3ContentCatalog catalog,
        RulesetCatalogNormalizationOptions? options = null) =>
        Encoding.UTF8.GetString(NormalizeToUtf8Json(plan, catalog, options));

    public static string NormalizeToJson(
        ContentBuildSession build,
        RulesetCatalogNormalizationOptions? options = null) =>
        Encoding.UTF8.GetString(NormalizeToUtf8Json(build, options));

    private static void WriteSection<TRule>(
        Utf8JsonWriter writer,
        TypedRuleSection<TRule> section,
        RulesetCatalogNormalizationOptions options,
        string? name = null)
        where TRule : notnull
    {
        writer.WriteStartObject();
        writer.WriteString("name", name ?? section.Definition.Name);
        writer.WriteString("identityKey", section.Definition.IdentityKey);
        writer.WriteNumber("count", section.Rules.Count);
        writer.WriteStartArray("rules");
        foreach (var rule in section.Rules)
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WriteNumber("deferredPropertyCount", rule.DeferredProperties.Count);
            writer.WritePropertyName("creationSource");
            WriteSource(writer, rule.CreationSource, options);
            writer.WritePropertyName("lastUpdateSource");
            WriteSource(writer, rule.LastUpdateSource, options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        EnsureOutputLimit(writer, options.MaximumOutputBytes);
    }

    private static void WriteSpecial(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<SpecialEntry> entries,
        RulesetCatalogNormalizationOptions options)
    {
        var materialized = entries.ToArray();
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteNumber("count", materialized.Length);
        writer.WriteStartArray("rules");
        foreach (var entry in materialized)
        {
            writer.WriteStartObject();
            writer.WriteString("id", entry.Id);
            writer.WritePropertyName("lastUpdateSource");
            WriteSource(writer, entry.Source, options);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        EnsureOutputLimit(writer, options.MaximumOutputBytes);
    }

    private static void WriteIds(Utf8JsonWriter writer, string name, IEnumerable<string> ids)
    {
        var materialized = ids.ToArray();
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteNumber("count", materialized.Length);
        writer.WriteStartArray("ids");
        foreach (var id in materialized)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSource(
        Utf8JsonWriter writer,
        RuleOperationSource source,
        RulesetCatalogNormalizationOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("layer", source.LayerId);
        writer.WriteString("mod", source.ModId);
        writer.WriteString("path", options.NormalizeSourceName?.Invoke(source.SourcePath) ?? source.SourcePath);
        writer.WriteNumber("line", source.Span.Start.Line);
        writer.WriteNumber("column", source.Span.Start.Column);
        writer.WriteEndObject();
    }

    private static void EnsureOutputLimit(Utf8JsonWriter writer, int maximumOutputBytes)
    {
        if (writer.BytesCommitted + writer.BytesPending > maximumOutputBytes)
        {
            throw new InvalidOperationException(
                $"Phase 3 content manifest exceeds the {maximumOutputBytes}-byte output limit.");
        }
    }

    private static string ComputeComposedDigest(
        UnresolvedRuleCatalog catalog,
        RulesetCatalogNormalizationOptions options)
    {
        using var digest = new SemanticDigestWriter();
        digest.Append(catalog.Sections.Count);
        foreach (var section in catalog.Sections)
        {
            digest.Append(section.Definition.Name);
            digest.Append(section.Definition.IdentityKey);
            digest.Append(section.Rules.Count);
            foreach (var rule in section.Rules)
            {
                digest.Append(rule.Id);
                AppendSource(rule.CreationSource);
                AppendSource(rule.LastUpdateSource);
                digest.Append(rule.Operations.Count);
                foreach (var operation in rule.Operations)
                {
                    digest.Append((int)operation.Kind);
                    AppendSource(operation.Source);
                    AppendNode(operation.Node, 1);
                }
            }
        }

        return Convert.ToHexStringLower(digest.Finish());

        void AppendSource(RuleOperationSource source)
        {
            digest.Append(source.LayerId);
            digest.Append(source.ModId);
            digest.Append(options.NormalizeSourceName?.Invoke(source.SourcePath) ?? source.SourcePath);
            digest.Append(source.Span.Start.Line);
            digest.Append(source.Span.Start.Column);
        }

        void AppendNode(YamlNode node, int depth)
        {
            if (depth > options.MaximumDepth)
            {
                throw new YamlFormatException(
                    $"Phase 3 semantic digest exceeds the {options.MaximumDepth}-level limit.", node.Span);
            }

            digest.Append((int)node.Kind);
            digest.Append(node.Tag);
            switch (node)
            {
                case YamlNullNode nullNode:
                    digest.Append(nullNode.Spelling);
                    break;
                case YamlScalarNode scalar:
                    digest.Append(scalar.Value);
                    break;
                case YamlSequenceNode sequence:
                    digest.Append(sequence.Items.Count);
                    foreach (var item in sequence.Items) AppendNode(item, depth + 1);
                    break;
                case YamlMappingNode mapping:
                    digest.Append(mapping.Entries.Count);
                    foreach (var entry in mapping.Entries)
                    {
                        AppendNode(entry.Key, depth + 1);
                        AppendNode(entry.Value, depth + 1);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported YAML node type {node.GetType().Name}.");
            }
        }
    }

    private sealed class SemanticDigestWriter : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void Append(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _hash.AppendData(bytes);
        }

        public void Append(string? value)
        {
            if (value is null)
            {
                Append(-1);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            Append(bytes.Length);
            _hash.AppendData(bytes);
        }

        public byte[] Finish() => _hash.GetHashAndReset();

        public void Dispose() => _hash.Dispose();
    }

    private sealed record SpecialEntry(string Id, RuleOperationSource Source);
}
