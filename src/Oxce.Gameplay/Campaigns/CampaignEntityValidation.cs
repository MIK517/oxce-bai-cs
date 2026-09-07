using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static SoldierPersonalState? RestorePersonal(SoldierPersonalState? state, RuntimeRuleCatalog rules)
    {
        if (state is null) return null;
        if (!float.IsFinite(state.Recovery)) throw new InvalidDataException("Soldier recovery must be finite.");
        if (state.Armor.Length != 0) rules.Armors.GetRequired(state.Armor);
        if (state.Commendations.Count > MaximumLogisticsLines || state.Commendations.Any(c => c.DecorationLevel < 0))
            throw new InvalidDataException("Soldier commendations exceed the supported range.");
        if (state.PreviousTransformations.Count > MaximumLogisticsLines || state.TransformationBonuses.Count > MaximumLogisticsLines)
            throw new InvalidDataException("Soldier transformation history exceeds the entry limit.");
        return state with
        {
            InitialStats = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>(state.InitialStats, StringComparer.Ordinal)),
            CurrentStats = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>(state.CurrentStats, StringComparer.Ordinal)),
            PreviousTransformations = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(state.PreviousTransformations, StringComparer.Ordinal)),
            TransformationBonuses = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(state.TransformationBonuses, StringComparer.Ordinal)),
            Commendations = Array.AsReadOnly(state.Commendations.ToArray()),
        };
    }

    private static CraftLogisticsState? RestoreCraftLogistics(CraftLogisticsState? state, RuntimeRuleCatalog rules)
    {
        if (state is null) return null;
        if (!double.IsFinite(state.Longitude) || !double.IsFinite(state.Latitude)) throw new InvalidDataException("Craft coordinates must be finite.");
        if (state.Fuel < 0 || state.Damage < 0 || state.Weapons.Count > 4 || state.Vehicles.Count > MaximumLogisticsLines)
            throw new InvalidDataException("Craft logistics values exceed their supported range.");
        foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
        {
            rules.CraftWeapons.GetRequired(weapon.RuleId);
            if (weapon.Ammo < 0) throw new InvalidDataException("Craft weapon ammunition cannot be negative.");
        }
        foreach (var vehicle in state.Vehicles)
        {
            rules.Items.GetRequired(vehicle.RuleId);
            if (vehicle.Size is <= 0 || vehicle.SpaceOccupied is < 0)
                throw new InvalidDataException("Vehicle dimensions exceed their supported range.");
        }
        foreach (var pair in state.Items)
        {
            rules.Items.GetRequired(pair.Key);
            if (pair.Value <= 0) throw new InvalidDataException("Craft item quantities must be positive.");
        }
        return state with
        {
            Weapons = CampaignSnapshot.ReadOnly(state.Weapons), Vehicles = CampaignSnapshot.ReadOnly(state.Vehicles),
            Items = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(state.Items, StringComparer.Ordinal)),
        };
    }

    private static void ValidateAssignments(BaseState state, RuntimeRuleCatalog rules)
    {
        var craftIds = state.Crafts.Select(c => (rules.Crafts.GetExternalId(c.Rule), c.Id)).ToHashSet();
        foreach (var soldier in state.Soldiers)
            if (soldier.Personal is { CraftType.Length: > 0 } personal && !craftIds.Contains((personal.CraftType, personal.CraftId)))
                throw new InvalidDataException("Soldier refers to a craft outside its base.");
        var incomingCrafts = state.Transfers.Where(t => !t.Delivered && t.Craft is not null)
            .Select(t => (t.Craft!.RuleId, t.Craft.Id, t.Hours)).ToHashSet();
        foreach (var transfer in state.Transfers)
            if (!transfer.Delivered && transfer.Soldier?.Personal is { CraftType.Length: > 0 } personal &&
                !incomingCrafts.Contains((personal.CraftType, personal.CraftId, transfer.Hours)))
                throw new InvalidDataException("Transferred crew must travel with its assigned craft.");
    }
}
