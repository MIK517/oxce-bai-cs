using Oxce.Core.Diagnostics;
using Oxce.Mods.Discovery;

namespace Oxce.Mods.Loading;

public static class ModLoadPlanner
{
    public static ModLoadPlan Create(
        ModCatalog catalog,
        ModActivationState activationState,
        ModEngineIdentity engineIdentity,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(activationState);
        return Create(
            catalog,
            activationState.Activations,
            activationState.ActiveMasterId,
            engineIdentity,
            diagnostics);
    }

    public static ModLoadPlan Create(
        ModCatalog catalog,
        IEnumerable<ModActivation> activations,
        string activeMasterId,
        ModEngineIdentity engineIdentity,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(activations);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeMasterId);
        ArgumentNullException.ThrowIfNull(engineIdentity);
        diagnostics ??= NullDiagnosticSink.Instance;
        var ordered = new List<ModCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;
        if (!catalog.TryGet(activeMasterId, out var activeMaster) || !activeMaster!.Metadata.IsMaster)
        {
            throw new ArgumentException(
                $"Active master '{activeMasterId}' is not an available master mod.",
                nameof(activeMasterId));
        }

        foreach (var activation in activations)
        {
            if (!activation.Enabled)
            {
                continue;
            }

            if (!catalog.TryGet(activation.Id, out var candidate))
            {
                diagnostics.Report(new DiagnosticEvent(
                    ModDiagnosticCodes.MissingActivation,
                    DiagnosticSeverity.Warning,
                    $"Enabled mod '{activation.Id}' is not available.",
                    context: new DiagnosticContext(ModId: activation.Id)));
                continue;
            }

            if (!candidate!.Metadata.CanActivate(activeMasterId))
            {
                diagnostics.Report(Diagnostic(
                    ModDiagnosticCodes.InactiveForMaster,
                    DiagnosticSeverity.Information,
                    $"Mod '{candidate.Metadata.Id}' cannot activate for master '{activeMasterId}'.",
                    candidate,
                    activeMasterId));
                continue;
            }

            var chain = new Stack<ModCandidate>();
            var current = candidate;
            while (true)
            {
                chain.Push(current);
                if (current.Metadata.MasterId.Length == 0)
                {
                    break;
                }

                current = catalog.Mods[current.Metadata.MasterId];
            }

            while (chain.TryPop(out var item))
            {
                if (!seen.Add(item.Metadata.Id))
                {
                    continue;
                }

                if (item.Metadata.RequiredMasterVersion is not null &&
                    !activeMaster.Metadata.Version.Satisfies(item.Metadata.RequiredMasterVersion))
                {
                    diagnostics.Report(Diagnostic(
                        ModDiagnosticCodes.RequiredMasterVersion,
                        DiagnosticSeverity.Error,
                        $"Mod '{item.Metadata.Id}' requires master version '{item.Metadata.RequiredMasterVersion.Text}', " +
                        $"but active master '{activeMasterId}' is version '{activeMaster.Metadata.Version.Text}'.",
                        item,
                        activeMasterId));
                    valid = false;
                }

                if (!engineIdentity.Supports(item.Metadata))
                {
                    diagnostics.Report(Diagnostic(
                        ModDiagnosticCodes.RequiredExtendedEngine,
                        DiagnosticSeverity.Error,
                        $"Mod '{item.Metadata.Id}' requires engine '{item.Metadata.RequiredExtendedEngine}' " +
                        $"version '{item.Metadata.RequiredExtendedVersion}', but the application provides " +
                        $"'{engineIdentity.Name}' version '{engineIdentity.Version}'.",
                        item,
                        item.Metadata.RequiredExtendedEngine));
                    valid = false;
                }

                ordered.Add(item);
            }
        }

        var groups = ordered.Select(candidate => new ModLoadGroup(
            candidate,
            Array.AsReadOnly(candidate.Layers.SelectMany(layer => layer.Rulesets)
                .OrderByDescending(entry => entry.SourcePath, StringComparer.Ordinal)
                .ToArray())));
        return new ModLoadPlan(groups, valid);
    }

    private static DiagnosticEvent Diagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        ModCandidate candidate,
        string relatedId) => new(
            code,
            severity,
            message,
            context: new DiagnosticContext(
                LayerId: candidate.Layer.Provenance.LayerId,
                ModId: candidate.Metadata.Id,
                RelatedId: relatedId));
}
