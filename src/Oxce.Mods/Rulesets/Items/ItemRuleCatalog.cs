using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.Items;

public sealed class ItemRuleCatalog
{
    [System.Text.Json.Serialization.JsonConstructor]
    internal ItemRuleCatalog(TypedRuleSection<ItemRule> items)
    {
        Items = items;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public TypedRuleSection<ItemRule> Items { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static ItemRuleCatalog Load(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        diagnostics ??= NullDiagnosticSink.Instance;
        compositionOptions ??= new RulesetCompositionOptions();
        compositionOptions.Validate();
        var definition = RequiredDefinition();
        var unresolved = RulesetComposer.Compose(plan, [definition], diagnostics, compositionOptions);
        return Load(unresolved, diagnostics, typedOptions);
    }

    internal static ItemRuleCatalog Load(
        UnresolvedRuleCatalog unresolved,
        IDiagnosticSink diagnostics,
        TypedRuleLoadOptions? typedOptions)
    {
        if (!unresolved.TryGetSection("items", out var section)) throw new InvalidOperationException();
        return new ItemRuleCatalog(new ItemRuleLoader().Load(section!, diagnostics, typedOptions));
    }

    public ItemRuleValidation ValidateInternalRelationships(IDiagnosticSink? diagnostics = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance;
        var issues = new List<ItemRuleValidationIssue>();
        foreach (var item in Items.Rules)
        {
            ValidateWeaponAmmo(item);
            ValidateItemReference(item, item.Value.Values.NullableName("spawnItem"), "spawnItem");
            foreach (var slot in item.Value.CompatibleAmmo)
                foreach (var ammo in slot) ValidateItemReference(item, ammo, "compatibleAmmo");
            foreach (var transformation in item.Value.RecoveryTransformations)
            {
                if (!Items.TryGet(transformation.Key, out var target))
                {
                    ReportMissing(item, "recoveryTransformations", transformation.Key);
                    continue;
                }
                if (target!.Value.Values.Boolean("liveAlien"))
                    ReportInvalid(item, "recoveryTransformations", transformation.Key,
                        "Recovery transformations cannot target a live-alien item.");
                if (transformation.Value.Count == 0)
                    ReportInvalid(item, "recoveryTransformations", transformation.Key,
                        "Recovery transformation values cannot be empty.");
            }
        }
        return new ItemRuleValidation(Array.AsReadOnly(issues.ToArray()));

        void ValidateWeaponAmmo(TypedRule<ItemRule> item)
        {
            var battleType = item.Value.Values.GetInteger("battleType");
            if (battleType is not (1 or 3) || item.Value.Values.GetInteger("clipSize") != 0) return;
            foreach (var action in item.Value.Actions.Values)
            {
                if (action.AmmoSlot == -1 || item.Value.CompatibleAmmo[action.AmmoSlot].Count != 0) continue;
                ReportInvalid(item, "clipSize", null,
                    "A firearm or melee weapon with clipSize 0 must define compatible ammo or self-use ammo.");
                break;
            }
        }

        void ValidateItemReference(TypedRule<ItemRule> owner, string? id, string property)
        {
            if (string.IsNullOrEmpty(id) || Items.TryGet(id, out _)) return;
            ReportMissing(owner, property, id);
        }

        void ReportMissing(TypedRule<ItemRule> owner, string property, string id)
        {
            issues.Add(new ItemRuleValidationIssue(owner.Id, property, id, "Referenced item does not exist."));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.MissingRuleReference,
                DiagnosticSeverity.Error,
                $"Item '{owner.Id}' property '{property}' references missing item '{id}'.",
                owner.LastUpdateSource.Span,
                Context(owner, id)));
        }

        void ReportInvalid(TypedRule<ItemRule> owner, string property, string? id, string message)
        {
            issues.Add(new ItemRuleValidationIssue(owner.Id, property, id, message));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.InvalidRuleRelationship,
                DiagnosticSeverity.Error,
                $"Item '{owner.Id}' property '{property}' is invalid: {message}",
                owner.LastUpdateSource.Span,
                Context(owner, id)));
        }

        static DiagnosticContext Context(TypedRule<ItemRule> owner, string? relatedId) => new(
            owner.LastUpdateSource.LayerId,
            owner.LastUpdateSource.ModId,
            "items",
            owner.Id,
            relatedId);
    }

    private static RuleSectionDefinition RequiredDefinition() =>
        RuleSectionRegistry.TryGetNamed("items", out var section) ? section! : throw new InvalidOperationException();
}

public sealed record ItemRuleValidationIssue(string RuleId, string Property, string? RelatedId, string Message);

public sealed record ItemRuleValidation(IReadOnlyList<ItemRuleValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
