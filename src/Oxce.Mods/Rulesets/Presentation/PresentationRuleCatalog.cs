using Oxce.Core.Diagnostics;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.Presentation;

public sealed class PresentationRuleCatalog
{
    private PresentationRuleCatalog(
        TypedRuleSection<InterfaceRule> interfaces,
        TypedRuleSection<MusicRule> music,
        TypedRuleSection<SoundDefinitionRule> soundDefinitions,
        TypedRuleSection<SoundDefinitionRule> resourceConfigSoundDefinitions,
        TypedRuleSection<CustomPaletteRule> customPalettes,
        TypedRuleSection<VideoRule> videos,
        PresentationSpecialRules special)
    {
        Interfaces = interfaces;
        Music = music;
        SoundDefinitions = soundDefinitions;
        ResourceConfigSoundDefinitions = resourceConfigSoundDefinitions;
        CustomPalettes = customPalettes;
        Videos = videos;
        Special = special;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public TypedRuleSection<InterfaceRule> Interfaces { get; }
    public TypedRuleSection<MusicRule> Music { get; }
    public TypedRuleSection<SoundDefinitionRule> SoundDefinitions { get; }
    public TypedRuleSection<SoundDefinitionRule> ResourceConfigSoundDefinitions { get; }
    public TypedRuleSection<CustomPaletteRule> CustomPalettes { get; }
    public TypedRuleSection<VideoRule> Videos { get; }
    public PresentationSpecialRules Special { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static PresentationRuleCatalog Load(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        diagnostics ??= NullDiagnosticSink.Instance;
        compositionOptions ??= new RulesetCompositionOptions();
        compositionOptions.Validate();
        var definitions = Names.Select(RequiredSection).ToArray();
        var documents = RulesetDocumentCatalog.Parse(plan, compositionOptions);
        var unresolved = RulesetComposer.Compose(documents, definitions, diagnostics, compositionOptions);
        return Load(unresolved, documents, diagnostics, compositionOptions, typedOptions);
    }

    internal static PresentationRuleCatalog Load(
        UnresolvedRuleCatalog unresolved,
        RulesetDocumentCatalog documents,
        IDiagnosticSink diagnostics,
        RulesetCompositionOptions compositionOptions,
        TypedRuleLoadOptions? typedOptions)
    {
        var resourceConfigDocuments = documents.Filter(static (mod, file) =>
        {
            var configured = mod.Metadata.ResourceConfigFile;
            return configured.Length != 0 && file.CanonicalPath == VirtualPath.NormalizeFile(configured);
        });
        var resourceConfig = RulesetComposer.Compose(
            resourceConfigDocuments,
            [RequiredSection("soundDefs")],
            diagnostics,
            compositionOptions);
        return new PresentationRuleCatalog(
            new InterfaceRuleLoader().Load(Required(unresolved, "interfaces"), diagnostics, typedOptions),
            new MusicRuleLoader().Load(Required(unresolved, "musics"), diagnostics, typedOptions),
            new SoundDefinitionRuleLoader().Load(Required(unresolved, "soundDefs"), diagnostics, typedOptions),
            new SoundDefinitionRuleLoader().Load(Required(resourceConfig, "soundDefs"), diagnostics, typedOptions),
            new CustomPaletteRuleLoader().Load(Required(unresolved, "customPalettes"), diagnostics, typedOptions),
            new VideoRuleLoader().Load(Required(unresolved, "cutscenes"), diagnostics, typedOptions),
            PresentationSpecialRulesComposer.Compose(documents, compositionOptions));
    }

    public PresentationResourceValidation ValidateDeclaredResources(
        VirtualFileCatalog files,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        diagnostics ??= NullDiagnosticSink.Instance;
        var missing = new List<DeclaredResourceReference>();

        foreach (var rule in SoundDefinitions.Rules)
        {
            if (rule.Value.File.Length != 0)
            {
                Check(rule.Value.File, [rule.Value.File, "SOUND/" + rule.Value.File], "soundDefs", rule.Id,
                    rule.LastUpdateSource);
            }
        }
        foreach (var rule in CustomPalettes.Rules)
        {
            if (rule.Value.File.Length != 0) Check(rule.Value.File, [rule.Value.File], "customPalettes", rule.Id,
                rule.LastUpdateSource);
        }
        foreach (var pair in Special.Sprites)
        {
            foreach (var declaration in pair.Value)
            {
                foreach (var file in declaration.Files.Values)
                {
                    Check(file, [file], "extraSprites", declaration.Type, declaration.Source);
                }
            }
        }
        foreach (var declaration in Special.Sounds)
        {
            foreach (var file in declaration.Files.Values)
            {
                Check(file, [file], "extraSounds", declaration.Type, declaration.Source);
            }
        }

        return new PresentationResourceValidation(Array.AsReadOnly(missing.ToArray()));

        void Check(
            string declaredPath,
            IReadOnlyList<string> candidates,
            string section,
            string id,
            RuleOperationSource source)
        {
            var found = declaredPath.EndsWith('/')
                ? files.List(declaredPath).Count != 0
                : candidates.Any(candidate => files.TryGet(candidate, out _));
            if (found) return;
            var reference = new DeclaredResourceReference(declaredPath, section, id, source);
            missing.Add(reference);
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.MissingDeclaredResource,
                DiagnosticSeverity.Error,
                $"Rule '{id}' in section '{section}' declares missing resource '{declaredPath}'.",
                source.Span,
                new DiagnosticContext(source.LayerId, source.ModId, section, id)));
        }
    }

    private static readonly string[] Names = ["interfaces", "musics", "soundDefs", "customPalettes", "cutscenes"];

    private static RuleSectionDefinition RequiredSection(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();

    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();

}

public sealed record DeclaredResourceReference(
    string Path,
    string Section,
    string RuleId,
    RuleOperationSource Source);

public sealed record PresentationResourceValidation(IReadOnlyList<DeclaredResourceReference> Missing)
{
    public bool IsValid => Missing.Count == 0;
}
