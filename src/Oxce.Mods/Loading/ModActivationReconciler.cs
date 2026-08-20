using Oxce.Core.Diagnostics;
using Oxce.Mods.Discovery;

namespace Oxce.Mods.Loading;

public sealed class ModActivationState
{
    internal ModActivationState(string activeMasterId, IEnumerable<ModActivation> activations)
    {
        ActiveMasterId = activeMasterId;
        Activations = Array.AsReadOnly(activations.ToArray());
    }

    public string ActiveMasterId { get; }

    public IReadOnlyList<ModActivation> Activations { get; }
}

public static class ModActivationReconciler
{
    public static ModActivationState Reconcile(
        ModCatalog catalog,
        IEnumerable<ModActivation> persistedActivations,
        string? preferredMasterId = null,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(persistedActivations);
        diagnostics ??= NullDiagnosticSink.Instance;
        var persisted = new List<ModActivation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activation in persistedActivations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(activation.Id);
            if (seen.Add(activation.Id) && catalog.Mods.ContainsKey(activation.Id))
            {
                persisted.Add(activation);
            }
            else if (!catalog.Mods.ContainsKey(activation.Id))
            {
                diagnostics.Report(new DiagnosticEvent(
                    ModDiagnosticCodes.MissingActivation,
                    DiagnosticSeverity.Information,
                    $"Removed persisted activation for unavailable mod '{activation.Id}'.",
                    context: new DiagnosticContext(ModId: activation.Id)));
            }
        }

        var masters = catalog.Mods.Values
            .Where(candidate => candidate.Metadata.IsMaster)
            .OrderBy(candidate => candidate.Metadata.Id, StringComparer.Ordinal)
            .ToArray();
        if (masters.Length == 0)
        {
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.NoAvailableMaster,
                DiagnosticSeverity.Critical,
                "No master mods are available."));
            throw new InvalidOperationException("No master mods are available.");
        }

        var availableMasterIds = masters.Select(candidate => candidate.Metadata.Id).ToHashSet(StringComparer.Ordinal);
        string? activeMasterId = null;
        if (!string.IsNullOrEmpty(preferredMasterId) && availableMasterIds.Contains(preferredMasterId))
        {
            activeMasterId = preferredMasterId;
        }
        else
        {
            var enabledMasters = persisted
                .Where(activation => activation.Enabled && availableMasterIds.Contains(activation.Id))
                .Select(activation => activation.Id)
                .ToArray();
            activeMasterId = enabledMasters.FirstOrDefault();
            if (enabledMasters.Length > 1)
            {
                diagnostics.Report(new DiagnosticEvent(
                    ModDiagnosticCodes.MultipleActiveMasters,
                    DiagnosticSeverity.Warning,
                    $"Multiple active masters were persisted; keeping '{activeMasterId}' and disabling the rest.",
                    context: new DiagnosticContext(ModId: activeMasterId)));
            }
        }

        activeMasterId ??= SelectDefaultMaster(availableMasterIds, masters);
        var persistedById = persisted.ToDictionary(activation => activation.Id, StringComparer.Ordinal);
        var orderedIds = persisted
            .Where(activation => availableMasterIds.Contains(activation.Id))
            .Select(activation => activation.Id)
            .Concat(masters.Select(candidate => candidate.Metadata.Id))
            .Concat(persisted.Where(activation => !availableMasterIds.Contains(activation.Id)).Select(activation => activation.Id))
            .Concat(catalog.Mods.Keys.Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);
        var reconciled = orderedIds.Select(id =>
        {
            var isMaster = availableMasterIds.Contains(id);
            var enabled = isMaster
                ? string.Equals(id, activeMasterId, StringComparison.Ordinal)
                : persistedById.TryGetValue(id, out var activation) && activation.Enabled;
            return new ModActivation(id, enabled);
        });
        return new ModActivationState(activeMasterId, reconciled);
    }

    private static string SelectDefaultMaster(
        HashSet<string> availableMasterIds,
        ModCandidate[] masters)
    {
        if (availableMasterIds.Contains("xcom1"))
        {
            return "xcom1";
        }

        if (availableMasterIds.Contains("xcom2"))
        {
            return "xcom2";
        }

        return masters[0].Metadata.Id;
    }
}
